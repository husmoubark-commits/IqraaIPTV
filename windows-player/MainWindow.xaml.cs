using System.IO;
using IoPath = System.IO.Path;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VoltixIptvWindows;

public partial class MainWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly string CacheDir = IoPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoltixIptvWindows");
    private readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private JsonObject _cache = new();
    private string _server = "", _user = "", _pass = "", _m3uUrl = "";
    private bool _xtream = true;
    private bool _busy;
    private bool _suppressCategorySelection;
    private string _selectedSeriesCategoryId = "";
    private readonly HashSet<string> _loadedSeriesCategoryIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loadingSeriesCategoryIds = new(StringComparer.Ordinal);

    public MainWindow()
    {
        InitializeComponent();
        Directory.CreateDirectory(CacheDir);
        LoadSettings();
        LoadCache();
        if ((_xtream && !string.IsNullOrWhiteSpace(_server) && !string.IsNullOrWhiteSpace(_user)) || (!_xtream && !string.IsNullOrWhiteSpace(_m3uUrl)))
            ShowBrowse();
        else
            SetMode(true);
    }

    private string CacheFile => IoPath.Combine(CacheDir, "catalog.json");
    private string SettingsFile => IoPath.Combine(CacheDir, "settings.json");

    private void LoadSettings()
    {
        try
        {
            var o = JsonNode.Parse(File.ReadAllText(SettingsFile))?.AsObject();
            _xtream = o?["type"]?.GetValue<string>() != "m3u";
            _server = o?["server"]?.GetValue<string>() ?? "";
            _user = o?["user"]?.GetValue<string>() ?? "";
            _pass = o?["pass"]?.GetValue<string>() ?? "";
            _m3uUrl = o?["m3u"]?.GetValue<string>() ?? "";
            ProfileBox.Text = o?["profile"]?.GetValue<string>() ?? "";
            ServerBox.Text = _server; UserBox.Text = _user; PassBox.Password = _pass; M3uBox.Text = _m3uUrl;
            SetMode(_xtream);
        }
        catch { SetMode(true); }
    }

    private void LoadCache()
    {
        try
        {
            _cache = JsonNode.Parse(File.ReadAllText(CacheFile))?.AsObject() ?? new();
            EnsureSeriesCacheStructure();
            RenderAll();
            StatusText.Text = "تم تحميل البيانات المحفوظة. يمكنك التصفح بدون إنترنت.";
        }
        catch { }
    }

    private void EnsureSeriesCacheStructure()
    {
        if (_cache["series_categories"] is not JsonArray)
            _cache["series_categories"] = new JsonArray();

        if (_cache["series_by_category"] is JsonObject)
            return;

        var categories = _cache["series_categories"]!.AsArray();
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in categories.OfType<JsonObject>())
        {
            var id = c["category_id"]?.ToString() ?? c["id"]?.ToString() ?? "";
            if (!string.IsNullOrWhiteSpace(id)) known.Add(id);
        }

        var grouped = new JsonObject();
        if (_cache["series"] is JsonArray legacy)
        {
            foreach (var item in legacy.OfType<JsonObject>())
            {
                var id = item["category_id"]?.ToString();
                if (string.IsNullOrWhiteSpace(id)) id = "legacy";
                if (grouped[id] is not JsonArray)
                    grouped[id] = new JsonArray();
                grouped[id]!.AsArray().Add(item.DeepClone());

                if (known.Add(id))
                {
                    var name = item["category_name"]?.GetValue<string>() ?? (id == "legacy" ? "مسلسلات" : $"تصنيف {id}");
                    categories.Add(new JsonObject { ["category_id"] = id, ["category_name"] = name });
                }
            }
        }
        _cache["series_by_category"] = grouped;
    }

    private JsonObject SeriesByCategoryCache
    {
        get
        {
            EnsureSeriesCacheStructure();
            return _cache["series_by_category"]!.AsObject();
        }
    }

    private void SaveSettings()
    {
        _server = ServerBox.Text.Trim().TrimEnd('/');
        _user = UserBox.Text.Trim();
        _pass = PassBox.Password;
        _m3uUrl = M3uBox.Text.Trim();
        var profile = string.IsNullOrWhiteSpace(ProfileBox.Text) ? "User" : ProfileBox.Text.Trim();
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new { type = _xtream ? "xtream" : "m3u", profile, server = _server, user = _user, pass = _pass, m3u = _m3uUrl }));
        ProfileText.Text = profile;
    }

    private void SetMode(bool xtream)
    {
        _xtream = xtream;
        XtreamFields.Visibility = xtream ? Visibility.Visible : Visibility.Collapsed;
        M3uFields.Visibility = xtream ? Visibility.Collapsed : Visibility.Visible;
        XtreamButton.Background = xtream ? (Brush)FindResource("AccentBrush") : Brushes.Transparent;
        M3uButton.Background = xtream ? Brushes.Transparent : (Brush)FindResource("AccentBrush");
    }

    private void XtreamMode_Click(object sender, RoutedEventArgs e) => SetMode(true);
    private void M3uMode_Click(object sender, RoutedEventArgs e) => SetMode(false);

    private void ShowBrowse()
    {
        LoginView.Visibility = Visibility.Collapsed;
        BrowseView.Visibility = Visibility.Visible;
        ProfileText.Text = string.IsNullOrWhiteSpace(ProfileBox.Text) ? "User" : ProfileBox.Text.Trim();
        RenderAll();
    }

    private void ShowLogin()
    {
        BrowseView.Visibility = Visibility.Collapsed;
        LoginView.Visibility = Visibility.Visible;
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        LoginError.Text = "";
        if (_xtream)
        {
            if (string.IsNullOrWhiteSpace(ServerBox.Text) || string.IsNullOrWhiteSpace(UserBox.Text) || string.IsNullOrWhiteSpace(PassBox.Password))
            {
                LoginError.Text = "املأ رابط السيرفر واسم المستخدم وكلمة المرور.";
                return;
            }
        }
        else if (string.IsNullOrWhiteSpace(M3uBox.Text))
        {
            LoginError.Text = "أدخل رابط قائمة M3U.";
            return;
        }
        SaveSettings();
        ShowBrowse();
        await RefreshCatalogAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshCatalogAsync();

    private async Task RefreshCatalogAsync()
    {
        if (_busy) return;
        if (_xtream && (string.IsNullOrWhiteSpace(_server) || string.IsNullOrWhiteSpace(_user) || string.IsNullOrWhiteSpace(_pass)))
        {
            ShowLogin();
            LoginError.Text = "املأ بيانات الاشتراك أولًا.";
            return;
        }
        if (!_xtream && string.IsNullOrWhiteSpace(_m3uUrl))
        {
            ShowLogin();
            LoginError.Text = "أدخل رابط قائمة M3U.";
            return;
        }

        _busy = true; Progress.Value = 0; StatusText.Text = "جاري تحديث البيانات...";
        try
        {
            if (_xtream)
            {
                var tasks = new[] { ("live", "get_live_streams"), ("movies", "get_vod_streams"), ("series_categories", "get_series_categories") };
                for (int i = 0; i < tasks.Length; i++)
                {
                    try { _cache[tasks[i].Item1] = await GetArray(tasks[i].Item2); } catch { }
                    Progress.Value = (i + 1) * 100.0 / tasks.Length;
                    StatusText.Text = $"جاري تحديث {i + 1} من {tasks.Length} ({Progress.Value:0}%)";
                }
                EnsureSeriesCacheStructure();
            }
            else
            {
                _cache["live"] = await GetM3uArray();
                _cache["movies"] = new JsonArray();
                _cache["series_categories"] = new JsonArray();
                _cache["series_by_category"] = new JsonObject();
                Progress.Value = 100;
                StatusText.Text = "تم تحميل قائمة M3U (100%)";
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

    private async Task<JsonArray> GetArray(string action, string? categoryId = null)
    {
        var categoryQuery = string.IsNullOrWhiteSpace(categoryId) ? "" : $"&category_id={Uri.EscapeDataString(categoryId)}";
        var url = $"{BaseUrl()}/player_api.php?username={Uri.EscapeDataString(_user)}&password={Uri.EscapeDataString(_pass)}&action={action}{categoryQuery}";
        using var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())?.AsArray() ?? new JsonArray();
    }

    private async Task<JsonArray> GetM3uArray()
    {
        var text = await Http.GetStringAsync(_m3uUrl);
        var result = new JsonArray();
        string name = "", logo = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = line.IndexOf(',');
                name = comma >= 0 ? line[(comma + 1)..].Trim() : "قناة";
                logo = Between(line, "tvg-logo=\"", "\"") ?? "";
            }
            else if (!line.StartsWith("#") && line.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new JsonObject { ["name"] = name, ["stream_icon"] = logo, ["stream_url"] = line });
                name = ""; logo = "";
            }
        }
        return result;
    }

    private static string? Between(string value, string left, string right)
    {
        var a = value.IndexOf(left, StringComparison.OrdinalIgnoreCase);
        if (a < 0) return null;
        a += left.Length; var b = value.IndexOf(right, a, StringComparison.Ordinal);
        return b < 0 ? null : value[a..b];
    }

    private string BaseUrl() => (_server.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? _server : "http://" + _server).TrimEnd('/');

    private JsonArray Items(string key)
    {
        try { return _cache[key]?.AsArray() ?? new JsonArray(); } catch { return new JsonArray(); }
    }

    private void RenderAll()
    {
        RenderList(HomePanel, Items("movies"), "movie");
        RenderSeriesCategories();
        RenderList(MoviesPanel, Items("movies"), "movie");
        RenderList(LivePanel, Items("live"), "live");
    }

    private void RenderSeriesCategories()
    {
        _suppressCategorySelection = true;
        var previousId = _selectedSeriesCategoryId;
        SeriesCategories.Items.Clear();
        foreach (var item in Items("series_categories").OfType<JsonObject>())
        {
            var id = item["category_id"]?.ToString() ?? item["id"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(id)) continue;
            var name = item["category_name"]?.GetValue<string>() ?? item["name"]?.GetValue<string>() ?? $"تصنيف {id}";
            SeriesCategories.Items.Add(new ListBoxItem { Content = name, Tag = id, Padding = new Thickness(10, 8, 10, 8), FlowDirection = FlowDirection.RightToLeft });
        }

        ListBoxItem? selected = null;
        foreach (var item in SeriesCategories.Items.OfType<ListBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), previousId, StringComparison.Ordinal))
            {
                selected = item;
                break;
            }
        }
        selected ??= SeriesCategories.Items.OfType<ListBoxItem>().FirstOrDefault();
        SeriesCategories.SelectedItem = selected;
        _suppressCategorySelection = false;

        if (selected != null)
        {
            _selectedSeriesCategoryId = selected.Tag?.ToString() ?? "";
            RenderSelectedSeriesCategory();
            _ = LoadSeriesCategoryAsync(_selectedSeriesCategoryId);
        }
        else
        {
            _selectedSeriesCategoryId = "";
            SeriesPanel.Children.Clear();
            SeriesPanel.Children.Add(new TextBlock { Text = "لا توجد تصنيفات محفوظة. اضغط تحديث لتحميل التصنيفات.", Foreground = Brushes.LightGray, FontSize = 18, Margin = new Thickness(20) });
        }
    }

    private async void SeriesCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressCategorySelection || SeriesCategories.SelectedItem is not ListBoxItem selected)
            return;

        var id = selected.Tag?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(id)) return;
        _selectedSeriesCategoryId = id;
        RenderSelectedSeriesCategory();
        await LoadSeriesCategoryAsync(id);
    }

    private void RenderSelectedSeriesCategory()
    {
        if (string.IsNullOrWhiteSpace(_selectedSeriesCategoryId)) return;
        var cached = SeriesByCategoryCache[_selectedSeriesCategoryId] as JsonArray ?? new JsonArray();
        RenderList(SeriesPanel, cached, "series");
    }

    private async Task LoadSeriesCategoryAsync(string categoryId)
    {
        if (!_xtream || string.IsNullOrWhiteSpace(categoryId) || _loadedSeriesCategoryIds.Contains(categoryId) || !_loadingSeriesCategoryIds.Add(categoryId))
            return;

        try
        {
            if (SeriesByCategoryCache.ContainsKey(categoryId))
            {
                _loadedSeriesCategoryIds.Add(categoryId);
                return;
            }

            StatusText.Text = "جاري تحميل مسلسلات التصنيف المختار...";
            var items = await GetArray("get_series", categoryId);
            SeriesByCategoryCache[categoryId] = items;
            _loadedSeriesCategoryIds.Add(categoryId);
            File.WriteAllText(CacheFile, _cache.ToJsonString(JsonOptions));
            if (string.Equals(_selectedSeriesCategoryId, categoryId, StringComparison.Ordinal))
                RenderSelectedSeriesCategory();
            StatusText.Text = $"تم تحميل {items.Count} مسلسلًا في التصنيف المحدد.";
        }
        catch
        {
            StatusText.Text = "تعذر تحميل هذا التصنيف. اعرض آخر بياناته المحفوظة أو تحقق من الاتصال.";
        }
        finally
        {
            _loadingSeriesCategoryIds.Remove(categoryId);
        }
    }

    private void RenderList(WrapPanel panel, JsonArray items, string type)
    {
        panel.Children.Clear();
        if (items.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "لا توجد بيانات محفوظة لهذا القسم.", Foreground = Brushes.LightGray, FontSize = 18, Margin = new Thickness(20) });
            return;
        }
        foreach (var node in items.OfType<JsonObject>()) panel.Children.Add(CreateCard(node, type));
    }

    private Border CreateCard(JsonObject item, string type)
    {
        var title = item["name"]?.GetValue<string>() ?? "بدون اسم";
        var image = item["stream_icon"]?.GetValue<string>() ?? item["cover"]?.GetValue<string>() ?? "";
        var id = item["stream_id"]?.GetValue<int?>() ?? item["series_id"]?.GetValue<int?>() ?? 0;
        var card = new Border { Width = 170, Height = 245, Margin = new Thickness(8), Background = new SolidColorBrush(Color.FromRgb(25, 30, 40)), CornerRadius = new CornerRadius(10), Cursor = System.Windows.Input.Cursors.Hand };
        var stack = new StackPanel();
        var img = new Image { Height = 190, Stretch = Stretch.UniformToFill }; TryImage(img, image); stack.Children.Add(img);
        stack.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 14, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(6) });
        card.Child = stack;
        card.MouseLeftButtonUp += async (_, _) =>
        {
            if (type == "series") await ShowSeries(id, title);
            else
            {
                var url = _xtream ? StreamUrl(type == "live" ? "live" : "movie", id, item["container_extension"]?.GetValue<string>() ?? "mp4") : item["stream_url"]?.GetValue<string>() ?? "";
                if (!string.IsNullOrWhiteSpace(url)) new PlayerWindow(url, title) { Owner = this }.Show();
            }
        };
        return card;
    }

    private string StreamUrl(string kind, int id, string ext)
    {
        var folder = kind == "live" ? "live" : kind == "series" ? "series" : "movie";
        return $"{BaseUrl()}/{folder}/{Uri.EscapeDataString(_user)}/{Uri.EscapeDataString(_pass)}/{id}.{ext}";
    }

    private async Task ShowSeries(int id, string title)
    {
        if (id == 0) return;
        try
        {
            var info = await GetObject($"get_series_info&series_id={id}");
            var win = new Window { Owner = this, Title = title, Width = 620, Height = 650, Background = (Brush)FindResource("PageBrush"), FlowDirection = FlowDirection.RightToLeft };
            var panel = new StackPanel { Margin = new Thickness(18) }; win.Content = new ScrollViewer { Content = panel };
            var eps = info["episodes"]?.AsObject();
            if (eps != null)
                foreach (var season in eps)
                    foreach (var ep in season.Value?.AsArray()?.OfType<JsonObject>() ?? [])
                    {
                        var n = ep["episode_num"]?.GetValue<int?>() ?? 0;
                        var epTitle = ep["title"]?.GetValue<string>() ?? $"الحلقة {n}";
                        var b = new Button { Content = $"الحلقة {n}: {epTitle}", Margin = new Thickness(4), Padding = new Thickness(10), HorizontalContentAlignment = HorizontalAlignment.Right };
                        b.Click += (_, _) =>
                        {
                            var eid = ep["id"]?.GetValue<string>() ?? "0";
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
        var url = $"{BaseUrl()}/player_api.php?username={Uri.EscapeDataString(_user)}&password={Uri.EscapeDataString(_pass)}&{query}";
        using var response = await Http.GetAsync(url); response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())?.AsObject() ?? new JsonObject();
    }

    private static void TryImage(Image img, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { var bi = new BitmapImage(); bi.BeginInit(); bi.UriSource = new Uri(url); bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); img.Source = bi; } catch { }
    }

    private void Downloads_Click(object sender, RoutedEventArgs e) => MessageBox.Show("قسم التنزيلات سيظهر هنا بعد إضافة أول تنزيل.", "Voltix IPTV");

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        ShowLogin();
        StatusText.Text = "تم تسجيل الخروج. الكاش المحفوظ ما زال متاحًا.";
    }
}
