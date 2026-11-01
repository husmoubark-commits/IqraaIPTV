using LibVLCSharp.Shared;
using System.Windows;

namespace VoltixIptvWindows;

public partial class PlayerWindow : Window
{
    private LibVLC? _libVLC;
    private MediaPlayer? _mediaPlayer;
    private readonly string _url;
    private readonly string? _fallbackUrl;
    private bool _triedFallback;

    public PlayerWindow(string url, string title, string? fallbackUrl = null)
    {
        InitializeComponent();
        _url = url;
        _fallbackUrl = fallbackUrl;
        Title = title;
        TitleText.Text = title;

        try
        {
            Core.Initialize();
            _libVLC = new LibVLC("--network-caching=5000", "--file-caching=1500", "--http-reconnect");
            _mediaPlayer = new MediaPlayer(_libVLC);
            _mediaPlayer.EncounteredError += (_, _) => Dispatcher.Invoke(HandlePlaybackError);
            Video.MediaPlayer = _mediaPlayer;
            PlayUrl(_url);
        }
        catch
        {
            ShowError();
        }

        Closed += (_, _) =>
        {
            try { _mediaPlayer?.Stop(); } catch { }
            try { _mediaPlayer?.Dispose(); } catch { }
            try { _libVLC?.Dispose(); } catch { }
        };
    }

    private void PlayUrl(string url)
    {
        if (_libVLC is null || _mediaPlayer is null) return;
        using var media = new Media(_libVLC, url, FromType.FromLocation);
        media.AddOption(":network-caching=5000");
        media.AddOption(":http-reconnect");
        media.AddOption(":http-user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0.0.0 Safari/537.36");
        _mediaPlayer.Play(media);
    }

    private void HandlePlaybackError()
    {
        if (!_triedFallback && !string.IsNullOrWhiteSpace(_fallbackUrl))
        {
            _triedFallback = true;
            ErrorOverlay.Visibility = Visibility.Collapsed;
            try { PlayUrl(_fallbackUrl); return; } catch { }
        }
        ShowError();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer is null)
        {
            ShowError();
            return;
        }
        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
            PlayPauseButton.Content = "استئناف";
        }
        else
        {
            _mediaPlayer.Play();
            PlayPauseButton.Content = "إيقاف مؤقت";
            ErrorOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void ClosePlayer_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowError()
    {
        ErrorOverlay.Visibility = Visibility.Visible;
    }
}
