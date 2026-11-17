using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace SCPSL.WinUI;

public sealed partial class MainWindow : Window
{
    private const string ProjectUrl = "https://github.com/hayabusa255/SCPSLTranslationJP";
    private const string CarbonUrl =
        "https://support.microsoft.com/ja-jp/windows/deployment/updates-lifecycle/windows-update-is-now-carbon-aware";

    private readonly RustCoreBridge _core = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMinutes(30) };
    private readonly List<ReleaseNote> _releases = [];
    private readonly List<FolderCandidate> _games = [];

    private string? _selectedGame;
    private string? _latestTag;
    private bool _checking;
    private bool _installing;
    private bool _foldersRendering;

    private sealed record FolderCandidate(string Path, string Source);
    private sealed record ReleaseNote(string Tag, string Title, string Summary,
        string Notes, string? PublishedAt);

    public MainWindow()
    {
        InitializeComponent();
        Title = "SCP: SL 日本語化インストーラー";
        AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1080, Height = 710 });
        Nav.SelectedItem = NavUpdate;
        Nav.Loaded += async (_, _) => await InitializeAsync();
        _refreshTimer.Tick += async (_, _) => {
            if (!_checking && !_installing)
                await Task.WhenAll(RefreshAsync(), LoadHistoryAsync());
        };
        _refreshTimer.Start();
    }

    private void ShowError(string message)
    {
        ErrorBar.Title = "処理に失敗しました";
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private async Task InitializeAsync()
    {
        await DetectAsync();
        await Task.WhenAll(RefreshAsync(), LoadHistoryAsync());
    }

    private void SetPage(string name)
    {
        UpdatePage.Visibility = name == "Update" ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = name == "History" ? Visibility.Visible : Visibility.Collapsed;
        FoldersPage.Visibility = name == "Folders" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = name == "About" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnNavigationChanged(NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string page)
            SetPage(page);
    }

    private void UpdatePresentation()
    {
        var versions = _installedVersions;
        bool upToDate = _latestTag is not null && versions.Contains(_latestTag);
        InstalledText.Text = "インストール済み: " +
            (versions.Count > 0 ? string.Join(" / ", versions) : "未インストール");

        RefreshButton.IsEnabled = !_checking && !_installing;
        InstallButton.IsEnabled = !_checking && !_installing && _selectedGame != null;
        EcoRow.Visibility = !_checking && !_installing && upToDate
            ? Visibility.Visible : Visibility.Collapsed;
        LatestDetailsButton.Visibility = _latestTag is null
            ? Visibility.Collapsed : Visibility.Visible;

        if (_installing)
        {
            StatusTitle.Text = "更新プログラムをインストールしています";
            StatusDescription.Text = "日本語翻訳を適用しています";
            AvailableUpdate.Visibility = Visibility.Collapsed;
        }
        else if (_checking)
        {
            StatusTitle.Text = "更新プログラムを確認しています";
            StatusDescription.Text = "GitHub Releasesから更新情報を取得しています";
            AvailableUpdate.Visibility = Visibility.Collapsed;
        }
        else if (_latestTag == null)
        {
            StatusTitle.Text = "更新プログラムを確認できませんでした";
            StatusDescription.Text = "インターネット接続を確認して、もう一度お試しください";
            AvailableUpdate.Visibility = Visibility.Collapsed;
        }
        else if (_selectedGame == null)
        {
            StatusTitle.Text = "インストール先を選択してください";
            StatusDescription.Text = "SCP: Secret Laboratoryの保存先を指定してください";
            AvailableUpdate.Visibility = Visibility.Collapsed;
        }
        else if (upToDate)
        {
            StatusTitle.Text = "最新の状態です";
            StatusDescription.Text = "日本語翻訳は最新の状態です";
            AvailableUpdate.Visibility = Visibility.Collapsed;
        }
        else
        {
            StatusTitle.Text = "更新プログラムを利用できます";
            StatusDescription.Text = "日本語翻訳の新しいバージョンをダウンロードできます";
            AvailableTitle.Text = "SCP: SL 日本語翻訳 " + _latestTag;
            AvailableUpdate.Visibility = Visibility.Visible;
        }
        GameFolderText.Text = _selectedGame ?? "未検出 — フォルダーを指定してください";
    }

    private List<string> _installedVersions = [];

    private async Task RefreshInstalledAsync()
    {
        _installedVersions = [];
        if (_selectedGame is null)
        {
            UpdatePresentation();
            return;
        }
        try
        {
            JsonElement response = await _core.RunAsync("inspect", _selectedGame);
            if (response.TryGetProperty("installedVersions", out var list))
                _installedVersions = list.EnumerateArray()
                    .Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList();
        }
        catch (Exception exception) { ShowError(exception.Message); }
        UpdatePresentation();
    }

    private async Task RefreshAsync()
    {
        if (_checking || _installing) return;
        _checking = true;
        UpdatePresentation();
        try
        {
            JsonElement response = await _core.RunAsync("latest");
            _latestTag = Text(response, "tag");
            LastChecked.Text = "最終チェック: " + DateTime.Now.ToString("HH:mm", CultureInfo.CurrentCulture);
        }
        catch (Exception exception)
        {
            _latestTag = null;
            ShowError(exception.Message);
        }
        finally
        {
            _checking = false;
            await RefreshInstalledAsync();
            UpdatePresentation();
        }
    }

    private async Task DetectAsync()
    {
        try
        {
            JsonElement data = await _core.RunAsync("detect");
            _games.Clear();
            foreach (var item in data.EnumerateArray())
            {
                var path = Text(item, "path");
                if (path != null)
                    _games.Add(new FolderCandidate(path, Text(item, "source") ?? "Steam"));
            }
            if (_selectedGame == null && _games.Count > 0)
                _selectedGame = _games[0].Path;
            RenderFolders();
            await RefreshInstalledAsync();
        }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    private void RenderFolders()
    {
        _foldersRendering = true;
        FolderList.Items.Clear();
        foreach (var candidate in _games)
        {
            var view = new StackPanel { Spacing = 5 };
            view.Children.Add(new TextBlock {
                Text = candidate.Source, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            view.Children.Add(new TextBlock {
                Text = candidate.Path, TextWrapping = TextWrapping.WrapWholeWords,
                Opacity = 0.72,
                FontSize = 12
            });
            var item = new ListViewItem { Content = view, Tag = candidate.Path,
                HorizontalContentAlignment = HorizontalAlignment.Stretch };
            FolderList.Items.Add(item);
            if (_selectedGame == candidate.Path)
                FolderList.SelectedItem = item;
        }
        _foldersRendering = false;
    }

    private async void FolderList_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_foldersRendering) return;
        if (FolderList.SelectedItem is ListViewItem item && item.Tag is string path)
        {
            _selectedGame = path;
            GameFolderText.Text = path;
            await RefreshInstalledAsync();
        }
    }

    private async Task LoadHistoryAsync()
    {
        HistoryStatus.Text = "GitHub Releasesから取得中...";
        try
        {
            JsonElement data = await _core.RunAsync("history");
            _releases.Clear();
            foreach (var item in data.EnumerateArray())
            {
                var tag = Text(item, "tag");
                if (tag is null) continue;
                _releases.Add(new ReleaseNote(
                    tag,
                    Text(item, "title") ?? tag,
                    Text(item, "summary") ?? "",
                    Text(item, "notes") ?? "",
                    Text(item, "publishedAt")
                ));
            }
            HistoryStatus.Text = $"GitHubから{_releases.Count}件の更新履歴を取得しました";
            RenderHistory();
        }
        catch (Exception exception)
        {
            HistoryStatus.Text = "更新履歴を取得できませんでした";
            ShowError(exception.Message);
        }
    }

    private readonly Dictionary<string, Expander> _historyExpanders = [];

    private void RenderHistory()
    {
        HistoryItems.Children.Clear();
        _historyExpanders.Clear();
        foreach (var release in _releases)
        {
            var heading = new StackPanel { Spacing = 5, Margin = new Thickness(3, 5, 0, 5) };
            heading.Children.Add(new TextBlock {
                Text = release.Title, FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            heading.Children.Add(new TextBlock {
                Text = release.Summary, FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
            string date = DateTimeOffset.TryParse(release.PublishedAt, out var parsed)
                ? parsed.ToLocalTime().ToString("yyyy年M月d日")
                : "公開日不明";
            heading.Children.Add(new TextBlock {
                Text = release.Tag + " · " + date,
                FontSize = 11
            });

            var details = new StackPanel { Spacing = 14, Padding = new Thickness(16) };
            details.Children.Add(new TextBlock { Text = "更新内容",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            details.Children.Add(new TextBlock {
                Text = string.IsNullOrWhiteSpace(release.Notes)
                    ? "変更内容は記載されていません" : release.Notes,
                TextWrapping = TextWrapping.Wrap, FontSize = 12
            });
            var link = new HyperlinkButton { Content = "GitHubでこのリリースを見る",
                Padding = new Thickness(0) };
            link.Click += async (_, _) =>
                await Launcher.LaunchUriAsync(new Uri(
                    ProjectUrl + "/releases/tag/" + Uri.EscapeDataString(release.Tag)));
            details.Children.Add(link);

            var expander = new Expander { Header = heading, Content = details };
            _historyExpanders[release.Tag] = expander;
            HistoryItems.Children.Add(expander);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await Task.WhenAll(RefreshAsync(), LoadHistoryAsync());
    }

    private async void HistoryRefresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadHistoryAsync();
    }

    private async void LatestDetails_Click(object sender, RoutedEventArgs e)
    {
        Nav.SelectedItem = NavHistory;
        await LoadHistoryAsync();
        if (_latestTag is not null && _historyExpanders.TryGetValue(_latestTag, out var item))
            item.IsExpanded = true;
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGame is null || _installing) return;
        _installing = true;
        InstallProgress.Visibility = Visibility.Visible;
        ProgressValue.Value = 0;
        ProgressStage.Text = "準備しています";
        UpdatePresentation();
        var selected = _selectedGame;
        try
        {
            JsonElement result = await _core.RunAsync("install", selected,
                p => DispatcherQueue.TryEnqueue(() =>
                {
                    ProgressValue.Value = p.Percent;
                    ProgressStage.Text = p.Stage;
                    ProgressDetail.Text = p.Detail;
                }));
            ProgressValue.Value = 100;
            ProgressStage.Text = "更新が完了しました";
            ProgressDetail.Text = Text(result, "languageStatus") ??
                "翻訳ファイルを配置しました";
            await RefreshInstalledAsync();
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            ProgressStage.Text = "更新に失敗しました";
        }
        finally
        {
            _installing = false;
            UpdatePresentation();
        }
    }

    private void NavigateToFolders_Click(object sender, RoutedEventArgs e)
    {
        Nav.SelectedItem = NavFolders;
    }

    private async void Rescan_Click(object sender, RoutedEventArgs e)
    {
        await DetectAsync();
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var chosen = await picker.PickSingleFolderAsync();
            if (chosen is null) return;
            JsonElement data = await _core.RunAsync("validate", chosen.Path);
            var path = Text(data, "path");
            if (path is null) return;
            _selectedGame = path;
            if (!_games.Any(g => g.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                _games.Insert(0, new FolderCandidate(path, "手動選択"));
            RenderFolders();
            await RefreshInstalledAsync();
        }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    private static async Task OpenAsync(string url) =>
        await Launcher.LaunchUriAsync(new Uri(url));

    private async void EcoDetails_Click(object sender, RoutedEventArgs e) =>
        await OpenAsync(CarbonUrl);

    private async void ProjectLink_Click(object sender, RoutedEventArgs e) =>
        await OpenAsync(ProjectUrl);
}