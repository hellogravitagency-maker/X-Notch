using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XNotch.Models;
using XNotch.Services;
using Brush = System.Windows.Media.Brush;

namespace XNotch.Views;

public partial class MusicLiveActivity : UserControl
{
    private MediaService? _mediaService;
    private AudioCaptureService? _audioCaptureService;
    private bool _userEnabled = true;
    private bool _isPeeking;
    private volatile bool _isComponentVisible;

    /// <summary>Reflects the ShowMusicControls setting; gates self-show on media changes.</summary>
    public bool UserEnabled
    {
        get => _userEnabled;
        set
        {
            _userEnabled = value;
            if (_mediaService != null)
                UpdateCompact(_mediaService.MediaInfo, null);
        }
    }

    /// <summary>
    /// When true, smoothly switches to the interactive capsule layout with micro transport controls.
    /// </summary>
    public bool IsPeeking
    {
        get => _isPeeking;
        set
        {
            if (_isPeeking != value)
            {
                _isPeeking = value;
                CompactLayout.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
                PeekLayout.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    public MusicLiveActivity()
    {
        InitializeComponent();
    }

    public void Bind(MediaService mediaService, AudioCaptureService audioCaptureService)
    {
        _mediaService = mediaService;
        _audioCaptureService = audioCaptureService;

        var info = mediaService.MediaInfo;
        info.PropertyChanged += (_, e) =>
        {
            Dispatcher.Invoke(() => UpdateCompact(info, e.PropertyName));
        };

        audioCaptureService.SpectrumUpdated += () =>
        {
            if (!_isComponentVisible) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (!_isComponentVisible) return;
                if (audioCaptureService.SpectrumData != null)
                {
                    if (CompactVisualizer.IsVisible)
                        CompactVisualizer.UpdateSpectrum(audioCaptureService.SpectrumData);
                    if (PeekVisualizer.IsVisible)
                        PeekVisualizer.UpdateSpectrum(audioCaptureService.SpectrumData);
                }
            });
        };

        audioCaptureService.WaveformUpdated += () =>
        {
            if (!_isComponentVisible) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (!_isComponentVisible) return;
                if (PeekVisualizer.IsVisible)
                    PeekVisualizer.UpdateWaveform(audioCaptureService.WaveformRingBuffer);
            });
        };

        UpdateCompact(info, null);
    }

    private void UpdateCompact(MediaInfo info, string? propertyName)
    {
        // Gate visibility, but keep content in sync even while hidden
        _isComponentVisible = _userEnabled && info.HasMedia && info.IsPlaying;
        Visibility = _isComponentVisible
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (propertyName == null || propertyName == nameof(info.AlbumArt))
        {
            CompactAlbumArt.Source = info.AlbumArt;
            PeekAlbumArt.Source = info.AlbumArt;
        }

        if (propertyName == null || propertyName == nameof(info.Title) || propertyName == nameof(info.LyricsText))
        {
            if (info.HasLyrics && !string.IsNullOrWhiteSpace(info.LyricsText))
            {
                CompactTitle.Text = info.LyricsText;
            }
            else if (info.IsInstrumental)
            {
                CompactTitle.Text = "♪ " + info.Title;
            }
            else
            {
                CompactTitle.Text = info.Title;
            }

            PeekTitle.Text = string.IsNullOrWhiteSpace(info.Title) ? "Unknown Track" : info.Title;
        }

        if (propertyName == null || propertyName == nameof(info.Artist))
        {
            PeekArtist.Text = string.IsNullOrWhiteSpace(info.Artist) ? "Unknown Artist" : info.Artist;
        }

        if (propertyName == null || propertyName == nameof(info.IsPlaying))
        {
            MiniPlayIcon.Text = info.IsPlaying ? "⏸" : "▶";
            if (!info.IsPlaying)
            {
                CompactVisualizer.ResetToIdle();
                PeekVisualizer.ResetToIdle();
            }
        }

        if (propertyName == null || propertyName == nameof(info.DominantColor))
        {
            var color = info.DominantColor;
            double brightness = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;

            if (brightness > 0.15 && color.A > 0)
            {
                // Create vibrant Apple Music dynamic gradient
                var topColor = Color.FromRgb(
                    (byte)Math.Min(255, (int)(color.R * 1.35 + 30)),
                    (byte)Math.Min(255, (int)(color.G * 1.35 + 30)),
                    (byte)Math.Min(255, (int)(color.B * 1.35 + 30)));
                var grad = new LinearGradientBrush(topColor, color, new Point(0.5, 0), new Point(0.5, 1));
                grad.Freeze();
                CompactVisualizer.BarBrush = grad;
                PeekVisualizer.BarBrush = grad;

                if (PeekArtBorder != null)
                {
                    PeekArtBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(140, color.R, color.G, color.B));
                }
            }
            else
            {
                // Fallback to iconic Apple Music sunset gradient
                CompactVisualizer.BarBrush = null;
                PeekVisualizer.BarBrush = null;
                if (PeekArtBorder != null)
                {
                    PeekArtBorder.BorderBrush = (Brush)FindResource("SeparatorBrush");
                }
            }
        }
    }

    private async void OnMiniPrevClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_mediaService != null)
        {
            await _mediaService.PreviousAsync();
        }
    }

    private async void OnMiniPlayClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_mediaService != null)
        {
            await _mediaService.PlayPauseAsync();
        }
    }

    private async void OnMiniNextClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_mediaService != null)
        {
            await _mediaService.NextAsync();
        }
    }
}
