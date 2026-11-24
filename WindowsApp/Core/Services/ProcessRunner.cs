using System.Collections;
using System.Diagnostics;
using System.Text;

namespace YPD.Core;

public sealed record ProcessResult(int Status, byte[] Stdout, IReadOnlyList<string> StderrTail, bool WasCancelled)
{
    /// <summary>yt-dlp가 남긴 마지막 "ERROR:" 줄, 없으면 stderr 마지막 줄.</summary>
    public string ErrorMessage
    {
        get
        {
            var line = StderrTail.LastOrDefault(l => l.StartsWith("ERROR:", StringComparison.Ordinal));
            if (line != null) return line["ERROR:".Length..].Trim();
            return StderrTail.Count > 0 ? StderrTail[^1] : L.F("The process exited with code {0}.", Status);
        }
    }
}

/// <summary>
/// 외부 프로그램을 실행하고 stdout/stderr를 줄 단위로 넘겨줍니다.
/// 파이프는 실행 중에 계속 비웁니다(끝난 뒤 읽으면 큰 출력에서 파이프가 꽉 차 멈춥니다).
/// 콜백은 백그라운드 스레드에서 불립니다.
/// </summary>
public static class ProcessRunner
{
    /// <summary>
    /// 취소하면 프로세스 트리 전체(ffmpeg 자식 포함)를 끝내고, 예외 대신 <see cref="ProcessResult.WasCancelled"/>가 true인 결과를 돌려줍니다.
    /// 실행 파일을 시작하지 못하면 예외를 던집니다.
    /// </summary>
    public static async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments,
                                                     IReadOnlyDictionary<string, string> environment,
                                                     bool collectStdout = false, Action<string>? onLine = null,
                                                     CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo(executable)
        {
            // 셸 없이 직접 실행하고 콘솔 창을 띄우지 않습니다. 인자는 따옴표가 든 값(--ppa)이 있어 목록으로만 넘깁니다.
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment.Clear();
        foreach (var (key, value) in environment) info.Environment[key] = value;

        var tail = new List<string>();
        var stdout = new LineReader(collectStdout, line => onLine?.Invoke(line));
        var stderr = new LineReader(false, line =>
        {
            lock (tail)
            {
                tail.Add(line);
                if (tail.Count > 20) tail.RemoveRange(0, tail.Count - 20);
            }
            onLine?.Invoke(line);
        });

        using var process = new Process { StartInfo = info };
        process.Start();
        ProcessRegistry.Add(process);
        bool cancelled = false;
        try
        {
            process.StandardInput.Close();
            var outTask = ReadAll(process.StandardOutput.BaseStream, stdout);
            var errTask = ReadAll(process.StandardError.BaseStream, stderr);
            using (cancellationToken.Register(() =>
            {
                Volatile.Write(ref cancelled, true);
                // 취소는 UI 스레드에서 불릴 수 있고, 트리 종료는 전체 프로세스 목록을 훑으므로 스레드 풀에서 합니다.
                ThreadPool.QueueUserWorkItem(_ => ProcessRegistry.Kill(process));
            }))
            {
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            // 자식의 자식(ffmpeg 등)이 파이프를 붙잡고 있으면 EOF가 오지 않을 수 있어, 종료 후 2초가 지나면 그냥 끝냅니다.
            await Task.WhenAny(Task.WhenAll(outTask, errTask), Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
            stdout.Flush();
            stderr.Flush();
            string[] lines;
            lock (tail) lines = [.. tail];
            return new ProcessResult(process.ExitCode, stdout.Collected, lines, Volatile.Read(ref cancelled));
        }
        finally
        {
            ProcessRegistry.Remove(process);
        }
    }

    // Windows 익명 파이프는 동기 핸들이라 읽기가 스레드를 붙잡습니다. 스레드 풀 대신 전용 스레드를 씁니다.
    private static Task ReadAll(Stream stream, LineReader reader) => Task.Factory.StartNew(() =>
    {
        var buffer = new byte[64 * 1024];
        try
        {
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) reader.Append(buffer.AsSpan(0, read));
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // 파이프가 끊기면 거기까지 읽은 것으로 끝냅니다.
        }
        finally
        {
            reader.Flush();
            stream.Dispose();
        }
    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    /// <summary>
    /// 자식 프로세스 환경. Python이 SystemRoot·TEMP 등을 필요로 하므로 부모 환경을 모두 물려주고,
    /// 도구 폴더를 PATH 앞에 둡니다. PYTHON… 변수는 시스템 Python으로 도는 yt-dlp용이고, PyInstaller판은 무시하므로 출력 인코딩은 --encoding으로 정합니다.
    /// 변수를 더 추가하면 FilenameBuilder.IsEnvironmentVariable에도 넣어야 합니다(파일 이름 속 $변수가 펼쳐지지 않게).
    /// </summary>
    public static Dictionary<string, string> Environment(IEnumerable<string> toolDirectories)
    {
        var env = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
            env[(string)entry.Key] = entry.Value as string ?? "";
        var dirs = toolDirectories.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (env.TryGetValue("PATH", out var path) && path.Length > 0) dirs.Add(path);
        env["PATH"] = string.Join(Path.PathSeparator, dirs);
        env["PYTHONIOENCODING"] = "utf-8";
        env["PYTHONUTF8"] = "1";
        return env;
    }
}

/// <summary>실행 중인 자식 프로세스 목록. 앱 종료 때 한 번에 끝냅니다.</summary>
public static class ProcessRegistry
{
    private static readonly HashSet<Process> Processes = [];
    private static bool shuttingDown;

    internal static void Add(Process process)
    {
        bool kill;
        lock (Processes)
        {
            Processes.Add(process);
            kill = shuttingDown;
        }
        // 종료 뒤에 대기열이 다음 항목을 시작해도 앱보다 오래 남지 않게 바로 끝냅니다.
        if (kill) Kill(process);
    }
    internal static void Remove(Process process) { lock (Processes) Processes.Remove(process); }

    /// <summary>
    /// Windows에는 SIGINT가 없어 프로세스 트리째 끝냅니다(ffmpeg 자식까지). 끝날 때까지 기다리지 않습니다.
    /// 앱을 끝낼 때만 부릅니다. 이후에 시작하는 프로세스도 바로 끝냅니다.
    /// </summary>
    public static void KillAll()
    {
        Process[] running;
        lock (Processes)
        {
            shuttingDown = true;
            running = [.. Processes];
        }
        foreach (var process in running) Kill(process);
    }

    internal static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // 이미 끝났거나 끝내는 중입니다. 트리 일부를 끝내지 못하면 AggregateException이 나는데,
            // 스레드 풀에서 새면 앱이 죽고 KillAll이 나머지를 놓치므로 모두 삼킵니다.
        }
    }
}

/// <summary>
/// 바이트를 모아 줄 단위(\n 또는 \r)로 잘라 UTF-8로 넘깁니다. yt-dlp 진행률은 \r로 끝나기도 합니다. 빈 줄은 넘기지 않습니다.
/// collect면 줄로 나누지 않고 모두 모읍니다(-J 출력용).
/// </summary>
internal sealed class LineReader(bool collect, Action<string> onLine)
{
    // 줄바꿈 없이 끝없이 쌓이는 출력에 대비한 상한.
    private const int MaxPending = 1 << 20;
    private readonly Lock gate = new();
    private readonly MemoryStream collected = new();
    private byte[] pending = new byte[4096];
    private int count;

    public byte[] Collected { get { lock (gate) return collected.ToArray(); } }

    public void Append(ReadOnlySpan<byte> data)
    {
        lock (gate)
        {
            if (collect)
            {
                collected.Write(data);
                return;
            }
            int newline;
            while ((newline = data.IndexOfAny((byte)'\n', (byte)'\r')) >= 0)
            {
                AddPending(data[..newline]);
                EmitPending();
                data = data[(newline + 1)..];
            }
            AddPending(data);
            if (count > MaxPending) count = 0;
        }
    }

    public void Flush()
    {
        lock (gate) EmitPending();
    }

    private void AddPending(ReadOnlySpan<byte> data)
    {
        if (count + data.Length > pending.Length) Array.Resize(ref pending, Math.Max(pending.Length * 2, count + data.Length));
        data.CopyTo(pending.AsSpan(count));
        count += data.Length;
    }

    private void EmitPending()
    {
        if (count == 0) return;
        string line = Encoding.UTF8.GetString(pending, 0, count);
        count = 0;
        onLine(line);
    }
}
