using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using XNotch.Services;
using Brush = System.Windows.Media.Brush;
using Cursors = System.Windows.Input.Cursors;
using Orientation = System.Windows.Controls.Orientation;
using XNotch.Helpers;

namespace XNotch.Views;

/// <summary>Stream card: OBS (live/rec state, timecode, start/stop, scenes) + live chat feed.</summary>
public sealed class StreamCardView : UserControl
{
    private static readonly Color Live = Color.FromRgb(0xFF, 0x45, 0x3A);
    private readonly Grid _root = new() { Margin = new Thickness(6, 0, 6, 0) };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private ObsService? _obs;
    private ChatService? _chat;

    public StreamCardView()
    {
        Height = 128;
        Background = Brushes.Transparent;
        Content = _root;
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        _tick.Tick += (_, _) => { if (_obs?.LiveSince != null) Refresh(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) { Refresh(); _tick.Start(); } else _tick.Stop(); };
    }

    public void Bind(ObsService obs, ChatService chat)
    {
        _obs = obs;
        _chat = chat;
        obs.StateChanged += () => Dispatcher.BeginInvoke(Refresh);
        chat.MessageReceived += _ => Dispatcher.BeginInvoke(Refresh);
        chat.StateChanged += () => Dispatcher.BeginInvoke(Refresh);
        Refresh();
    }

    private void Refresh()
    {
        if (_obs == null || _chat == null || !IsVisible) return;
        _root.Children.Clear();

        // ── OBS ──
        var obs = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        string state; Color? colour = null;
        if (_obs.AuthFailed) state = Loc.T("Wrong OBS password");
        else if (!_obs.Connected) state = Loc.T("OBS isn't running");
        else if (_obs.Streaming || _obs.Recording)
        {
            var t = _obs.LiveSince is DateTime since ? DateTime.Now - since : TimeSpan.Zero;
            state = $"● {(_obs.Streaming ? Loc.T("LIVE") : Loc.T("REC"))}  {(t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss"))}";
            colour = Live;
        }
        else state = Loc.T("OBS ready");
        var stateText = Label(state, 12, bold: true);
        if (colour is Color c) stateText.Foreground = new SolidColorBrush(c);
        obs.Children.Add(stateText);

        if (_obs.Connected)
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
            buttons.Children.Add(Pill(_obs.Streaming ? Loc.T("End stream") : Loc.T("Go live"), _obs.Streaming, () => _ = _obs.ToggleStream()));
            buttons.Children.Add(Pill(_obs.Recording ? Loc.T("Stop rec") : Loc.T("Record"), _obs.Recording, () => _ = _obs.ToggleRecord()));
            obs.Children.Add(buttons);

            var scenes = new WrapPanel();
            foreach (var name in _obs.Scenes.Take(6))
                scenes.Children.Add(Pill(name, name == _obs.CurrentScene, () => _ = _obs.SetScene(name), small: true));
            obs.Children.Add(scenes);
        }
        _root.Children.Add(obs);

        // ── Chat ──
        var chat = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        var msgs = _chat.Messages.TakeLast(6).ToList();
        if (msgs.Count == 0)
        {
            string hint = _chat.TwitchConnected || _chat.YouTubeConnected ? Loc.T("Chat is quiet") : Loc.T("Add a Twitch channel in settings to see chat");
            chat.VerticalAlignment = VerticalAlignment.Center;
            chat.Children.Add(Label(hint, 10.5, muted: true));
        }
        foreach (var m in msgs)
        {
            var line = new TextBlock { FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 1) };
            line.Inlines.Add(new System.Windows.Documents.Run(m.User + " ")
            {
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(UserColour(m)),
            });
            line.Inlines.Add(new System.Windows.Documents.Run(m.Text) { Foreground = (Brush)FindResource("TextPrimaryBrush") });
            chat.Children.Add(line);
        }
        Grid.SetColumn(chat, 1);
        _root.Children.Add(chat);
    }

    public static Color UserColour(ChatMessage m)
    {
        if (m.Color != null)
        {
            try { return (Color)System.Windows.Media.ColorConverter.ConvertFromString(m.Color); } catch { }
        }
        // Stable per-user colour when the platform gives none.
        Color[] palette = [Color.FromRgb(0x64, 0xD2, 0xFF), Color.FromRgb(0xFF, 0x9F, 0x0A), Color.FromRgb(0x30, 0xD1, 0x58), Color.FromRgb(0xBF, 0x5A, 0xF2), Color.FromRgb(0xFF, 0x37, 0x5F)];
        return palette[(int)((uint)StringComparer.Ordinal.GetHashCode(m.User) % palette.Length)];
    }

    private TextBlock Label(string text, double size, bool bold = false, bool muted = false) => new()
    {
        Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = muted ? TextWrapping.Wrap : TextWrapping.NoWrap,
        Foreground = (Brush)FindResource(muted ? "TextTertiaryBrush" : "TextPrimaryBrush"),
    };

    private Border Pill(string text, bool on, Action click, bool small = false)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(small ? 7 : 10, 3, small ? 7 : 10, 3),
            Margin = new Thickness(0, 0, 5, 4), Cursor = Cursors.Hand,
            Background = on ? new SolidColorBrush(small ? Color.FromRgb(0x0A, 0x84, 0xFF) : Live) : (Brush)FindResource("SurfaceBrush"),
            Child = new TextBlock
            {
                Text = text, FontSize = small ? 9.5 : 10.5, MaxWidth = 90, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = on ? Brushes.White : (Brush)FindResource("TextPrimaryBrush"),
            },
        };
        b.PreviewMouseLeftButtonDown += (_, _) => b.Opacity = 0.7;
        b.MouseLeave += (_, _) => b.Opacity = 1;
        b.MouseLeftButtonUp += (_, _) => { b.Opacity = 1; click(); };
        return b;
    }
}
