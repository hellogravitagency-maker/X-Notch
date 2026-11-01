using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XNotch.Models;
using XNotch.Plugins;

namespace MyXNotchPlugin;

/// <summary>
/// Example X-Notch plugin. Rename this class and update the metadata below.
/// 
/// Plugin types you can implement:
///   - IUIPlugin      → Inject UI into the notch (closed, open, settings, overlay)
///   - IServicePlugin → Run background services
///   - IAnimationPlugin → Override notch animations
///   - Or just IPlugin for basic lifecycle hooks
/// </summary>
[XNotchPlugin("com.yourname.myplugin", "My Plugin", "1.0.0", "Your Name")]
public class MyPlugin : PluginBase, IUIPlugin
{
    public override string Id => "com.yourname.myplugin";
    public override string Name => "My Plugin";
    public override string Version => "1.0.0";
    public override string Author => "Your Name";
    public override string Description => "A template X-Notch plugin.";

    private TextBlock? _closedLabel;
    private StackPanel? _openPanel;

    public override async Task InitializeAsync(IPluginContext context)
    {
        await base.InitializeAsync(context);

        // Build your UI elements here
        _closedLabel = new TextBlock
        {
            Text = "🔌",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White
        };

        _openPanel = new StackPanel { Margin = new Thickness(8) };
        _openPanel.Children.Add(new TextBlock
        {
            Text = "Hello from My Plugin!",
            FontSize = 14,
            Foreground = Brushes.White
        });
    }

    public UIElement? GetUIElement(UIPluginLocation location)
    {
        return location switch
        {
            UIPluginLocation.ClosedContent => _closedLabel,
            UIPluginLocation.OpenContent => _openPanel,
            _ => null
        };
    }

    public void OnNotchStateChanged(NotchState newState)
    {
        // React to notch state changes (Closed, Peeking, Open)
        Context?.Log($"Notch state changed to {newState}");
    }
}
