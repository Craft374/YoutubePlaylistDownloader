using System.Globalization;

namespace YPD.Core;

public static class Formatters
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];
    private static readonly int[] Decimals = [0, 0, 1, 2, 2];

    /// <summary>"1.2 MB/s". macOS ByteCountFormatter(.file)처럼 1000 단위이고, KB는 정수, MB는 소수 한 자리입니다.</summary>
    public static string? Speed(double? bytesPerSecond) => Speed(bytesPerSecond, CultureInfo.CurrentCulture);

    internal static string? Speed(double? bytesPerSecond, IFormatProvider culture)
    {
        if (bytesPerSecond is not { } value || !(value > 0) || !double.IsFinite(value)) return null;
        int unit = 0;
        // 반올림한 값이 1000이 되면 다음 단위로 올립니다("1000 KB" 대신 "1 MB").
        while (unit < Units.Length - 1 && Math.Round(value, Decimals[unit]) >= 1000)
        {
            value /= 1000;
            unit++;
        }
        return value.ToString("F" + Decimals[unit], culture) + " " + Units[unit] + "/s";
    }

    /// <summary>"3:05", 한 시간 이상이면 "1:02:03".</summary>
    public static string? Duration(double? seconds)
    {
        if (seconds is not { } s || !(s > 0) || !double.IsFinite(s)) return null;
        var t = TimeSpan.FromSeconds(Math.Floor(s));
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }

    /// <summary>천 단위 구분 기호를 넣은 숫자(조회수 등).</summary>
    public static string Number(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
