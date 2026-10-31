using System;
using System.Windows;

namespace VoltixIptvWindows;

public partial class PlayerWindow : Window
{
    public PlayerWindow(string url, string title)
    {
        InitializeComponent();
        Title = title;
        try
        {
            Video.Source = new Uri(url);
            Video.Play();
        }
        catch { ShowError(); }
    }

    private void Video_MediaFailed(object sender, ExceptionRoutedEventArgs e) => ShowError();

    private void ShowError()
    {
        Video.Stop();
        ErrorText.Visibility = Visibility.Visible;
    }
}
