using LibVLCSharp.Shared;
using System.Windows;

namespace VoltixIptvWindows;

public partial class PlayerWindow : Window
{
    private readonly LibVLC _libVLC;
    private readonly MediaPlayer _mediaPlayer;

    public PlayerWindow(string url, string title)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;

        try
        {
            Core.Initialize();
            _libVLC = new LibVLC("--network-caching=3000", "--file-caching=1000");
            _mediaPlayer = new MediaPlayer(_libVLC);
            _mediaPlayer.EncounteredError += (_, _) => Dispatcher.Invoke(() => ShowError());
            Video.MediaPlayer = _mediaPlayer;
            _mediaPlayer.Play(new Media(_libVLC, new Uri(url), FromType.FromLocation));
        }
        catch
        {
            ShowError();
        }

        Closed += (_, _) =>
        {
            try { _mediaPlayer.Stop(); } catch { }
            try { _mediaPlayer.Dispose(); } catch { }
            try { _libVLC.Dispose(); } catch { }
        };
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
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