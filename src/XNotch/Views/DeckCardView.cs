using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QRCoder;
using XNotch.Services;
using Brush = System.Windows.Media.Brush;
using Cursors = System.Windows.Input.Cursors;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;
using XNotch.Helpers;

namespace XNotch.Views;

/// <summary>Phone Deck card: server state, Loc.T("Pair phone") (QR + code), paired phones with remove.</summary>
public sealed class DeckCardView : UserControl
{
    private readonly Grid _root = new() { Margin = new Thickness(6, 0, 6, 0) };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private NotchServices? _services;

    public DeckCardView()
    {
        Height = 128;
        Background = Brushes.Transparent;
        Content = _root;
        _tick.Tick += (_, _) => { if (_services?.DeckPairing?.IsOpen == true || _services?.DeckPairing?.Code != null) Refresh(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) { Refresh(); _tick.Start(); } else _tick.Stop(); };
    }

    public void Bind(NotchServices services)
    {
        _services = services;
        services.DeckChanged += () => Dispatcher.BeginInvoke(Refresh);
        Refresh();
    }

    private void Refresh()
    {
        _root.Children.Clear();
        _root.ColumnDefinitions.Clear();
        var s = _services;
        if (s == null) return;
        var deck = s.Deck;
        var pairing = s.DeckPairing;

        if (deck == null || deck.State != DeckState.On || pairing == null)
        {
            string msg = deck?.State switch
            {
                DeckState.PublicNetwork => Loc.T("Your network is set to Public, so the Deck stays off. Set it to Private in Windows network settings."),
                DeckState.NoNetwork => Loc.T("No network: connect to Wi-Fi or Ethernet."),
                DeckState.Failed => Loc.T("The Deck couldn't start (port {0} may be in use).", DeckServer.DefaultPort),
                _ => Loc.T("Phone Deck is off. Turn it on in settings to control music, the mouse and apps from your phone."),
            };
            _root.Children.Add(Text(msg, 11, "TextSecondaryBrush", wrap: true));
            return;
        }

        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _root.ColumnDefinitions.Add(new ColumnDefinition());

        // Left: QR + code while pairing, otherwise the Pair button
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        if (pairing.IsOpen && pairing.Code is string code)
        {
            left.Orientation = Orientation.Horizontal;
            left.Children.Add(new Image { Source = Qr($"{deck.Url}#code={code}"), Width = 104, Height = 104 });
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            info.Children.Add(Text(Loc.T("Scan with your phone, or open"), 10, "TextTertiaryBrush"));
            info.Children.Add(Text(deck.Url!, 11, "TextPrimaryBrush"));
            info.Children.Add(Text(Loc.T("and enter"), 10, "TextTertiaryBrush"));
            var codeText = Text($"{code[..3]} {code[3..]}", 22, "TextPrimaryBrush");
            codeText.FontFamily = new System.Windows.Media.FontFamily("Consolas");
            codeText.FontWeight = FontWeights.Bold;
            info.Children.Add(codeText);
            info.Children.Add(Button(Loc.T("Cancel"), () => pairing.Close()));
            left.Children.Add(info);
        }
        else
        {
            if (pairing.Code != null) pairing.Close(); // window expired
            left.Children.Add(Text(Loc.T("Phone Deck is on"), 12, "TextPrimaryBrush", bold: true));
            left.Children.Add(Text(deck.Url!, 10, "TextTertiaryBrush"));
            var pair = Button(Loc.T("Pair phone"), () => pairing.Open());
            pair.Margin = new Thickness(0, 8, 0, 0);
            left.Children.Add(pair);
        }
        _root.Children.Add(left);

        // Right: paired phones
        var list = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var devices = pairing.Devices;
        list.Children.Add(Text(devices.Count == 0 ? Loc.T("No phones paired yet") : Loc.T("PAIRED PHONES"), 9.5, "TextMutedBrush", bold: devices.Count > 0));
        foreach (var d in devices.OrderByDescending(d => d.LastSeen).Take(3))
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = Text($"{d.Name} · {Ago(d.LastSeen)}", 10.5, "TextPrimaryBrush");
            var remove = Button(Loc.T("Remove"), () => pairing.Revoke(d.Id));
            remove.ToolTip = Loc.T("This phone loses access immediately");
            Grid.SetColumn(remove, 1);
            row.Children.Add(name);
            row.Children.Add(remove);
            list.Children.Add(row);
        }
        Grid.SetColumn(list, 1);
        _root.Children.Add(list);
    }

    private TextBlock Text(string text, double size, string brush, bool wrap = false, bool bold = false) => new()
    {
        Text = text, FontSize = size, Foreground = (Brush)FindResource(brush), VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
    };

    private Border Button(string label, Action click)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 4, 10, 4), Cursor = Cursors.Hand,
            Background = (Brush)FindResource("SurfaceBrush"), HorizontalAlignment = HorizontalAlignment.Left,
            Child = Text(label, 10.5, "TextPrimaryBrush"),
        };
        b.PreviewMouseLeftButtonDown += (_, _) => b.Opacity = 0.7;
        b.MouseLeave += (_, _) => b.Opacity = 1;
        b.MouseLeftButtonUp += (_, _) => { b.Opacity = 1; click(); Refresh(); };
        return b;
    }

    private static BitmapImage Qr(string text)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8, [0, 0, 0, 255], [255, 255, 255, 255]);
        var img = new BitmapImage();
        img.BeginInit();
        img.StreamSource = new MemoryStream(png);
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.EndInit();
        img.Freeze();
        return img;
    }

    private static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        return d.TotalMinutes < 2 ? Loc.T("now") : d.TotalHours < 1 ? Loc.T("{0} min ago", (int)d.TotalMinutes) : d.TotalDays < 1 ? Loc.T("{0} h ago", (int)d.TotalHours) : t.ToString("d MMM");
    }
}
