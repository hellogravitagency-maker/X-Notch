using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace XNotch.Models;

public partial class NotchViewModel : ObservableObject
{
    [ObservableProperty]
    private NotchState _notchState = NotchState.Closed;

    [ObservableProperty]
    private bool _isHovering;

    [RelayCommand]
    public void Open()
    {
        NotchState = NotchState.Open;
    }

    [RelayCommand]
    public void Peek()
    {
        NotchState = NotchState.Peeking;
    }

    [RelayCommand]
    public void Close()
    {
        NotchState = NotchState.Closed;
    }

    [RelayCommand]
    public void Toggle()
    {
        if (NotchState == NotchState.Closed)
            Open();
        else
            Close();
    }
}
