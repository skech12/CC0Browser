using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace NeurvanceBrowser;

public partial class MainWindow : Window
{
    private const string StarIcon = "\uE734";
    private const string StarFilledIcon = "\uE735";
    private const string CloseIcon = "\uE711";
    private const string MaximizeIcon = "\uE922";
    private const string RestoreIcon = "\uE923";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly List<BrowserTab> _tabs = [];
    private readonly string _dataPath;
    private readonly string _legacyJsonPath;
    private BrowserData _data = new();
    private BrowserTab? _activeTab;
    private int _nextTabId = 1;
    private bool _loaded;
    private string _resolvedApiKey = "";

    public MainWindow()
    {
        InitializeComponent();
        UpdateMaximizeButton();

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NeurvanceBrowser");
        Directory.CreateDirectory(appData);
        _dataPath = Path.Combine(appData, "browser-data.dat");
        _legacyJsonPath = Path.Combine(appData, "browser-data.json");
        LoadBrowserData();
        LoadSettings();
        RefreshStoredLists();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await CreateNewTabAsync();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SaveBrowserData();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        UpdateMaximizeButton();
    }

    private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void NewTabButton_Click(object sender, RoutedEventArgs e)
    {
        await CreateNewTabAsync();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab?.View.CanGoBack == true)
        {
            _activeTab.View.GoBack();
        }
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab?.View.CanGoForward == true)
        {
            _activeTab.View.GoForward();
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(_resolvedApiKey, FindDotEnvPath()) { Owner = this };
        if (dlg.ShowDialog() != true)
            return;

        _resolvedApiKey = dlg.ResultApiKey;
        _data.Settings.ApiKey = _resolvedApiKey;
        SaveBrowserData();
        StatusText.Text = "Settings saved";
    }

    private async void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is null)
        {
            return;
        }

        if (_activeTab.IsSearchPage && !string.IsNullOrWhiteSpace(_activeTab.SearchQuery))
        {
            await SearchActiveTabAsync(_activeTab.SearchQuery, addToHistory: false);
            return;
        }

        _activeTab.View.Reload();
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is not null)
        {
            ShowHome(_activeTab);
        }
    }

    private async void GoButton_Click(object sender, RoutedEventArgs e)
    {
        await NavigateOrSearchAsync(AddressBar.Text);
    }

    private async void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await NavigateOrSearchAsync(AddressBar.Text);
    }

    private void BookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is null)
        {
            return;
        }

        var url = _activeTab.StoredAddress;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var existing = _data.Bookmarks.FirstOrDefault(item => SameStoredUrl(item.Url, url));
        if (existing is not null)
        {
            _data.Bookmarks.Remove(existing);
            StatusText.Text = "Bookmark removed";
        }
        else
        {
            _data.Bookmarks.Insert(0, new StoredPage
            {
                Title = _activeTab.Title,
                Url = url,
                SavedAt = DateTimeOffset.UtcNow,
            });
            StatusText.Text = "Bookmarked";
        }

        SaveBrowserData();
        RefreshStoredLists();
        UpdateBookmarkState();
    }

    private async void HistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HistoryList.SelectedItem is StoredPage page)
        {
            await OpenStoredPageAsync(page);
        }
    }

    private async void BookmarkList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (BookmarkList.SelectedItem is StoredPage page)
        {
            await OpenStoredPageAsync(page);
        }
    }

    private async Task CreateNewTabAsync(string? initialAddress = null)
    {
        var tab = new BrowserTab
        {
            Id = _nextTabId++,
            Title = "New tab",
        };

        tab.View.NavigationStarting += WebView_NavigationStarting;
        tab.View.NavigationCompleted += WebView_NavigationCompleted;
        tab.View.SourceChanged += WebView_SourceChanged;

        _tabs.Add(tab);
        SetActiveTab(tab);

        try
        {
            await tab.View.EnsureCoreWebView2Async();
            ConfigureCoreWebView(tab);
        }
        catch (Exception ex)
        {
            tab.Title = "WebView2 error";
            RenderErrorPage(tab, "WebView2 could not start", ex.Message);
            RefreshTabs();
            return;
        }

        if (string.IsNullOrWhiteSpace(initialAddress))
        {
            ShowHome(tab);
        }
        else
        {
            await NavigateOrSearchAsync(initialAddress);
        }
    }

    private void ConfigureCoreWebView(BrowserTab tab)
    {
        if (tab.View.CoreWebView2 is null)
        {
            return;
        }

        tab.View.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;
        tab.View.CoreWebView2.Settings.AreDevToolsEnabled = true;
        tab.View.CoreWebView2.Settings.IsStatusBarEnabled = false;
        tab.View.CoreWebView2.DocumentTitleChanged += (_, _) =>
        {
            if (tab.IsSearchPage || tab.View.CoreWebView2 is null)
            {
                return;
            }

            var title = tab.View.CoreWebView2.DocumentTitle;
            if (!string.IsNullOrWhiteSpace(title))
            {
                tab.Title = title;
                if (_activeTab == tab)
                {
                    RefreshTabs();
                }
            }
        };
    }

    private async Task NavigateOrSearchAsync(string input)
    {
        input = input.Trim();
        if (_activeTab is null)
        {
            await CreateNewTabAsync(input);
            return;
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            RenderErrorPage(_activeTab, "Nothing to search", "Enter a question or a web address.");
            return;
        }

        if (TryCreateWebUri(input, out var uri))
        {
            NavigateActiveTab(uri);
            return;
        }

        await SearchActiveTabAsync(input, addToHistory: true);
    }

    private void NavigateActiveTab(Uri uri)
    {
        if (_activeTab is null)
        {
            return;
        }

        _activeTab.IsSearchPage = false;
        _activeTab.SearchQuery = "";
        _activeTab.StoredAddress = uri.ToString();
        _activeTab.Title = uri.Host;
        AddressBar.Text = uri.ToString();
        StatusText.Text = $"Loading {uri.Host}";
        _activeTab.View.Source = uri;
        RefreshTabs();
        UpdateNavigationState();
    }

    private async Task SearchActiveTabAsync(string query, bool addToHistory)
    {
        if (_activeTab is null)
        {
            return;
        }

        SetChromeEnabled(false);
        StatusText.Text = $"Searching Neurvance RAG for \"{query}\"";

        var response = await RunRagAsync(query);
        if (_activeTab is null)
        {
            return;
        }

        _activeTab.IsSearchPage = true;
        _activeTab.SearchQuery = query;
        _activeTab.StoredAddress = BuildSearchAddress(query);
        _activeTab.Title = $"Search: {Shorten(query, 28)}";

        if (response.Ok)
        {
            _activeTab.View.NavigateToString(BuildResultsHtml(response));
            StatusText.Text = $"{response.Results.Count} RAG result{(response.Results.Count == 1 ? "" : "s")}";
            if (addToHistory)
            {
                AddHistory(_activeTab.Title, _activeTab.StoredAddress);
            }
        }
        else
        {
            RenderErrorPage(_activeTab, "RAG search failed", response.Error);
            StatusText.Text = "RAG search failed";
        }

        SetChromeEnabled(true);
        AddressBar.Text = query;
        RefreshTabs();
        UpdateNavigationState();
        UpdateBookmarkState();
    }

    private async Task<RagResponse> RunRagAsync(string query)
    {
        var scriptPath = FindRagScript();
        if (scriptPath is null)
        {
            return RagResponse.Failed(query, "Could not find rag.py above the app folder.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = "python",
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (!string.IsNullOrEmpty(_resolvedApiKey))
            psi.EnvironmentVariables["CC0_CONTENT_API_KEY"] = _resolvedApiKey;

        psi.ArgumentList.Add(scriptPath);
        psi.ArgumentList.Add("--json");
        psi.ArgumentList.Add(query);

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            return RagResponse.Failed(query, "Python was not found. Install Python or add it to PATH.");
        }
        catch (Exception ex)
        {
            return RagResponse.Failed(query, ex.Message);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return RagResponse.Failed(query, "The RAG backend timed out.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (string.IsNullOrWhiteSpace(stdout))
        {
            return RagResponse.Failed(query, string.IsNullOrWhiteSpace(stderr)
                ? "rag.py did not return JSON."
                : stderr.Trim());
        }

        try
        {
            var response = JsonSerializer.Deserialize<RagResponse>(stdout, JsonOptions);
            if (response is null)
            {
                return RagResponse.Failed(query, "rag.py returned empty JSON.");
            }

            response.Query = string.IsNullOrWhiteSpace(response.Query) ? query : response.Query;
            response.Results ??= [];
            response.Error = string.IsNullOrWhiteSpace(response.Error) && !response.Ok
                ? "The RAG search returned an error."
                : response.Error;
            return response;
        }
        catch (JsonException)
        {
            return RagResponse.Failed(query, "rag.py returned invalid JSON.");
        }
    }

    private void WebView_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var tab = FindTab(sender);
        if (tab is null)
        {
            return;
        }

        if (tab.IsSearchPage)
        {
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return;
            }

            tab.IsSearchPage = false;
            tab.SearchQuery = "";
        }

        tab.StoredAddress = e.Uri;
        if (_activeTab == tab)
        {
            AddressBar.Text = e.Uri;
            StatusText.Text = $"Loading {e.Uri}";
        }
    }

    private void WebView_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        var tab = FindTab(sender);
        if (tab is null || tab.IsSearchPage || tab.View.Source is null)
        {
            return;
        }

        tab.StoredAddress = tab.View.Source.ToString();
        if (_activeTab == tab)
        {
            AddressBar.Text = tab.StoredAddress;
        }
    }

    private void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        var tab = FindTab(sender);
        if (tab is null)
        {
            return;
        }

        if (!tab.IsSearchPage && e.IsSuccess)
        {
            var source = tab.View.Source?.ToString();
            if (!string.IsNullOrWhiteSpace(source))
            {
                tab.StoredAddress = source;
            }

            var title = tab.View.CoreWebView2?.DocumentTitle;
            tab.Title = string.IsNullOrWhiteSpace(title)
                ? HostOrAddress(tab.StoredAddress)
                : title;
            AddHistory(tab.Title, tab.StoredAddress);
        }
        else if (!e.IsSuccess)
        {
            StatusText.Text = $"Navigation failed: {e.WebErrorStatus}";
        }

        if (_activeTab == tab)
        {
            UpdateNavigationState();
            UpdateBookmarkState();
            RefreshTabs();
            StatusText.Text = e.IsSuccess ? "Ready" : StatusText.Text;
        }
    }

    private void ShowHome(BrowserTab tab)
    {
        tab.IsSearchPage = true;
        tab.SearchQuery = "";
        tab.StoredAddress = "";
        tab.Title = "New tab";
        tab.View.NavigateToString(BuildHomeHtml());
        if (_activeTab == tab)
        {
            AddressBar.Text = "";
            StatusText.Text = "Ready";
        }

        RefreshTabs();
        UpdateNavigationState();
        UpdateBookmarkState();
    }

    private void RenderErrorPage(BrowserTab tab, string title, string message)
    {
        tab.IsSearchPage = true;
        tab.Title = title;
        var html = BuildShellHtml(title, $"""
            <main class="center">
                <section class="message">
                    <p class="eyebrow">Neurvance Browser</p>
                    <h1>{Html(title)}</h1>
                    <p>{Html(message)}</p>
                </section>
            </main>
            """);
        tab.View.NavigateToString(html);
        if (_activeTab == tab)
        {
            RefreshTabs();
            UpdateNavigationState();
            UpdateBookmarkState();
        }
    }

    private void SetActiveTab(BrowserTab tab)
    {
        _activeTab = tab;
        BrowserHost.Content = tab.View;
        AddressBar.Text = tab.IsSearchPage ? tab.SearchQuery : tab.StoredAddress;
        RefreshTabs();
        UpdateNavigationState();
        UpdateBookmarkState();
    }

    private void RefreshTabs()
    {
        TabStrip.Children.Clear();

        foreach (var tab in _tabs)
        {
            var isActive = tab == _activeTab;

            var container = new DockPanel
            {
                LastChildFill = true,
                Height = 32,
                Margin = new Thickness(0, 0, 2, 0),
            };

            var closeButton = new Button
            {
                Content = CloseIcon,
                Tag = tab,
                ToolTip = "Close tab",
                Style = (Style)FindResource("TabClose"),
            };
            WindowChrome.SetIsHitTestVisibleInChrome(closeButton, true);
            closeButton.Click += async (_, _) => await CloseTabAsync(tab);
            DockPanel.SetDock(closeButton, Dock.Right);

            var titleButton = new Button
            {
                Content = Shorten(tab.Title, 22),
                Tag = tab,
                ToolTip = tab.Title,
                Style = (Style)FindResource("TabBtn"),
                Background = isActive
                    ? new SolidColorBrush(Color.FromRgb(55, 55, 55))
                    : Brushes.Transparent,
                Foreground = isActive
                    ? new SolidColorBrush(Color.FromRgb(230, 230, 230))
                    : new SolidColorBrush(Color.FromRgb(150, 150, 150)),
            };
            WindowChrome.SetIsHitTestVisibleInChrome(titleButton, true);
            titleButton.Click += (_, _) => SetActiveTab(tab);

            container.Children.Add(closeButton);
            container.Children.Add(titleButton);
            TabStrip.Children.Add(container);
        }
    }

    private async Task CloseTabAsync(BrowserTab tab)
    {
        var index = _tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        _tabs.RemoveAt(index);
        tab.View.Dispose();

        if (_tabs.Count == 0)
        {
            await CreateNewTabAsync();
            return;
        }

        if (_activeTab == tab)
        {
            SetActiveTab(_tabs[Math.Clamp(index - 1, 0, _tabs.Count - 1)]);
        }
        else
        {
            RefreshTabs();
        }
    }

    private async Task OpenStoredPageAsync(StoredPage page)
    {
        if (_activeTab is null)
        {
            await CreateNewTabAsync();
        }

        if (TryParseSearchAddress(page.Url, out var query))
        {
            await SearchActiveTabAsync(query, addToHistory: true);
            return;
        }

        await NavigateOrSearchAsync(page.Url);
    }

    private void UpdateNavigationState()
    {
        BackButton.IsEnabled = _activeTab?.View.CanGoBack == true;
        ForwardButton.IsEnabled = _activeTab?.View.CanGoForward == true;
        ReloadButton.IsEnabled = _activeTab is not null;
        HomeButton.IsEnabled = _activeTab is not null;
        GoButton.IsEnabled = _activeTab is not null;
        BookmarkButton.IsEnabled = _activeTab is not null && !string.IsNullOrWhiteSpace(_activeTab.StoredAddress);
    }

    private void UpdateBookmarkState()
    {
        if (_activeTab is null || string.IsNullOrWhiteSpace(_activeTab.StoredAddress))
        {
            BookmarkButton.Content = StarIcon;
            BookmarkButton.ToolTip = "Bookmark";
            return;
        }

        var bookmarked = _data.Bookmarks.Any(item => SameStoredUrl(item.Url, _activeTab.StoredAddress));
        BookmarkButton.Content = bookmarked ? StarFilledIcon : StarIcon;
        BookmarkButton.ToolTip = bookmarked ? "Remove bookmark" : "Bookmark";
    }

    private void UpdateMaximizeButton()
    {
        var isMaximized = WindowState == WindowState.Maximized;
        MaximizeWindowButton.Content = isMaximized ? RestoreIcon : MaximizeIcon;
        MaximizeWindowButton.ToolTip = isMaximized ? "Restore" : "Maximize";
    }

    private void SetChromeEnabled(bool enabled)
    {
        AddressBar.IsEnabled = enabled;
        GoButton.IsEnabled = enabled;
        NewTabButton.IsEnabled = enabled;
        BackButton.IsEnabled = enabled && _activeTab?.View.CanGoBack == true;
        ForwardButton.IsEnabled = enabled && _activeTab?.View.CanGoForward == true;
        ReloadButton.IsEnabled = enabled;
        HomeButton.IsEnabled = enabled;
        BookmarkButton.IsEnabled = enabled;
        SettingsButton.IsEnabled = enabled;
    }

    private void AddHistory(string title, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var existing = _data.History.FirstOrDefault(item => SameStoredUrl(item.Url, url));
        if (existing is not null)
        {
            _data.History.Remove(existing);
        }

        _data.History.Insert(0, new StoredPage
        {
            Title = string.IsNullOrWhiteSpace(title) ? HostOrAddress(url) : title,
            Url = url,
            SavedAt = DateTimeOffset.UtcNow,
        });

        if (_data.History.Count > 120)
        {
            _data.History.RemoveRange(120, _data.History.Count - 120);
        }

        SaveBrowserData();
        RefreshStoredLists();
    }

    private void RefreshStoredLists()
    {
        HistoryList.ItemsSource = null;
        HistoryList.ItemsSource = _data.History;
        BookmarkList.ItemsSource = null;
        BookmarkList.ItemsSource = _data.Bookmarks;
    }

    private void LoadBrowserData()
    {
        if (BrowserDataStore.TryLoad(_dataPath, JsonOptions, out var loaded))
        {
            _data = loaded;
            return;
        }

        if (BrowserDataStore.TryLoadLegacyJson(_legacyJsonPath, JsonOptions, out var legacy))
        {
            _data = legacy;
            SaveBrowserData();
            try { File.Delete(_legacyJsonPath); } catch { }
            return;
        }

        _data = new BrowserData();
    }

    private void SaveBrowserData()
    {
        try
        {
            BrowserDataStore.Save(_dataPath, _data, JsonOptions);
        }
        catch
        {
            StatusText.Text = "Could not save browser data";
        }
    }

    private void LoadSettings()
    {
        var envPath = FindDotEnvPath();
        if (envPath is not null)
        {
            var dotEnv = ParseDotEnv(envPath);
            if (dotEnv.TryGetValue("CC0_CONTENT_API_KEY", out var envKey) && !string.IsNullOrEmpty(envKey))
                _resolvedApiKey = envKey;
        }

        if (!string.IsNullOrEmpty(_data.Settings.ApiKey))
            _resolvedApiKey = _data.Settings.ApiKey;
    }

    private string? FindDotEnvPath()
    {
        var scriptDir = Path.GetDirectoryName(FindRagScript());
        if (scriptDir is null) return null;
        var path = Path.Combine(scriptDir, ".env");
        return File.Exists(path) ? path : null;
    }

    internal static Dictionary<string, string> ParseDotEnv(string path)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(path))
        {
            var t = line.Trim();
            if (t.StartsWith('#') || !t.Contains('=')) continue;
            var idx = t.IndexOf('=');
            var key = t[..idx].Trim();
            var val = t[(idx + 1)..].Trim().Trim('"').Trim('\'');
            dict[key] = val;
        }
        return dict;
    }

    private static bool TryCreateWebUri(string input, out Uri uri)
    {
        if (Uri.TryCreate(input, UriKind.Absolute, out uri!)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return true;
        }

        if (input.Contains(' ') || input.Contains('\t'))
        {
            uri = null!;
            return false;
        }

        var candidate = input.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)
            || input.Contains('.')
            ? $"https://{input}"
            : "";

        if (candidate.Length > 0
            && Uri.TryCreate(candidate, UriKind.Absolute, out uri!)
            && !string.IsNullOrWhiteSpace(uri.Host))
        {
            return true;
        }

        uri = null!;
        return false;
    }

    private static string? FindRagScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "rag.py");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private BrowserTab? FindTab(object? sender)
    {
        return sender is WebView2 view
            ? _tabs.FirstOrDefault(tab => ReferenceEquals(tab.View, view))
            : null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup after timeout.
        }
    }

    private static bool SameStoredUrl(string left, string right)
    {
        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSearchAddress(string query)
    {
        return $"rag://search?q={Uri.EscapeDataString(query)}";
    }

    private static bool TryParseSearchAddress(string address, out string query)
    {
        query = "";
        if (!address.StartsWith("rag://search?", StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var rawQuery = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .FirstOrDefault(pair => pair.Length == 2 && pair[0] == "q");
        if (rawQuery is null)
        {
            return false;
        }

        query = Uri.UnescapeDataString(rawQuery[1]);
        return !string.IsNullOrWhiteSpace(query);
    }

    private static string HostOrAddress(string address)
    {
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.Host;
        }

        return address;
    }

    private static string Shorten(string value, int maxLength)
    {
        value = string.IsNullOrWhiteSpace(value) ? "Untitled" : value.Trim();
        return value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 3)] + "...";
    }

    private static string Html(string value)
    {
        return WebUtility.HtmlEncode(value ?? "");
    }

    private static string BuildHomeHtml()
    {
        return BuildShellHtml("Neurvance", """
            <main class="center">
                <section class="clock">
                    <div id="time">--:--</div>
                    <div id="date"></div>
                    <div class="greeting">Good Evening</div>
                    <div id="weather-box" style="display:none">
                        <div id="weather-desc" class="weather-desc"></div>
                        <div id="weather-icon" class="weather-icon"></div>
                    </div>
                </section>
            </main>
            <button class="gear-btn" title="Settings">&#x2699;</button>
            <script>
                const time = document.getElementById('time');
                const date = document.getElementById('date');
                const greeting = document.querySelector('.greeting');
                function tick() {
                    const now = new Date();
                    time.textContent = now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
                    date.textContent = now.toLocaleDateString([], { weekday: 'long', month: 'long', day: 'numeric' });
                    const h = now.getHours();
                    greeting.textContent = h < 12 ? 'Good Morning' : h < 18 ? 'Good Afternoon' : 'Good Evening';
                }
                tick();
                setInterval(tick, 1000);

                async function loadWeather() {
                    if (!navigator.geolocation) return;
                    try {
                        const pos = await new Promise((res, rej) =>
                            navigator.geolocation.getCurrentPosition(res, rej, { timeout: 6000 }));
                        const { latitude: lat, longitude: lon } = pos.coords;
                        const r = await fetch(
                            'https://api.open-meteo.com/v1/forecast?latitude=' + lat +
                            '&longitude=' + lon +
                            '&current=temperature_2m,weathercode&temperature_unit=celsius&timezone=auto');
                        if (!r.ok) return;
                        const d = await r.json();
                        const temp = Math.round(d.current.temperature_2m);
                        const code = d.current.weathercode;
                        document.getElementById('weather-icon').textContent = weatherIcon(code) + ' ' + temp + '°';
                        document.getElementById('weather-desc').textContent = weatherDesc(code);
                        document.getElementById('weather-box').style.display = '';
                    } catch {}
                }
                function weatherIcon(c) {
                    if (c === 0) return '☀️';
                    if (c <= 3) return '⛅';
                    if (c <= 48) return '🌫️';
                    if (c <= 67) return '🌦️';
                    if (c <= 77) return '❄️';
                    if (c <= 82) return '🌧️';
                    return '⛈️';
                }
                function weatherDesc(c) {
                    if (c === 0) return 'Clear sky';
                    if (c === 1) return 'Mainly clear';
                    if (c === 2) return 'Partly cloudy';
                    if (c === 3) return 'Overcast';
                    if (c <= 48) return 'Foggy';
                    if (c <= 55) return 'Drizzle';
                    if (c <= 67) return 'Rainy';
                    if (c <= 77) return 'Snowy';
                    if (c <= 82) return 'Showers';
                    return 'Thunderstorm';
                }
                loadWeather();
            </script>
            """);
    }

    private static string BuildResultsHtml(RagResponse response)
    {
        var results = response.Results
            .Where(result => result is not null)
            .Select((result, index) => BuildResultItem(result, index + 1))
            .ToList();

        var body = results.Count == 0
            ? $"""
                <main class="content">
                    <section class="search-head">
                        <span>RAG</span>
                        <h1>{Html(response.Query)}</h1>
                        <p>No links found.</p>
                    </section>
                </main>
                """
            : $"""
                <main class="content">
                    <section class="search-head">
                        <span>RAG</span>
                        <h1>{Html(response.Query)}</h1>
                        <p>{response.TotalResults} result{(response.TotalResults == 1 ? "" : "s")} · {response.ProcessingTimeMs:0.##} ms</p>
                    </section>
                    <section class="results">
                        {string.Join(Environment.NewLine, results)}
                    </section>
                </main>
                """;

        return BuildShellHtml($"Search: {response.Query}", body);
    }

    private static string BuildResultItem(RagResult result, int index)
    {
        var url = NormalizeResultUrl(result.Url);
        var title = string.IsNullOrWhiteSpace(result.Title) ? $"RAG result {index}" : result.Title;
        var source = string.IsNullOrWhiteSpace(result.Source) ? "Neurvance RAG" : result.Source;
        var snippet = string.IsNullOrWhiteSpace(result.Snippet) ? "No preview text was returned." : result.Snippet;

        var titleMarkup = url is null
            ? $"<h2>{Html(title)}</h2>"
            : $"<a class=\"title\" href=\"{Html(url)}\"><h2>{Html(title)}</h2></a>";
        var urlMarkup = url is null
            ? "<p class=\"url muted\">No valid URL returned by RAG</p>"
            : $"<p class=\"url\">{Html(url)}</p>";

        return $"""
            <article class="result">
                <div>
                    <p class="source">{Html(source)}</p>
                    {titleMarkup}
                    {urlMarkup}
                    <p class="snippet">{Html(snippet)}</p>
                </div>
            </article>
            """;
    }

    private static string? NormalizeResultUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        url = url.Trim();
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute.ToString();
        }

        if (!url.Contains(' ') && url.Contains('.') && Uri.TryCreate($"https://{url}", UriKind.Absolute, out var withScheme))
        {
            return withScheme.ToString();
        }

        return null;
    }

    private static string BuildShellHtml(string title, string body)
    {
        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>{{Html(title)}}</title>
                <style>
                    * { box-sizing: border-box; margin: 0; padding: 0; }
                    body {
                        font-family: "Segoe UI", system-ui, sans-serif;
                        background: #1e1e1e;
                        color: #e0e0e0;
                        min-height: 100vh;
                    }
                    a { color: inherit; text-decoration: none; }

                    /* ── New-tab page ── */
                    .center {
                        min-height: 100vh;
                        display: flex;
                        flex-direction: column;
                        align-items: center;
                        justify-content: center;
                        gap: 0;
                    }
                    .clock { text-align: center; }
                    #time {
                        font-size: 64px;
                        font-weight: 200;
                        letter-spacing: -1px;
                        color: #f0f0f0;
                        line-height: 1;
                    }
                    #date {
                        margin-top: 8px;
                        font-size: 14px;
                        color: #888;
                        text-transform: capitalize;
                    }
                    .greeting {
                        margin-top: 48px;
                        font-size: 22px;
                        font-weight: 300;
                        color: #ccc;
                    }
                    #weather-box { margin-top: 10px; text-align: center; }
                    .weather-desc { font-size: 13px; color: #888; }
                    .weather-icon { font-size: 20px; margin-top: 4px; color: #aaa; }
                    .gear-btn {
                        position: fixed;
                        bottom: 18px;
                        right: 18px;
                        background: transparent;
                        border: 1px solid #333;
                        border-radius: 50%;
                        width: 34px;
                        height: 34px;
                        cursor: pointer;
                        color: #555;
                        font-size: 16px;
                        display: grid;
                        place-items: center;
                        transition: border-color .15s, color .15s;
                    }
                    .gear-btn:hover { border-color: #555; color: #aaa; }

                    /* ── RAG / error pages ── */
                    .content {
                        width: min(780px, calc(100% - 48px));
                        margin: 0 auto;
                        padding: 36px 0 60px;
                    }
                    .message {
                        padding: 28px;
                        border: 1px solid #333;
                        border-radius: 8px;
                        background: #252525;
                    }
                    .search-head {
                        padding-bottom: 14px;
                        border-bottom: 1px solid #2e2e2e;
                        margin-bottom: 14px;
                    }
                    .search-head span, .source {
                        font-size: 10px;
                        letter-spacing: .1em;
                        text-transform: uppercase;
                        color: #666;
                    }
                    h1 { font-size: 22px; font-weight: 400; color: #e0e0e0; margin-top: 6px; }
                    h2 { font-size: 15px; font-weight: 500; color: #d0d0d0; }
                    p { font-size: 13px; line-height: 1.55; color: #999; margin-top: 4px; }
                    .muted { color: #555; }
                    .results { display: grid; gap: 6px; }
                    .result {
                        padding: 12px 14px;
                        border: 1px solid #2e2e2e;
                        border-radius: 6px;
                        background: #242424;
                        transition: background .1s;
                    }
                    .result:hover { background: #2c2c2c; }
                    .title:hover h2 { color: #6aacff; }
                    .url { font-size: 11px; color: #5a9a6a; margin: 3px 0 6px; word-break: break-all; }
                    .snippet { font-size: 13px; color: #888; max-width: 80ch; }
                </style>
            </head>
            <body>
            {{body}}
            </body>
            </html>
            """;
    }

    private sealed class BrowserTab
    {
        public required int Id { get; init; }
        public required string Title { get; set; }
        public string StoredAddress { get; set; } = "";
        public string SearchQuery { get; set; } = "";
        public bool IsSearchPage { get; set; }
        public WebView2 View { get; } = new();
    }

    internal sealed class BrowserData
    {
        public List<StoredPage> History { get; set; } = [];
        public List<StoredPage> Bookmarks { get; set; } = [];
        public AppSettings Settings { get; set; } = new();
    }

    internal sealed class AppSettings
    {
        public string ApiKey { get; set; } = "";
    }

    internal sealed class StoredPage
    {
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public DateTimeOffset SavedAt { get; set; }
    }

    private sealed class RagResponse
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("query")]
        public string Query { get; set; } = "";

        [JsonPropertyName("total_results")]
        public int TotalResults { get; set; }

        [JsonPropertyName("processing_time_ms")]
        public double ProcessingTimeMs { get; set; }

        [JsonPropertyName("results")]
        public List<RagResult> Results { get; set; } = [];

        [JsonPropertyName("error")]
        public string Error { get; set; } = "";

        public static RagResponse Failed(string query, string error)
        {
            return new RagResponse
            {
                Ok = false,
                Query = query,
                Error = error,
            };
        }
    }

    private sealed class RagResult
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("snippet")]
        public string Snippet { get; set; } = "";

        [JsonPropertyName("source")]
        public string Source { get; set; } = "";
    }
}
