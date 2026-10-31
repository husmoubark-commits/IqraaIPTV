using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IoPath = System.IO.Path;

namespace VoltixIptvWindows;

public partial class MainWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly string CacheDir = IoPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoltixIptvWindows");
    private readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private JsonObject _cache = new();
    private string _server = "", _user = "", _pass = "";
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        Directory.CreateDirectory(CacheDir);
        LoadSettings();
        LoadCache();
    }

    private string CacheFile => IoPath.Combine(CacheDir, "catalog.json");
    private string SettingsFile => IoPath.Combine(CacheDir, "settings.json");

    private void LoadSettings()
    {
        try
        {
            var o = JsonNode.Parse(File.ReadAllText(SettingsFile))?.AsObject();
            _server = o?["server"]?.GetValue<string>() ?? "";
            _user = o?["user"]?.GetValue<string>() ?? "";
            _pass = o?["pass"]?.GetValue<string>() ?? "";
            ServerBox.Text = _server; UserBox.Text = _user; PassBox.Password = _pass;
        }
        catch { }
    }

    private void LoadCache()
    {
        try
        {
            _cache = JsonNode.Parse(File.ReadAllText(CacheFile))?.AsObject() ?? new();
            RenderAll();
            StatusText.Text = "تم تحميل البيانات المحفوظة. يمكنك التصفح بدون إنترنت.";
        }
        catch { StatusText.Text = "أدخل بيانات الاشتراك ثم اضغط دخول / تحديث"; }
    }

    private void SaveSettings()
    {
        _server = ServerBox.Text.Trim().TrimEnd('/');
        _user = UserBox.Text.Trim();
        _pass = PassBox.Password;
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new { server = _server, user = _user, pass = _pass }));
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SaveSettings();
        if (string.IsNullOrWhiteSpace(_server) || string.IsNullOrWhiteSpace(_user) || string.IsNullOrWhiteSpace(_pass))
        {
            StatusText.Text = "اكتب رابط السيرفر واسم المستخدم وكلمة المرور أولًا.";
            return;
        }

        _busy = true; Progress.Value = 0; StatusText.Text = "جاري تحديث البيانات...";
        try
        {
            var tasks = new (string key, string action)[] {
                ("live", "get_live_streams"), ("movies", "get_vod_streams"), ("series", "get_series")
            };
            for (int i = 0; i < tasks.Length; i++)
            {
                try { _cache[tasks[i].key] = await GetArray(tasks[i].action); }
                catch { /* keep the last successful cached section */ }
                Progress.Value = (i + 1) * 100.0 / tasks.Length;
                StatusText.Text = $"جاري تحديث {i + 1} من {tasks.Length} ({Progress.Value:0}%)";
            }
            File.WriteAllText(CacheFile, _cache.ToJsonString(JsonOptions));
            RenderAll();
            StatusText.Text = $"تم التحديث والحفظ ({Progress.Value:0}%). البيانات متاحة للتصفح بدون إنترنت.";
        }
        catch
        {
            RenderAll();
            StatusText.Text = "تعذر الاتصال. تم عرض آخر بيانات محفوظة إن وُجدت.";
        }
        finally { _busy = false; }
    }

    private async Task<JsonArray> GetArray(string action)
    {
        var baseUrl = _server.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? _server : "http://" + _server;
        var url = $"{baseUrl}/player_api.php?username={Uri.EscapeDataString(_user)}&password={Uri.EscapeDataString(_pass)}&action={action}";
        using var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync();
        return JsonNode.Parse(text)?.AsArray() ?? new JsonArray();
    }

    private JsonArray Items(string key)
    {
        try { return _cache[key]?.AsArray() ?? new JsonArray(); } catch { return new JsonArray(); }
    }

    private void RenderAll()
    {
        RenderList(HomePanel, Items("series"), "series");
        RenderList(SeriesPanel, Items("series"), "series");
        RenderList(MoviesPanel, Items("movies"), "movie");
        RenderList(LivePanel, Items("live"), "live");
    }

    private void RenderList(WrapPanel panel, JsonArray items, string type)
    {
        panel.Children.Clear();
        if (items.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "لا توجد بيانات محفوظة لهذا القسم.", Foreground = Brushes.LightGray, FontSize = 18, Margin = new Thickness(20) });
            return;
        }
        foreach (var node in items.OfType<JsonObject>())
            panel.Children.Add(CreateCard(node, type));
    }

    private Border CreateCard(JsonObject item, string type)
    {
        var title = item["name"]?.GetValue<string>() ?? "بدون اسم";
        var image = item["stream_icon"]?.GetValue<string>() ?? item["cover"]?.GetValue<string>() ?? "";
        var id = item["stream_id"]?.GetValue<int?>() ?? item["series_id"]?.GetValue<int?>() ?? 0;
        var card = new Border { Width = 170, Height = 245, Margin = new Thickness(8), Background = new SolidColorBrush(Color.FromRgb(25, 30, 40)), CornerRadius = new CornerRadius(10), Cursor = System.Windows.Input.Cursors.Hand };
        var stack = new StackPanel();
        var img = new Image { Height = 190, Stretch = Stretch.UniformToFill };
        TryImage(img, image);
        stack.Children.Add(img);
        stack.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 14, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(6) });
        card.Child = stack;
        card.MouseLeftButtonUp += async (_, _) =>
        {
            if (type == "series") await ShowSeries(id, title);
            else
            {
                var ext = item["container_extension"]?.GetValue<string>() ?? "mp4";
                var kind = type == "live" ? "live" : "movie";
                var url = StreamUrl(kind, id, ext);
                new PlayerWindow(url, title) { Owner = this }.Show();
            }
        };
        return card;
    }

    private string StreamUrl(string kind, int id, string ext)
    {
        var baseUrl = _server.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? _server : "http://" + _server;
        var folder = kind == "live" ? "live" : kind == "series" ? "series" : "movie";
        return $"{baseUrl}/{folder}/{Uri.EscapeDataString(_user)}/{Uri.EscapeDataString(_pass)}/{id}.{ext}";
    }

    private async Task ShowSeries(int id, string title)
    {
        if (id == 0) return;
        try
        {
            var info = await GetObject($"get_series_info&series_id={id}");
            var win = new Window { Owner = this, Title = title, Width = 620, Height = 650, Background = (Brush)FindResource("PageBrush"), FlowDirection = FlowDirection.RightToLeft };
            var scroll = new ScrollViewer();
            var panel = new StackPanel { Margin = new Thickness(18) };
            scroll.Content = panel; win.Content = scroll;
            var eps = info["episodes"]?.AsObject();
            if (eps != null)
                foreach (var season in eps)
                    foreach (var epNode in season.Value?.AsArray()?.OfType<JsonObject>() ?? [])
                    {
                        var ep = epNode;
                        var n = ep["episode_num"]?.GetValue<int?>() ?? 0;
                        var epTitle = ep["title"]?.GetValue<string>() ?? $"الحلقة {n}";
                        var b = new Button { Content = $"الحلقة {n}: {epTitle}", Margin = new Thickness(4), Padding = new Thickness(10), HorizontalContentAlignment = HorizontalAlignment.Right };
                        b.Click += (_, _) =>
                        {
                            var eid = ep["id"]?.GetValue<string>() ?? "";
                            var ext = ep["container_extension"]?.GetValue<string>() ?? "mp4";
                            var url = StreamUrl("series", int.TryParse(eid, out var parsedId) ? parsedId : 0, ext);
                            new PlayerWindow(url, $"{title} - {epTitle}") { Owner = this }.Show();
                        };
                        panel.Children.Add(b);
                    }
            if (panel.Children.Count == 0) panel.Children.Add(new TextBlock { Text = "لا توجد حلقات محفوظة.", Foreground = Brushes.White, FontSize = 18 });
            win.Show();
        }
        catch { MessageBox.Show("التصفح متاح، لكن تحميل تفاصيل الحلقات يحتاج اتصالًا بالإنترنت.", "Voltix IPTV", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private async Task<JsonObject> GetObject(string query)
    {
        var baseUrl = _server.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? _server : "http://" + _server;
        var url = $"{baseUrl}/player_api.php?username={Uri.EscapeDataString(_user)}&password={Uri.EscapeDataString(_pass)}&{query}";
        using var response = await Http.GetAsync(url); response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())?.AsObject() ?? new JsonObject();
    }

    private static void TryImage(Image img, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            var bi = new BitmapImage(); bi.BeginInit(); bi.UriSource = new Uri(url); bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); img.Source = bi;
        }
        catch { }
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        _server = _user = _pass = ""; ServerBox.Clear(); UserBox.Clear(); PassBox.Clear();
        try { if (File.Exists(SettingsFile)) File.Delete(SettingsFile); } catch { }
        StatusText.Text = "تم تسجيل الخروج. الكاش المحفوظ ما زال متاحًا.";
    }
}
