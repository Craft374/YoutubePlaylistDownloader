using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace YPD.Core;

/// <summary>값이 실제로 바뀔 때만 PropertyChanged를 보냅니다. 같은 값을 다시 써도 화면을 다시 그리지 않게 합니다.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
