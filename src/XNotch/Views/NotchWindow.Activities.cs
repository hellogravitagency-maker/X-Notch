using System.Windows;
using XNotch.Island;

namespace XNotch.Views;

/// <summary>Dynamic Island activity views (compact, minimal, alert, hud).</summary>
public partial class NotchWindow
{
    private bool _spotlightHasResults;

    private FrameworkElement? BuildIslandContent(IslandPresentation p) => null;

    private bool TickIslandExtras() => false;

    private void RefreshIslandActivities() { }

    private void PulseTimerRing() { }

    private void LayoutMinimalCircle(double offsetX, double offsetY, double w, double h) { }
}
