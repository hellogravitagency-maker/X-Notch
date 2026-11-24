using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using XNotch.Helpers;
using XNotch.Services;

namespace XNotch.Views;

/// <summary>
/// Click-through chat overlay (bottom-left of the primary screen). New lines rise in;
/// each fades out 30 s later. Never takes focus, so it's safe over games.
/// </summary>
public sealed class ChatOverlayWindow : Window
{
    private const int MaxLines = 8;
    private static readonly TimeSpan LineLife = TimeSpan.FromSeconds(30);
    private readonly StackPanel _lines = new() { VerticalAlignment = VerticalAlignment.Bottom };

    public ChatOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 380;
        Height = 320;
        Content = _lines;
        SourceInitialized += (_, _) =>
        {
            WindowHelper.MakeOverlayWindow(this);
            WindowHelper.SetClickThrough(this, true);
            var wa = SystemParameters.WorkArea;
            Left = wa.Left + 16;
            Top = wa.Bottom - Height - 16;
        };
    }

    public void Add(ChatMessage m)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = Brushes.White };
        text.Inlines.Add(new System.Windows.Documents.Run(m.User + "  ") { FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(StreamCardView.UserColour(m)) });
        text.Inlines.Add(new System.Windows.Documents.Run(m.Text));
        var translate = new TranslateTransform();
        var line = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 5, 10, 6), Margin = new Thickness(0, 3, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            Child = text, RenderTransform = translate, Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 0.4 },
        };
        _lines.Children.Add(line);
        while (_lines.Children.Count > MaxLines) _lines.Children.RemoveAt(0);

        bool motion = SystemParameters.ClientAreaAnimation;
        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
        line.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        if (motion) translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });

        var expire = new DispatcherTimer { Interval = LineLife };
        expire.Tick += (_, _) =>
        {
            expire.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(400));
            fade.Completed += (_, _) => _lines.Children.Remove(line);
            line.BeginAnimation(OpacityProperty, fade);
        };
        expire.Start();
    }
}
