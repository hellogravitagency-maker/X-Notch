using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XNotch.Services;
using Debug = System.Diagnostics.Debug;

namespace XNotch.Views;

public partial class WebcamView : UserControl
{
    private WebcamService? _service;
    private bool _bound;

    public WebcamView()
    {
        InitializeComponent();
    }

    public async void Bind(WebcamService service)
    {
        if (_bound && _service == service && service.IsAvailable)
            return; // already bound to this service

        _service = service;

        if (!_bound)
        {
            service.FrameReady += OnFrameReady;
            _bound = true;
        }

        await service.StartAsync();
        Debug.WriteLine($"[WebcamView] Bind complete, IsAvailable={service.IsAvailable}");

        if (!service.IsAvailable)
            Visibility = Visibility.Collapsed;
        else
            Visibility = Visibility.Visible;
    }

    private void OnFrameReady(ImageSource frame)
    {
        WebcamImage.Source = frame;
    }

    public async void StopCamera()
    {
        if (_service != null)
        {
            _service.FrameReady -= OnFrameReady;
            _bound = false;
            await _service.StopAsync();
        }
    }
}
