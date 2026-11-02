using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace XNotch.Models;

public partial class NotchViewModel : ObservableObject
{
    [ObservableProperty]
    private NotchState _notchState = NotchState.Closed;

    [ObservableProperty]
    private bool _isHovering;

    [ObservableProperty]
    private bool _isPinned;

    [ObservableProperty]
    private string _activeWorkspaceTitle = "Personal Workspace";

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
        IsPinned = false;
        NotchState = NotchState.Closed;
    }

    [RelayCommand]
    public void Focus()
    {
        IsPinned = true;
        NotchState = NotchState.Focus;
    }

    [RelayCommand]
    public void TogglePin()
    {
        IsPinned = !IsPinned;
        if (IsPinned)
        {
            NotchState = NotchState.Focus;
        }
        else if (NotchState == NotchState.Focus)
        {
            NotchState = NotchState.Open;
        }
    }

    [RelayCommand]
    public void Command()
    {
        NotchState = NotchState.Command;
    }

    [RelayCommand]
    public void Workspace()
    {
        NotchState = NotchState.Workspace;
    }

    [RelayCommand]
    public void Notification()
    {
        NotchState = NotchState.Notification;
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
