using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using XNotch.Services;
using Brush = System.Windows.Media.Brush;
using Cursors = System.Windows.Input.Cursors;
using XNotch.Helpers;

namespace XNotch.Views;

/// <summary>Capture card: Snip, Record, Text from screen, Colour picker.</summary>
public sealed class CaptureCardView : UserControl
{
    private readonly UniformGrid _grid = new() { Rows = 1, Columns = 4, Margin = new Thickness(4, 6, 4, 0), VerticalAlignment = VerticalAlignment.Top };

    /// <summary>Asks the island to get out of the way before the screen is captured.</summary>
    public event Action? CloseIslandRequested;

    /// <summary>Short result to show on the island (e.g. "Copied #0A84FF").</summary>
    public event Action<string, string>? Result;

    public CaptureCardView()
    {
        Height = 128;
        Background = Brushes.Transparent;
        Content = _grid;
        Loaded += (_, _) => { if (_grid.Children.Count == 0) Build(); };
    }

    private void Build()
    {
        Tile("", Loc.T("Snip"), Loc.T("Win+Shift+S · saves to the shelf"), () => { CloseIslandRequested?.Invoke(); CaptureService.Snip(); });
        Tile("", Loc.T("Record"), Loc.T("Win+Shift+R · recording lands on the shelf"), () => { CloseIslandRequested?.Invoke(); CaptureService.Record(); });
        Tile("", Loc.T("Text"), Loc.T("Snip an area and copy its text (Windows OCR)"), async () =>
        {
            CloseIslandRequested?.Invoke();
            string? text;
            try { text = await CaptureService.TextFromScreenAsync(TimeSpan.FromSeconds(60)); }
            catch (Exception ex) { Result?.Invoke(Loc.T("Text capture failed"), ex.Message); return; }
            if (text == null) return; // cancelled
            Result?.Invoke(text.Length == 0 ? Loc.T("No text found") : Loc.T("Text copied"),
                text.Length == 0 ? Loc.T("Windows found no words in that area") : text.ReplaceLineEndings(" "));
        });
        Tile("", Loc.T("Colour"), Loc.T("Pick any colour on screen · Esc cancels"), () =>
        {
            CloseIslandRequested?.Invoke();
            // Let the island finish closing so it isn't in the frozen frame.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
            {
                var picker = new ColourPickerWindow();
                picker.ShowDialog();
                if (picker.Picked is Color c)
                {
                    var hex = ColourPickerWindow.Hex(c);
                    try { System.Windows.Clipboard.SetText(hex); } catch { }
                    Result?.Invoke($"Copied {hex}", $"rgb({c.R}, {c.G}, {c.B})");
                }
            });
        });
    }

    private void Tile(string glyph, string label, string tip, Action click)
    {
        var scale = new ScaleTransform(1, 1);
        var tile = new Border
        {
            CornerRadius = new CornerRadius(14), Margin = new Thickness(4, 0, 4, 0), Height = 88,
            Background = (Brush)FindResource("SurfaceBrush"), Cursor = Cursors.Hand, ToolTip = tip,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale,
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)FindResource("TextPrimaryBrush") },
                    new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)FindResource("TextSecondaryBrush") },
                },
            },
        };

        void Press(double to)
        {
            if (!SystemParameters.ClientAreaAnimation) return;
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(120)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        tile.PreviewMouseLeftButtonDown += (_, _) => Press(0.96);
        tile.MouseLeave += (_, _) => Press(1);
        tile.MouseLeftButtonUp += (_, _) => { Press(1); click(); };
        _grid.Children.Add(tile);
    }
}
