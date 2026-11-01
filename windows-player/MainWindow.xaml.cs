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
    private static readonly HttpClient ImageHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly SemaphoreSlim ImageGate = new(8);
    private readonly string CacheDir = IoPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoltixIptvWindows");
    private readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private JsonObject _cache = new();
    private string _server = "", _user = "", _pass = "", _m3uUrl = "";
    private bool _xtream = true;
    private bool _busy;
    private bool _suppressCategorySelection;
    private string _activeKind = "series";
    private string _activeCategoryId = "";
    private JsonArray _currentItems = new();
    private int _renderLimit = 48;
    private readonly Dictionary<string, string> _selectedCategoryIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loadedCategoryKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loadingCategoryKeys = new(StringComparer.Ordinal);

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
            EnsureAllCategoryCaches();
        }
        catch { _cache = new JsonObject(); EnsureAllCategoryCaches(); }
    }

    private void EnsureAllCategoryCaches()
    {
        EnsureCategoryCache("series_categories", "series_by_category", "series");
        EnsureCategoryCache("movie_categories", "movies_by_category", "movies");
        EnsureCategoryCache("live_categories", "live_by_category", "live");
        if (_cache["continue_watching"] is not JsonArray)
            _cache["continue_watching"] = new JsonArray();
    }

    private void EnsureCategoryCache(string categoriesKey, string groupedKey, string legacyItemsKey)
    {
        if (_cache[categoriesKey] is not JsonArray) _cache[categoriesKey] = new JsonArray();
        if (_cache[groupedKey] is JsonObject) return;
        var categories = _cache[categoriesKey]!.AsArray();
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cat in categories.OfType<JsonObject>())
        {
            var id = cat["category_id"]?.ToString() ?? cat["id"]?.ToString() ?? "";
            if (!string.IsNullOrWhiteSpace(id)) known.Add(id);
        }
        var grouped = new JsonObject();
        if (_cache[legacyItemsKey] is JsonArray legacy)
        {
            foreach (var item in legacy.OfType<JsonObject>())
            {
                var id = item["category_id"]?.ToString();
                if (string.IsNullOrWhiteSpace(id)) id = "legacy";
                if (grouped[id] is not JsonArray) grouped[id] = new JsonArray();
                grouped[id]!.AsArray().Add(item.DeepClone());
                if (known.Add(id)) categories.Add(new JsonObject { ["category_id"] = id, ["category_name"] = id == "legacy" ? "مسلسلات" : $"تصنيف {id}" });
            }
        }
        _cache[groupedKey] = grouped;
    }

    private JsonObject CategoryItemsCache(string kind)
    {
        EnsureAllCategoryCaches();
        var key = kind switch { "series" => "series_by_category", "movie" => "movies_by_category", _ => "live_by_category" };
        return _cache[key]!.AsObject();
    }

    private string CategoriesKey(string kind) => kind switch
    {
        "series" => "series_categories",
        "movie" => "movie_categories",
        _ => "live_categories"
    };

    private string SectionName(string kind) => kind switch
    {
        "series" => "Series",
        "movie" => "Movies",
        _ => "Live TV"
    };

    private string ContentTypeName(string kind) => kind switch
    {
        "series" => "المسلسلات",
        "movie" => "الأفلام",
        _ => "قنوات اللايف"
    };

    private string CategoryCacheKey(string kind, string id) => $"{kind}:{id}";

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
        HomeView.Visibility = Visibility.Visible;
        SectionView.Visibility = Visibility.Collapsed;
        ProfileText.Text = string.IsNullOrWhiteSpace(ProfileBox.Text) ? "User" : ProfileBox.Text.Trim();
    }

    private void ShowLogin()
    {
        BrowseView.Visibility = Visibility.Collapsed;
        LoginView.Visibility = Visibility.Visible;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        BrowseView.Visibility = Visibility.Collapsed;
        LoginView.Visibility = Visibility.Visible;
    }

    private void Login_Click(object sender, RoutedEventArgs e)
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
        _ = RefreshCatalogAsync();
    }

    private void OpenSeries_Click(object sender, RoutedEventArgs e) => OpenSection("series");
    private void OpenMovies_Click(object sender, RoutedEventArgs e) => OpenSection("movie");
    private void OpenLive_Click(object sender, RoutedEventArgs e) => OpenSection("live");

    private void OpenSection(string kind)
    {
        _activeKind = kind;
        _activeCategoryId = "";
        ContentSearchBox.Text = "";
        CategorySearchBox.Text = "";
        CurrentSectionTitle.Text = SectionName(kind);
        HomeView.Visibility = Visibility.Collapsed;
        SectionView.Visibility = Visibility.Visible;
        RenderCategories();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        SectionView.Visibility = Visibility.Collapsed;
        HomeView.Visibility = Visibility.Visible;
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

        _busy = true;
        Progress.Value = 0;
        StatusText.Text = "جاري تحديث التصنيفات...";
        try
        {
            if (_xtream)
            {
                var tasks = new[] {
                    ("live_categories", "get_live_categories"),
                    ("movie_categories", "get_vod_categories"),
                    ("series_categories", "get_series_categories")
                };
                for (int i = 0; i < tasks.Length; i++)
                {
                    _cache[tasks[i].Item1] = await GetArray(tasks[i].Item2);
                    Progress.Value = (i + 1) * 100.0 / tasks.Length;
                    StatusText.Text = $"جاري تحديث التصنيفات {i + 1}/{tasks.Length} ({Progress.Value:0}%)";
                }
                EnsureAllCategoryCaches();
            }
            else
            {
                _cache["series_categories"] = new JsonArray();
                _cache["series_by_category"] = new JsonObject();
                _cache["movie_categories"] = new JsonArray();
                _cache["movies_by_category"] = new JsonObject();
                _cache["live"] = await GetM3uArray();
                GroupM3uLiveItems(Items("live"));
                Progress.Value = 100;
                StatusText.Text = "تم تحميل قائمة M3U (100%)";
            }
            File.WriteAllText(CacheFile, _cache.ToJsonString(JsonOptions));
            if (SectionView.Visibility == Visibility.Visible) RenderCategories();
            StatusText.Text = $"تم التحديث والحفظ ({Progress.Value:0}%).";
        }
        catch
        {
            StatusText.Text = "تعذر الاتصال. تم الاحتفاظ بآخر بيانات محفوظة.";
            if (SectionView.Visibility == Visibility.Visible) RenderCategories();
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
        string name = "", logo = "", groupTitle = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = line.IndexOf(',');
                name = comma >= 0 ? line[(comma + 1)..].Trim() : "قناة";
                logo = Between(line, "tvg-logo=\"", "\"") ?? "";
                groupTitle = Between(line, "group-title=\"", "\"") ?? "";
            }
            else if (!line.StartsWith("#") && line.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new JsonObject { ["name"] = name, ["stream_icon"] = logo, ["stream_url"] = line, ["group_title"] = groupTitle });
                name = ""; logo = ""; groupTitle = "";
            }
        }
        return result;
    }

    private void GroupM3uLiveItems(JsonArray items)
    {
        var groupIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var grouped = new JsonObject();
        foreach (var item in items.OfType<JsonObject>())
        {
            var groupName = item["group_title"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(groupName)) groupName = "بدون تصنيف";
            if (!groupIds.TryGetValue(groupName, out var id))
            {
                id = (groupIds.Count + 1).ToString();
                groupIds[groupName] = id;
                grouped[id] = new JsonArray();
            }
            item["category_id"] = id;
            grouped[id]!.AsArray().Add(item.DeepClone());
        }

        var categories = new JsonArray();
        foreach (var pair in groupIds)
            categories.Add(new JsonObject { ["category_id"] = pair.Value, ["category_name"] = pair.Key });
        _cache["live_categories"] = categories;
        _cache["live_by_category"] = grouped;
    }

    private static string? Between(string value, string left, string right)
    {
        var a = value.IndexOf(left, StringComparison.OrdinalIgnoreCase);
        if (a < 0) return null;
        a += left.Length;
        var b = value.IndexOf(right, a, StringComparison.Ordinal);
        return b < 0 ? null : value[a..b];
    }

    private string BaseUrl() => (_server.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? _server : "http://" + _server).TrimEnd('/');

    private JsonArray Items(string key)
    {
        try { return _cache[key]?.AsArray() ?? new JsonArray(); } catch { return new JsonArray(); }
    }

    private void RenderCategories()
    {
        _suppressCategorySelection = true;
        var previous = _selectedCategoryIds.TryGetValue(_activeKind, out var old) ? old : "";
        CategoryList.Items.Clear();
        AddCategory("__all__", "ALL");
        AddCategory("__continue__", "Continue Watching");
        foreach (var cat in Items(CategoriesKey(_activeKind)).OfType<JsonObject>())
        {
            var id = cat["category_id"]?.ToString() ?? cat["id"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(id)) continue;
            var name = cat["category_name"]?.ToString() ?? cat["name"]?.ToString() ?? $"تصنيف {id}";
            AddCategory(id, name);
        }

        ListBoxItem? selected = CategoryList.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag?.ToString() == previous);
        selected ??= CategoryList.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag?.ToString() != "__all__" && i.Tag?.ToString() != "__continue__");
        selected ??= CategoryList.Items.OfType<ListBoxItem>().FirstOrDefault();
        CategoryList.SelectedItem = selected;
        _suppressCategorySelection = false;
        if (selected != null)
        {
            _activeCategoryId = selected.Tag?.ToString() ?? "";
            _selectedCategoryIds[_activeKind] = _activeCategoryId;
            RenderSelectedCategory();
            _ = LoadCategoryAsync(_activeKind, _activeCategoryId);
        }
        ApplyCategoryFilter();
    }

    private void AddCategory(string id, string label)
    {
        CategoryList.Items.Add(new ListBoxItem
        {
            Content = label,
            Tag = id,
            Padding = new Thickness(10, 9, 10, 9),
            FlowDirection = FlowDirection.LeftToRight
        });
    }

    private async void Category_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressCategorySelection || CategoryList.SelectedItem is not ListBoxItem selected) return;
        _activeCategoryId = selected.Tag?.ToString() ?? "";
        _selectedCategoryIds[_activeKind] = _activeCategoryId;
        _renderLimit = 48;
        RenderSelectedCategory();
        await LoadCategoryAsync(_activeKind, _activeCategoryId);
    }

    private void RenderSelectedCategory()
    {
        if (_activeCategoryId == "__continue__")
        {
            _currentItems = Items("continue_watching");
            RenderList(_currentItems, _activeKind);
            return;
        }
        if (_activeCategoryId == "__all__")
        {
            _currentItems = CombineCachedCategories(_activeKind);
            if (_currentItems.Count == 0)
            {
                ItemsPanel.Children.Clear();
                ItemsPanel.Children.Add(new TextBlock
                {
                    Text = "اختار تصنيفًا من القائمة لعرض محتواه وتحميله بسرعة.",
                    Foreground = Brushes.LightGray, FontSize = 17, Margin = new Thickness(18)
                });
                LoadMoreButton.Visibility = Visibility.Collapsed;
                return;
            }
            RenderList(_currentItems, _activeKind);
            return;
        }
        _currentItems = CategoryItemsCache(_activeKind)[_activeCategoryId] as JsonArray ?? new JsonArray();
        if (_currentItems.Count == 0)
        {
            ItemsPanel.Children.Clear();
            ItemsPanel.Children.Add(new TextBlock { Text = "جاري تحميل التصنيف...", Foreground = Brushes.LightGray, FontSize = 17, Margin = new Thickness(18) });
            LoadMoreButton.Visibility = Visibility.Collapsed;
            return;
        }
        RenderList(_currentItems, _activeKind);
    }

    private JsonArray CombineCachedCategories(string kind)
    {
        var combined = new JsonArray();
        foreach (var pair in CategoryItemsCache(kind))
        {
            if (pair.Value is JsonArray arr)
                foreach (var item in arr.OfType<JsonObject>())
                    combined.Add(item.DeepClone());
        }
        return combined;
    }

    private async Task LoadCategoryAsync(string kind, string id)
    {
        if (id == "__all__" || id == "__continue__") return;
        var cache = CategoryItemsCache(kind);
        var key = CategoryCacheKey(kind, id);
        if (_loadedCategoryKeys.Contains(key) || !_loadingCategoryKeys.Add(key)) return;
        try
        {
            if (cache.ContainsKey(id))
            {
                _loadedCategoryKeys.Add(key);
                return;
            }
            var action = kind switch { "series" => "get_series", "movie" => "get_vod_streams", _ => "get_live_streams" };
            if (kind == "live" && !_xtream) return;
            StatusText.Text = $"جاري تحميل {ContentTypeName(kind)}...";
            var items = await GetArray(action, id);
            cache[id] = items;
            _loadedCategoryKeys.Add(key);
            File.WriteAllText(CacheFile, _cache.ToJsonString(JsonOptions));
            if (_activeKind == kind && _activeCategoryId == id)
            {
                _currentItems = items;
                _renderLimit = 48;
                RenderList(items, kind);
            }
            StatusText.Text = $"تم تحميل {items.Count} عنصرًا.";
        }
        catch
        {
            StatusText.Text = "تعذر تحميل التصنيف. تم الاحتفاظ بالبيانات المحفوظة إن وُجدت.";
        }
        finally { _loadingCategoryKeys.Remove(key); }
    }

    private void ApplyCategoryFilter()
    {
        if (CategoryList.Items.Count == 0) return;
        var query = CategorySearchBox.Text.Trim();
        foreach (var item in CategoryList.Items.OfType<ListBoxItem>())
        {
            var label = item.Content?.ToString() ?? "";
            item.Visibility = string.IsNullOrWhiteSpace(query) || label.Contains(query, StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void CategorySearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyCategoryFilter();

    private void ContentSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _renderLimit = 48;
        if (_currentItems != null) RenderList(_currentItems, _activeKind);
    }

    private void RenderList(JsonArray items, string type)
    {
        ItemsPanel.Children.Clear();
        var query = ContentSearchBox.Text.Trim();
        var filtered = items.OfType<JsonObject>()
            .Where(item => string.IsNullOrWhiteSpace(query) ||
                (item["name"]?.ToString() ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (filtered.Count == 0)
        {
            ItemsPanel.Children.Add(new TextBlock { Text = "لا توجد عناصر في هذا التصنيف.", Foreground = Brushes.LightGray, FontSize = 18, Margin = new Thickness(20) });
            LoadMoreButton.Visibility = Visibility.Collapsed;
            return;
        }
        var shown = filtered.Take(_renderLimit).ToArray();
        foreach (var item in shown) ItemsPanel.Children.Add(CreateCard(item, type));
        LoadMoreButton.Visibility = filtered.Count > shown.Length ? Visibility.Visible : Visibility.Collapsed;
        LoadMoreButton.Content = $"تحميل المزيد ({shown.Length} من {filtered.Count})";
    }

    private void LoadMore_Click(object sender, RoutedEventArgs e)
    {
        _renderLimit += 48;
        RenderList(_currentItems, _activeKind);
    }

    private void ItemsScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange > 0 && ItemsScrollView.ScrollableHeight - ItemsScrollView.VerticalOffset < 120 &&
            LoadMoreButton.Visibility == Visibility.Visible)
        {
            _renderLimit += 48;
            RenderList(_currentItems, _activeKind);
        }
    }

    private Border CreateCard(JsonObject item, string type)
    {
        var title = item["name"]?.ToString() ?? "بدون اسم";
        var image = item["stream_icon"]?.ToString() ?? item["cover"]?.ToString() ?? "";
        var kind = item["_kind"]?.ToString() ?? type;
        var card = new Border
        {
            Width = 148, Height = 255, Margin = new Thickness(7),
            Background = new SolidColorBrush(Color.FromRgb(24, 27, 45)),
            CornerRadius = new CornerRadius(8), Cursor = System.Windows.Input.Cursors.Hand,
            ClipToBounds = true
        };
        var stack = new StackPanel();
        var poster = new Border { Height = 208, Background = new SolidColorBrush(Color.FromRgb(31, 36, 58)) };
        var img = new Image { Stretch = Stretch.UniformToFill };
        poster.Child = img;
        if (!string.IsNullOrWhiteSpace(image)) _ = LoadImageAsync(img, image);
        stack.Children.Add(poster);
        stack.Children.Add(new TextBlock
        {
            Text = title, Foreground = Brushes.White, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(5, 6, 5, 4), MaxHeight = 40
        });
        card.Child = stack;
        card.MouseLeftButtonUp += async (_, _) =>
        {
            var m3uUrl = item["stream_url"]?.ToString() ?? "";
            if (kind == "series")
            {
                var seriesId = ParseId(item["series_id"]?.ToString() ?? item["stream_id"]?.ToString() ?? item["id"]?.ToString());
                await ShowSeries(seriesId, title);
                return;
            }
            var id = ParseId(item["stream_id"]?.ToString() ?? item["id"]?.ToString());
            var folder = kind == "live" ? "live" : "movie";
            var generatedUrl = id > 0
                ? StreamUrl(folder, id, item["container_extension"]?.ToString() ?? (kind == "live" ? "ts" : "mp4"))
                : "";
            var directUrl = FirstPlayableUrl(item["stream_url"]?.ToString(), item["direct_source"]?.ToString());
            var primaryUrl = !string.IsNullOrWhiteSpace(directUrl) ? directUrl : generatedUrl;
            var fallbackUrl = !string.IsNullOrWhiteSpace(directUrl) && !string.IsNullOrWhiteSpace(generatedUrl) &&
                              !string.Equals(directUrl, generatedUrl, StringComparison.OrdinalIgnoreCase)
                ? generatedUrl : null;
            if (!string.IsNullOrWhiteSpace(m3uUrl) && string.IsNullOrWhiteSpace(fallbackUrl))
                primaryUrl = m3uUrl;
            if (!string.IsNullOrWhiteSpace(primaryUrl)) PlayItem(primaryUrl, title, item, kind, fallbackUrl);
        };
        return card;
    }

    private static async Task LoadImageAsync(Image image, string url)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out _)) return;
            await ImageGate.WaitAsync();
            try
            {
                var bytes = await ImageHttp.GetByteArrayAsync(url);
                using var stream = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                await image.Dispatcher.InvokeAsync(() => image.Source = bitmap);
            }
            finally { ImageGate.Release(); }
        }
        catch { }
    }

    private void PlayItem(string url, string title, JsonObject source, string kind, string? fallbackUrl = null)
    {
        AddToContinueWatching(title, source, kind, url);
        new PlayerWindow(url, title, fallbackUrl) { Owner = this }.Show();
    }

    private void AddToContinueWatching(string title, JsonObject source, string kind, string url)
    {
        var list = Items("continue_watching");
        var next = new JsonArray();
        next.Add(new JsonObject
        {
            ["name"] = title, ["stream_url"] = url, ["_kind"] = kind,
            ["stream_icon"] = source["stream_icon"]?.ToString() ?? source["cover"]?.ToString() ?? ""
        });
        foreach (var item in list.OfType<JsonObject>().Take(39))
        {
            if ((item["name"]?.ToString() ?? "") == title) continue;
            next.Add(item.DeepClone());
        }
        _cache["continue_watching"] = next;
        try { File.WriteAllText(CacheFile, _cache.ToJsonString(JsonOptions)); } catch { }
    }

    private static int ParseId(string? value) => int.TryParse(value, out var id) ? id : 0;

    private static string FirstPlayableUrl(params string?[] candidates)
        => candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) &&
            Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
            !value.Equals("0", StringComparison.OrdinalIgnoreCase)) ?? "";

    private string StreamUrl(string kind, int id, string ext)
    {
        var folder = kind == "live" ? "live" : kind == "series" ? "series" : "movie";
        return $"{BaseUrl()}/{folder}/{Uri.EscapeDataString(_user)}/{Uri.EscapeDataString(_pass)}/{id}.{ext}";
    }

    private void PlayEpisode(JsonObject episode, string seriesTitle, string episodeTitle)
    {
        var id = ParseId(episode["id"]?.ToString() ?? episode["stream_id"]?.ToString() ?? episode["episode_id"]?.ToString());
        var generatedUrl = id > 0
            ? StreamUrl("series", id, episode["container_extension"]?.ToString() ?? "mp4")
            : "";
        var directUrl = FirstPlayableUrl(episode["direct_source"]?.ToString(), episode["stream_url"]?.ToString());
        var url = !string.IsNullOrWhiteSpace(directUrl) ? directUrl : generatedUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        var fallback = !string.IsNullOrWhiteSpace(directUrl) && !string.IsNullOrWhiteSpace(generatedUrl) &&
                       !string.Equals(directUrl, generatedUrl, StringComparison.OrdinalIgnoreCase)
            ? generatedUrl : null;
        var title = $"{seriesTitle} - {episodeTitle}";
        AddToContinueWatching(title, new JsonObject { ["name"] = episodeTitle }, "series", url);
        new PlayerWindow(url, title, fallback) { Owner = this }.Show();
    }

    private static IEnumerable<(string Season, JsonObject Episode)> EnumerateEpisodes(JsonNode? node)
    {
        if (node is JsonArray directEpisodes)
        {
            foreach (var episode in directEpisodes.OfType<JsonObject>())
                yield return ("", episode);
            yield break;
        }
        if (node is not JsonObject seasons) yield break;
        foreach (var season in seasons)
        {
            if (season.Value is JsonArray episodes)
            {
                foreach (var episode in episodes.OfType<JsonObject>())
                    yield return (season.Key, episode);
            }
            else if (season.Value is JsonObject seasonObject && seasonObject["episodes"] is JsonArray nestedEpisodes)
            {
                var seasonName = seasonObject["name"]?.ToString() ?? seasonObject["season_number"]?.ToString() ?? season.Key;
                foreach (var episode in nestedEpisodes.OfType<JsonObject>())
                    yield return (seasonName, episode);
            }
        }
    }

    private async Task ShowSeries(int id, string title)
    {
        if (id == 0) return;
        try
        {
            var info = await GetObject($"get_series_info&series_id={id}");
            var win = new Window
            {
                Owner = this, Title = title, Width = 760, Height = 720, MinWidth = 600, MinHeight = 500,
                Background = (Brush)FindResource("PageBrush"), FlowDirection = FlowDirection.RightToLeft
            };
            var root = new DockPanel { Margin = new Thickness(18) };
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            var back = new Button { Content = "رجوع", Padding = new Thickness(15, 8, 15, 8), Margin = new Thickness(4) };
            back.Click += (_, _) => win.Close();
            header.Children.Add(back);
            header.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 22, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10) });
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
            var list = new StackPanel { Margin = new Thickness(8, 18, 8, 8) };
            var episodesNode = info["episodes"] ?? info["info"]?["episodes"];
            string? activeSeason = null;
            foreach (var entry in EnumerateEpisodes(episodesNode))
            {
                if (!string.IsNullOrWhiteSpace(entry.Season) && entry.Season != activeSeason)
                {
                    activeSeason = entry.Season;
                    list.Children.Add(new TextBlock
                    {
                        Text = $"الموسم {activeSeason}", Foreground = (Brush)FindResource("AccentBrush"),
                        FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(5, 12, 5, 5)
                    });
                }
                var episode = entry.Episode;
                var number = episode["episode_num"]?.ToString() ?? episode["episode"]?.ToString() ?? "";
                var episodeTitle = episode["title"]?.ToString() ?? episode["name"]?.ToString() ?? $"الحلقة {number}";
                var button = new Button
                {
                    Content = $"▶   الحلقة {number}: {episodeTitle}",
                    HorizontalContentAlignment = HorizontalAlignment.Right,
                    Padding = new Thickness(12), Margin = new Thickness(3),
                    Background = (Brush)FindResource("PanelBrush"), Foreground = Brushes.White,
                    BorderThickness = new Thickness(0)
                };
                button.Click += (_, _) => PlayEpisode(episode, title, episodeTitle);
                list.Children.Add(button);
            }
            if (list.Children.Count == 0)
                list.Children.Add(new TextBlock
                {
                    Text = "السيرفر لم يرجع حلقات لهذا المسلسل. حدّث القائمة أو راجع بيانات الاشتراك.",
                    Foreground = Brushes.White, FontSize = 18, TextWrapping = TextWrapping.Wrap
                });
            root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            win.Content = root;
            win.Show();
        }
        catch { MessageBox.Show("تعذر تحميل تفاصيل المسلسل. تأكد من الاتصال وبيانات الاشتراك.", "Iqraa IPTV", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private async Task<JsonObject> GetObject(string query)
    {
        var url = $"{BaseUrl()}/player_api.php?username={Uri.EscapeDataString(_user)}&password={Uri.EscapeDataString(_pass)}&{query}";
        using var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())?.AsObject() ?? new JsonObject();
    }

    private void Downloads_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("قسم التنزيلات سيظهر هنا بعد إضافة أول تنزيل.", "Iqraa IPTV");

    private void MatchSchedule_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("جدول المباريات غير متاح في هذا الاشتراك.", "Iqraa IPTV");

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        ShowLogin();
        StatusText.Text = "تم تسجيل الخروج. الإعدادات والكاش محفوظان.";
    }
}
