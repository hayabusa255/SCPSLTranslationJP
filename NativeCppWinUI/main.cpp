#include <windows.h>
#include "resource_loader.h"
#include <appmodel.h>
#include <shellapi.h>
#include <shobjidl.h>
#include <string>
#include <vector>
#include <thread>
#include <functional>
#include <optional>
#include <memory>
#include <algorithm>
#include <cstring>

#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Data.Json.h>
#include <winrt/Windows.UI.Xaml.Interop.h>
#include <winrt/Microsoft.UI.Xaml.h>
#include <winrt/Microsoft.UI.Xaml.Markup.h>
#include <winrt/Microsoft.UI.Xaml.XamlTypeInfo.h>
#include <winrt/Microsoft.UI.Xaml.Controls.h>
#include <winrt/Microsoft.UI.Xaml.Controls.Primitives.h>
#include <winrt/Microsoft.UI.Xaml.Media.h>
#include <winrt/Microsoft.UI.Xaml.Media.Imaging.h>
#include <winrt/Microsoft.UI.Xaml.Shapes.h>
#include "markdown_renderer.h"
#include <winrt/Windows.UI.Text.h>
#include <winrt/Windows.UI.h>
#include <winrt/Microsoft.UI.Dispatching.h>


using namespace winrt;
using namespace Windows::Foundation;
using namespace Windows::Data::Json;
using namespace Microsoft::UI::Xaml;
using namespace Microsoft::UI::Xaml::Controls;
using namespace Microsoft::UI::Xaml::Media;
using namespace Microsoft::UI::Xaml::Media::Imaging;
using namespace Microsoft::UI::Xaml::Shapes;

static void StartupTrace(char const* marker) noexcept {
    wchar_t dir[MAX_PATH]{};
    if (!GetTempPathW(MAX_PATH,dir)) return;
    std::wstring path=std::wstring(dir)+L"scpsl-cpp-startup.log";
    HANDLE f=CreateFileW(path.c_str(),FILE_APPEND_DATA,
        FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
    if (f==INVALID_HANDLE_VALUE)return;
    DWORD written=0;
    WriteFile(f,marker,static_cast<DWORD>(strlen(marker)),&written,nullptr);
    WriteFile(f,"\r\n",2,&written,nullptr);
    CloseHandle(f);
}

static std::wstring ExecutableFolder() {
    wchar_t buffer[32768]{};
    GetModuleFileNameW(nullptr, buffer, 32768);
    std::wstring path(buffer);
    auto pos = path.find_last_of(L"\\/");
    return pos == std::wstring::npos ? L"." : path.substr(0,pos);
}

static std::wstring ToWide(std::string const& bytes) {
    if (bytes.empty()) return {};
    auto n = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, bytes.data(),
        static_cast<int>(bytes.size()), nullptr,0);
    if (n <= 0) return {};
    std::wstring result(static_cast<size_t>(n),L'\0');
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, bytes.data(),
        static_cast<int>(bytes.size()),result.data(),n);
    return result;
}

static void OpenUri(wchar_t const* url) {
    ShellExecuteW(nullptr,L"open",url,nullptr,nullptr,SW_SHOWNORMAL);
}

struct EngineResponse {
    std::string output;
    unsigned long exitCode = 1;
    std::wstring error;
};

static EngineResponse ExecuteEngine(
    std::wstring const& command,
    std::wstring const& directory,
    std::function<void(std::string const&)> progress)
{
    EngineResponse reply;
    std::wstring exe=BundledPath(L"scpsl-native-core.exe");
    if (GetFileAttributesW(exe.c_str())==INVALID_FILE_ATTRIBUTES) {
        reply.error=L"scpsl-native-core.exe がありません";
        return reply;
    }
    SECURITY_ATTRIBUTES sec{sizeof(sec),nullptr,TRUE};
    HANDLE readPipe=nullptr,writePipe=nullptr;
    if(!CreatePipe(&readPipe,&writePipe,&sec,0)) {
        reply.error=L"Rust実行エンジンに接続できません";return reply;
    }
    SetHandleInformation(readPipe,HANDLE_FLAG_INHERIT,0);
    STARTUPINFOW start{sizeof(start)};
    start.dwFlags=STARTF_USESTDHANDLES;
    start.hStdOutput=writePipe;
    start.hStdError=writePipe;
    start.hStdInput=GetStdHandle(STD_INPUT_HANDLE);
    PROCESS_INFORMATION proc{};
    std::wstring cmd=L"\""+exe+L"\" "+command;
    if (!directory.empty()) cmd+=L" \""+directory+L"\"";
    auto created=CreateProcessW(exe.c_str(),cmd.data(),nullptr,nullptr,TRUE,
       CREATE_NO_WINDOW,nullptr,BundledDirectory().c_str(),&start,&proc);
    CloseHandle(writePipe);
    if(!created) {
        CloseHandle(readPipe);
        reply.error=L"Rustエンジンを起動できません";
        return reply;
    }
    std::string pending;
    char buffer[8192]{};
    DWORD read=0;
    while (ReadFile(readPipe,buffer,sizeof(buffer),&read,nullptr)&&read>0) {
        reply.output.append(buffer,read);
        pending.append(buffer,read);
        size_t newline;
        while((newline=pending.find('\n'))!=std::string::npos) {
            auto line=pending.substr(0,newline);
            pending.erase(0,newline+1);
            if(line.find("\"kind\":\"progress\"")!=std::string::npos)
                progress(line);
        }
        if(reply.output.size()>6*1024*1024) break;
    }
    CloseHandle(readPipe);
    WaitForSingleObject(proc.hProcess,60000);
    GetExitCodeProcess(proc.hProcess,&reply.exitCode);
    CloseHandle(proc.hThread);
    CloseHandle(proc.hProcess);
    return reply;
}

static TextBlock MakeText(hstring const& text,double fontSize=13) {
    TextBlock item;
    item.Text(text);
    item.FontSize(fontSize);
    item.TextWrapping(TextWrapping::Wrap);
    return item;
}
static FontIcon MakeIcon(wchar_t const* glyph,double size=19) {
    FontIcon icon;
    icon.Glyph(glyph);
    icon.FontSize(size);
    icon.FontFamily(FontFamily(L"Segoe Fluent Icons"));
    return icon;
}
static Button MakeButton(hstring const& label) {
    Button b;
    b.Content(box_value(label));
    return b;
}

struct App : ApplicationT<App,Microsoft::UI::Xaml::Markup::IXamlMetadataProvider>
{
    Microsoft::UI::Xaml::XamlTypeInfo::XamlControlsXamlMetaDataProvider m_metadata{};
    Microsoft::UI::Xaml::Markup::IXamlType GetXamlType(Windows::UI::Xaml::Interop::TypeName const& name) {return m_metadata.GetXamlType(name);}
    Microsoft::UI::Xaml::Markup::IXamlType GetXamlType(hstring const& name) {return m_metadata.GetXamlType(name);}
    com_array<Microsoft::UI::Xaml::Markup::XmlnsDefinition> GetXmlnsDefinitions(){return m_metadata.GetXmlnsDefinitions();}

    Window m_window{nullptr};
    ListView m_nav{nullptr};
    ListViewItem m_updateItem{nullptr},m_historyItem{nullptr},m_folderItem{nullptr},m_aboutItem{nullptr};
    FontIcon m_statusIcon{nullptr};
    Border m_availableCard{nullptr};
    TextBlock m_availableVersion{nullptr};
    std::vector<std::wstring> m_detectedPaths;
    ScrollViewer m_updatePage{nullptr},m_historyPage{nullptr},m_folderPage{nullptr},m_aboutPage{nullptr};
    StackPanel m_historyList{nullptr},m_installArea{nullptr},m_ecoRow{nullptr};
    ListView m_folderList{nullptr};
    TextBlock m_historyStatus{nullptr};
    std::vector<std::pair<std::wstring,Expander>> m_releaseExpanders;
    bool m_expandLatestWhenLoaded=false;
    TextBlock m_status{nullptr},m_subtitle{nullptr},m_lastChecked{nullptr},m_gameText{nullptr},m_installed{nullptr},m_progressText{nullptr};
    Button m_installButton{nullptr},m_checkButton{nullptr};
    HyperlinkButton m_releaseLink{nullptr};
    ProgressBar m_progress{nullptr};
    DispatcherTimer m_timer{nullptr};
    std::wstring m_gamePath,m_latestTag;
    std::vector<std::wstring> m_versions;
    bool m_busy=false;

    void OnLaunched(LaunchActivatedEventArgs const&) {
        StartupTrace("OnLaunched-enter");
        // Default Fluent control templates must be loaded before any WinUI controls.
        // Without this, NavigationView/Expander may crash in Microsoft.UI.Xaml.dll.
        StartupTrace("Resources-enter");
        Resources().MergedDictionaries().Append(XamlControlsResources());
        StartupTrace("Resources-ready");
        StartupTrace("Window-create-enter");
        m_window=Window();
        StartupTrace("Window-create-ok");
        m_window.Title(L"SCP: SL 日本語化インストーラー");
        m_window.SystemBackdrop(MicaBackdrop());
        StartupTrace("Shell-create-enter");
        auto root=BuildShell();
        StartupTrace("Content-ready");
        m_window.Content(root);
        StartupTrace("Shell-created");
        StartupTrace("Window-activate-enter");
        m_window.Activate();
        StartupTrace("Window-activated");
        m_timer=DispatcherTimer();
        m_timer.Interval(std::chrono::minutes(30));
        m_timer.Tick([this](auto const&,auto const&) {
            if(!m_busy) { LoadLatest();LoadHistory(); }
        });
        m_timer.Start();
        LoadGames();
        LoadLatest();
        LoadHistory();
    }

    // Windows Settings-like navigation is independent from content.
    // No NavigationView ContentGrid => no giant rounded top/content border.
    ListViewItem MakeNavItem(hstring const& label,wchar_t const* glyph) {
        ListViewItem item;
        item.MinHeight(48);
        item.HorizontalContentAlignment(HorizontalAlignment::Stretch);
        StackPanel row;
        row.Orientation(Orientation::Horizontal);
        row.Spacing(14);
        auto icon=MakeIcon(glyph,19);
        icon.VerticalAlignment(VerticalAlignment::Center);
        row.Children().Append(icon);
        auto name=MakeText(label,13.5);
        name.VerticalAlignment(VerticalAlignment::Center);
        row.Children().Append(name);
        item.Content(row);
        return item;
    }

    Grid BuildShell(){
        Grid root;
        ColumnDefinition sidebarWidth;
        sidebarWidth.Width(GridLength{315,GridUnitType::Pixel});
        ColumnDefinition mainWidth;
        mainWidth.Width(GridLength{1,GridUnitType::Star});
        root.ColumnDefinitions().Append(sidebarWidth);
        root.ColumnDefinitions().Append(mainWidth);

        StackPanel sidebar;
        sidebar.Padding(Thickness{14,20,11,10});
        sidebar.Spacing(14);
        auto top=StackPanel();
        top.Orientation(Orientation::Horizontal);
        top.Spacing(8);
        auto back=Button();
        back.Content(MakeIcon(L"\uE72B",16));
        back.MinWidth(35);back.MinHeight(32);
        back.Click([this](auto const&,auto const&){m_nav.SelectedIndex(0);});
        top.Children().Append(back);
        auto settings=MakeText(L"設定",14);
        settings.VerticalAlignment(VerticalAlignment::Center);
        top.Children().Append(settings);
        sidebar.Children().Append(top);
        sidebar.Children().Append(BuildProfile());

        m_nav=ListView();
        m_nav.BorderThickness(Thickness{0});
        m_nav.Background(SolidColorBrush(Windows::UI::Colors::Transparent()));
        m_nav.SelectionMode(ListViewSelectionMode::Single);
        m_nav.HorizontalAlignment(HorizontalAlignment::Stretch);
        m_updateItem=MakeNavItem(L"日本語訳アップデート",L"\uE895");
        m_historyItem=MakeNavItem(L"更新履歴",L"\uE81C");
        m_folderItem=MakeNavItem(L"インストール先",L"\uE8B7");
        m_aboutItem=MakeNavItem(L"このアプリについて",L"\uE946");
        m_nav.Items().Append(m_updateItem);
        m_nav.Items().Append(m_historyItem);
        m_nav.Items().Append(m_folderItem);
        m_nav.Items().Append(m_aboutItem);
        m_nav.SelectionChanged([this](auto const&,auto const&){
            int index=m_nav.SelectedIndex();
            if(index>=0 && index<4)SwitchPage(index);
        });
        sidebar.Children().Append(m_nav);
        root.Children().Append(sidebar);
        auto content=BuildContent();
        Grid::SetColumn(content,1);
        root.Children().Append(content);
        m_nav.SelectedIndex(0);
        return root;
    }

    StackPanel BuildProfile(){
        StackPanel wrapper;
        wrapper.Spacing(9);
        wrapper.Padding(Thickness{3,17,4,14});
        StackPanel row;
        row.Orientation(Orientation::Horizontal);
        row.Spacing(12);
        Microsoft::UI::Xaml::Shapes::Ellipse avatar;
        avatar.Width(64);avatar.Height(64);
        try {
            auto path=BundledPath(L"researcher.jpg");
            std::replace(path.begin(),path.end(),L'\\',L'/');
            auto brush=ImageBrush();
            brush.ImageSource(BitmapImage(Uri(L"file:///"+path)));
            brush.Stretch(Stretch::UniformToFill);
            avatar.Fill(brush);
        }catch(...){}
        row.Children().Append(avatar);
        StackPanel account;
        account.Spacing(3);
        account.VerticalAlignment(VerticalAlignment::Center);
        auto name=MakeText(L"研究員",14);
        name.FontWeight(Windows::UI::Text::FontWeights::SemiBold());
        account.Children().Append(name);
        auto email=MakeText(L"researcher@scp-foundation.jp",11);
        email.Opacity(0.75);
        account.Children().Append(email);
        row.Children().Append(account);
        wrapper.Children().Append(row);

        // Windows Accounts > Access work or school shows "<organization>による管理".
        StackPanel managed;
        managed.Orientation(Orientation::Horizontal);
        managed.Spacing(7);
        managed.Margin(Thickness{2,5,0,0});
        try {
            auto path=BundledPath(L"scp-emblem.png");
            std::replace(path.begin(),path.end(),L'\\',L'/');
            BitmapIcon logo;
            logo.UriSource(Uri(L"file:///"+path));
            logo.ShowAsMonochrome(true);
            logo.Width(16);logo.Height(16);
            managed.Children().Append(logo);
        }catch(...){
            managed.Children().Append(MakeIcon(L"\uE821",16));
        }
        auto label=MakeText(L"SCP Foundation による管理",11);
        label.Opacity(0.75);
        managed.Children().Append(label);
        wrapper.Children().Append(managed);
        return wrapper;
    }

    ScrollViewer MakePage(StackPanel content) {
        content.Padding(Thickness{37,30,33,56});
        content.Spacing(15);
        ScrollViewer scroll;
        scroll.Content(content);
        return scroll;
    }

    TextBlock Title(hstring const& label) {
        auto t=MakeText(label,29);
        t.FontWeight(Windows::UI::Text::FontWeights::SemiBold());
        t.Margin(Thickness{0,0,0,16});
        return t;
    }

    Grid BuildContent() {
        Grid root;
        m_updatePage=BuildUpdate();
        m_historyPage=BuildHistory();
        m_folderPage=BuildFolders();
        m_aboutPage=BuildAbout();
        root.Children().Append(m_updatePage);
        root.Children().Append(m_historyPage);
        root.Children().Append(m_folderPage);
        root.Children().Append(m_aboutPage);
        SwitchPage(0);
        return root;
    }

    Brush ThemeBrush(wchar_t const* key){
        try{
            auto value=Application::Current().Resources().Lookup(box_value(hstring(key)));
            if(value)return value.as<Brush>();
        }catch(...){}
        return SolidColorBrush(Windows::UI::Colors::Transparent());
    }
    Border SettingsCard(UIElement const& content){
        Border card;
        card.BorderThickness(Thickness{1});
        card.BorderBrush(ThemeBrush(L"CardStrokeColorDefaultBrush"));
        card.Background(ThemeBrush(L"CardBackgroundFillColorDefaultBrush"));
        card.CornerRadius(CornerRadius{8});
        card.Child(content);
        return card;
    }
    Button AccentButton(hstring const& caption){
        auto button=MakeButton(caption);
        try{
            auto style=Application::Current().Resources().Lookup(box_value(L"AccentButtonStyle"));
            if(style)button.Style(style.as<Style>());
        }catch(...){}
        return button;
    }
    Border SettingsRow(hstring const& header,hstring const& description,
                       wchar_t const* glyph,std::function<void()> action){
        auto btn=MakeButton(L"");
        btn.HorizontalAlignment(HorizontalAlignment::Stretch);
        btn.HorizontalContentAlignment(HorizontalAlignment::Stretch);
        btn.BorderThickness(Thickness{0});
        btn.Background(SolidColorBrush(Windows::UI::Colors::Transparent()));
        btn.Padding(Thickness{18,17,18,17});
        Grid row;
        row.ColumnSpacing(17);
        ColumnDefinition icon;icon.Width(GridLength{32,GridUnitType::Pixel});
        ColumnDefinition content;content.Width(GridLength{1,GridUnitType::Star});
        ColumnDefinition tail;tail.Width(GridLength{24,GridUnitType::Pixel});
        row.ColumnDefinitions().Append(icon);
        row.ColumnDefinitions().Append(content);
        row.ColumnDefinitions().Append(tail);
        auto ico=MakeIcon(glyph,22);
        ico.VerticalAlignment(VerticalAlignment::Center);
        row.Children().Append(ico);
        StackPanel desc;
        desc.Spacing(5);
        auto title=MakeText(header,13);
        title.FontWeight(Windows::UI::Text::FontWeights::SemiBold());
        desc.Children().Append(title);
        auto detail=MakeText(description,12);
        detail.Opacity(0.72);
        desc.Children().Append(detail);
        Grid::SetColumn(desc,1);
        row.Children().Append(desc);
        auto arrow=MakeIcon(L"\uE76C",14);
        arrow.VerticalAlignment(VerticalAlignment::Center);
        Grid::SetColumn(arrow,2);
        row.Children().Append(arrow);
        btn.Content(row);
        btn.Click([action](auto const&,auto const&){action();});
        return SettingsCard(btn);
    }

    ScrollViewer BuildUpdate() {
        StackPanel page;
        page.Children().Append(Title(L"日本語訳アップデート"));

        Grid overview;
        overview.MinHeight(122);
        overview.ColumnSpacing(19);
        ColumnDefinition ico;ico.Width(GridLength{96,GridUnitType::Pixel});
        ColumnDefinition center;center.Width(GridLength{1,GridUnitType::Star});
        ColumnDefinition right;right.Width(GridLength{0,GridUnitType::Auto});
        overview.ColumnDefinitions().Append(ico);
        overview.ColumnDefinitions().Append(center);
        overview.ColumnDefinitions().Append(right);
        m_statusIcon=MakeIcon(L"\uE895",74);
        m_statusIcon.HorizontalAlignment(HorizontalAlignment::Center);
        m_statusIcon.VerticalAlignment(VerticalAlignment::Center);
        m_statusIcon.Foreground(ThemeBrush(L"AccentTextFillColorPrimaryBrush"));
        overview.Children().Append(m_statusIcon);
        StackPanel status;
        status.Spacing(7);
        status.VerticalAlignment(VerticalAlignment::Center);
        m_status=MakeText(L"更新プログラムを確認しています",20);
        m_status.FontWeight(Windows::UI::Text::FontWeights::SemiBold());
        status.Children().Append(m_status);
        m_subtitle=MakeText(L"GitHub Releasesから更新情報を取得しています",13);
        m_subtitle.Opacity(0.75);
        status.Children().Append(m_subtitle);
        m_lastChecked=MakeText(L"最終チェック: 未実施",11.5);
        m_lastChecked.Opacity(0.65);
        status.Children().Append(m_lastChecked);
        m_releaseLink=HyperlinkButton();
        m_releaseLink.Content(box_value(L"この更新プログラムの詳細を確認"));
        m_releaseLink.Padding(Thickness{0});
        m_releaseLink.HorizontalAlignment(HorizontalAlignment::Left);
        m_releaseLink.Visibility(Visibility::Collapsed);
        m_releaseLink.Click([this](auto const&,auto const&){
            m_nav.SelectedIndex(1);OpenLatestHistory();
        });
        status.Children().Append(m_releaseLink);
        Grid::SetColumn(status,1);
        overview.Children().Append(status);
        m_checkButton=AccentButton(L"更新プログラムのチェック");
        m_checkButton.VerticalAlignment(VerticalAlignment::Center);
        m_checkButton.Click([this](auto const&,auto const&){LoadLatest();LoadHistory();});
        Grid::SetColumn(m_checkButton,2);
        overview.Children().Append(m_checkButton);
        page.Children().Append(overview);

        // Available updates match the Windows Settings card/row hierarchy.
        m_installArea=StackPanel();
        m_installArea.Spacing(0);
        StackPanel header;
        header.Padding(Thickness{18,17,18,15});
        auto heading=MakeText(L"利用可能な更新プログラム",14);
        heading.FontWeight(Windows::UI::Text::FontWeights::SemiBold());
        header.Children().Append(heading);
        m_installArea.Children().Append(header);
        Grid available;
        available.Padding(Thickness{18,15,18,18});
        available.ColumnSpacing(13);
        ColumnDefinition a1;a1.Width(GridLength{30,GridUnitType::Pixel});
        ColumnDefinition a2;a2.Width(GridLength{1,GridUnitType::Star});
        ColumnDefinition a3;a3.Width(GridLength{0,GridUnitType::Auto});
        available.ColumnDefinitions().Append(a1);
        available.ColumnDefinitions().Append(a2);
        available.ColumnDefinitions().Append(a3);
        auto updateIcon=MakeIcon(L"\uE946",22);
        updateIcon.VerticalAlignment(VerticalAlignment::Center);
        available.Children().Append(updateIcon);
        StackPanel availableText;
        availableText.Spacing(7);
        m_availableVersion=MakeText(L"SCP: SL 日本語翻訳",13);
        m_availableVersion.FontWeight(Windows::UI::Text::FontWeights::SemiBold());
        availableText.Children().Append(m_availableVersion);
        auto more=HyperlinkButton();
        more.Content(box_value(L"この更新プログラムの詳細を確認"));
        more.Padding(Thickness{0});
        more.HorizontalAlignment(HorizontalAlignment::Left);
        more.Click([this](auto const&,auto const&){m_nav.SelectedIndex(1);OpenLatestHistory();});
        availableText.Children().Append(more);
        Grid::SetColumn(availableText,1);
        available.Children().Append(availableText);
        m_installButton=AccentButton(L"ダウンロードしてインストール");
        m_installButton.VerticalAlignment(VerticalAlignment::Center);
        m_installButton.Click([this](auto const&,auto const&){InstallTranslation();});
        Grid::SetColumn(m_installButton,2);
        available.Children().Append(m_installButton);
        m_installArea.Children().Append(available);
        m_availableCard=SettingsCard(m_installArea);
        m_availableCard.Visibility(Visibility::Collapsed);
        page.Children().Append(m_availableCard);

        m_progress=ProgressBar();
        m_progress.Minimum(0);m_progress.Maximum(100);
        m_progress.Visibility(Visibility::Collapsed);
        page.Children().Append(m_progress);
        m_progressText=MakeText(L"",12);
        page.Children().Append(m_progressText);

        auto moreTitle=MakeText(L"その他のオプション",17);
        moreTitle.FontWeight(Windows::UI::Text::FontWeights::SemiBold());
        moreTitle.Margin(Thickness{0,14,0,1});
        page.Children().Append(moreTitle);
        page.Children().Append(SettingsRow(L"ゲームのインストール先",
            L"Steamライブラリからゲームの保存先を選択",L"\uE8B7",
            [this](){m_nav.SelectedIndex(2);}));
        page.Children().Append(SettingsRow(L"更新履歴",
            L"これまでに公開された日本語翻訳の変更内容",L"\uE81C",
            [this](){m_nav.SelectedIndex(1);}));
        m_gameText=MakeText(L"Steamのゲームを検出しています",11.5);
        m_gameText.Opacity(0.70);
        page.Children().Append(m_gameText);
        m_installed=MakeText(L"インストール済み: 確認中",11.5);
        m_installed.Opacity(0.70);
        page.Children().Append(m_installed);

        m_ecoRow=StackPanel();
        m_ecoRow.Orientation(Orientation::Horizontal);
        m_ecoRow.Spacing(7);
        m_ecoRow.Margin(Thickness{0,20,0,5});
        m_ecoRow.Visibility(Visibility::Collapsed);
        auto leaf=MakeIcon(L"\uE8BE",16);
        leaf.VerticalAlignment(VerticalAlignment::Center);
        leaf.Foreground(SolidColorBrush(Windows::UI::Color{255,43,161,73}));
        m_ecoRow.Children().Append(leaf);
        auto carbon=MakeText(
            L"SCPSL 日本語化インストーラーは二酸化炭素排出量の削減に取り組んでいます",12);
        carbon.TextWrapping(TextWrapping::NoWrap);
        carbon.VerticalAlignment(VerticalAlignment::Center);
        m_ecoRow.Children().Append(carbon);
        auto carbonLink=HyperlinkButton();
        carbonLink.Content(box_value(L"詳細情報"));
        carbonLink.Padding(Thickness{2,0,0,0});
        carbonLink.Click([](auto const&,auto const&){
            OpenUri(L"https://support.microsoft.com/ja-jp/windows/deployment/updates-lifecycle/windows-update-is-now-carbon-aware");
        });
        m_ecoRow.Children().Append(carbonLink);
        page.Children().Append(m_ecoRow);
        return MakePage(page);
    }

    ScrollViewer BuildHistory(){
        StackPanel page;
        page.Children().Append(Title(L"更新履歴"));
        auto desc=MakeText(L"GitHub Releasesに公開された日本語訳の更新内容を表示します",12);
        desc.Opacity(0.75);
        page.Children().Append(desc);
        Grid tools;
        ColumnDefinition left;left.Width(GridLength{1,GridUnitType::Star});
        ColumnDefinition right;right.Width(GridLength{0,GridUnitType::Auto});
        tools.ColumnDefinitions().Append(left);
        tools.ColumnDefinitions().Append(right);
        m_historyStatus=MakeText(L"更新履歴を読み込んでいます",12);
        m_historyStatus.Opacity(0.73);
        m_historyStatus.VerticalAlignment(VerticalAlignment::Center);
        tools.Children().Append(m_historyStatus);
        auto refresh=MakeButton(L"更新履歴を再取得");
        refresh.Click([this](auto const&,auto const&){LoadHistory();});
        Grid::SetColumn(refresh,1);
        tools.Children().Append(refresh);
        page.Children().Append(tools);
        m_historyList=StackPanel();
        m_historyList.Spacing(8);
        page.Children().Append(m_historyList);
        return MakePage(page);
    }

    ScrollViewer BuildFolders(){
        StackPanel page;
        page.Children().Append(Title(L"インストール先"));
        auto desc=MakeText(L"SCP: Secret Laboratoryの保存先を選択してください",12);
        desc.Opacity(0.75);
        page.Children().Append(desc);
        StackPanel card;
        card.Spacing(0);
        auto heading=MakeText(L"検出されたゲーム",15);
        heading.FontWeight(Windows::UI::Text::FontWeights::SemiBold());
        heading.Margin(Thickness{18,18,18,12});
        card.Children().Append(heading);
        m_folderList=ListView();
        m_folderList.MinHeight(75);
        m_folderList.MaxHeight(385);
        m_folderList.SelectionMode(ListViewSelectionMode::Single);
        m_folderList.BorderThickness(Thickness{0});
        m_folderList.Background(SolidColorBrush(Windows::UI::Colors::Transparent()));
        m_folderList.SelectionChanged([this](auto const&,auto const&){
            int index=m_folderList.SelectedIndex();
            if(index>=0 && index<static_cast<int>(m_detectedPaths.size())){
                auto selected=m_detectedPaths[static_cast<size_t>(index)];
                if(m_gamePath!=selected){
                    m_gamePath=selected;
                    RefreshInstalled();UpdatePresentation();
                }
            }
        });
        card.Children().Append(m_folderList);
        StackPanel actions;
        actions.Orientation(Orientation::Horizontal);
        actions.HorizontalAlignment(HorizontalAlignment::Right);
        actions.Margin(Thickness{12,16,16,18});
        actions.Spacing(9);
        auto rescan=MakeButton(L"再スキャン");
        rescan.Click([this](auto const&,auto const&){LoadGames();});
        actions.Children().Append(rescan);
        auto browse=MakeButton(L"フォルダーを参照...");
        browse.Click([this](auto const&,auto const&){BrowseFolder();});
        actions.Children().Append(browse);
        card.Children().Append(actions);
        page.Children().Append(SettingsCard(card));
        return MakePage(page);
    }

    ScrollViewer BuildAbout(){
        StackPanel page;
        page.Children().Append(Title(L"このアプリについて"));
        StackPanel card;
        card.Padding(Thickness{22});
        card.Spacing(14);
        auto title=MakeText(L"SCPSL 日本語化インストーラー",18);
        title.FontWeight(Windows::UI::Text::FontWeights::SemiBold());
        card.Children().Append(title);
        card.Children().Append(MakeText(
            L"Steamのゲームを検出し、翻訳プロジェクトのGitHub Releasesから日本語訳を取得します",13));
        auto link=HyperlinkButton();
        link.Content(box_value(L"GitHubリポジトリを開く"));
        link.HorizontalAlignment(HorizontalAlignment::Left);
        link.Padding(Thickness{0});
        link.Click([](auto const&,auto const&){
            OpenUri(L"https://github.com/hayabusa255/SCPSLTranslationJP");
        });
        card.Children().Append(link);
        page.Children().Append(SettingsCard(card));
        return MakePage(page);
    }

    void SwitchPage(int page) {
        if(m_updatePage) m_updatePage.Visibility(page==0?Visibility::Visible:Visibility::Collapsed);
        if(m_historyPage) m_historyPage.Visibility(page==1?Visibility::Visible:Visibility::Collapsed);
        if(m_folderPage) m_folderPage.Visibility(page==2?Visibility::Visible:Visibility::Collapsed);
        if(m_aboutPage) m_aboutPage.Visibility(page==3?Visibility::Visible:Visibility::Collapsed);
    }

    void RunEngine(std::wstring command,std::wstring argument,std::function<void(EngineResponse const&)> completed) {
        auto queue=m_window.DispatcherQueue();
        std::thread([this,command=std::move(command),argument=std::move(argument),
                     completed=std::move(completed),queue]() {
            auto response=ExecuteEngine(command,argument,[this,queue](std::string const& line) {
                queue.TryEnqueue([this,line]() {
                    try{
                        auto json=JsonObject::Parse(to_hstring(line));
                        m_progress.Value(json.GetNamedNumber(L"percent",0));
                        m_progressText.Text(json.GetNamedString(L"stage",L""));
                    }catch(...) {}
                });
            });
            queue.TryEnqueue([this,completed,response=std::move(response)]() {
                completed(response);
            });
        }).detach();
    }

    std::optional<IJsonValue> ReadData(EngineResponse const& response) {
        if(!response.error.empty()){m_subtitle.Text(response.error);return std::nullopt;}
        try{
            std::wstring input=ToWide(response.output);
            size_t start=0;
            while(start<input.size()){
                auto end=input.find(L'\n',start);
                if(end==std::wstring::npos)end=input.size();
                auto line=input.substr(start,end-start);
                if(!line.empty()){
                    try {
                        auto json=JsonObject::Parse(line);
                        auto kind=json.GetNamedString(L"kind",L"");
                        if(kind==L"result") return json.GetNamedValue(L"data");
                        if(kind==L"error")m_subtitle.Text(json.GetNamedString(L"message",L"実行に失敗しました"));
                    }catch(...) {}
                }
                start=end+1;
            }
        }catch(...){}
        return std::nullopt;
    }

    void UpdatePresentation(){
        bool current=!m_latestTag.empty() &&
            std::find(m_versions.begin(),m_versions.end(),m_latestTag)!=m_versions.end();
        if(!m_status)return;
        if(m_latestTag.empty()){
            m_status.Text(L"更新プログラムを確認できませんでした");
            m_subtitle.Text(L"GitHubへの接続を確認してもう一度お試しください");
        }else if(m_gamePath.empty()){
            m_status.Text(L"インストール先を選択してください");
            m_subtitle.Text(L"SCP: Secret Laboratoryの保存先を指定してください");
        }else if(current){
            m_status.Text(L"最新の状態です");
            m_subtitle.Text(L"日本語翻訳は最新の状態です");
        }else {
            m_status.Text(L"更新プログラムを利用できます");
            m_subtitle.Text(L"新しい日本語翻訳をダウンロードできます");
        }
        if(m_gameText) {
            std::wstring shown=m_gamePath;
            if(shown.rfind(L"\\\\?\\",0)==0)shown=shown.substr(4);
            m_gameText.Text(shown.empty()?L"ゲームの保存先が見つかりません":hstring(shown));
        }
        std::wstring installed=L"インストール済み: ";
        if(m_versions.empty())installed+=L"未インストール";
        else for(auto const& v:m_versions)installed+=v+L" ";
        m_installed.Text(installed);
        if(m_availableVersion)m_availableVersion.Text(
            hstring(L"SCP: SL 日本語翻訳 "+m_latestTag));
        bool available=!m_gamePath.empty() && !current && !m_latestTag.empty();
        m_ecoRow.Visibility(current?Visibility::Visible:Visibility::Collapsed);
        m_availableCard.Visibility(available?Visibility::Visible:Visibility::Collapsed);
        m_releaseLink.Visibility(m_latestTag.empty()?Visibility::Collapsed:Visibility::Visible);
        m_checkButton.IsEnabled(!m_busy);
    }

    void LoadLatest(){
        if(m_busy) return;
        m_status.Text(L"更新プログラムを確認しています");
        RunEngine(L"latest",L"",[this](auto const& r) {
            if(auto v=ReadData(r)){
                try{
                    auto tag=v->GetObject().GetNamedString(L"tag",L"");
                    m_latestTag=tag.c_str();
                    SYSTEMTIME st{};GetLocalTime(&st);
                    wchar_t time[32]{};swprintf_s(time,L"最終チェック: %02d:%02d",st.wHour,st.wMinute);
                    m_lastChecked.Text(time);
                }catch(...) {}
            }
            if(m_latestTag.empty()){m_status.Text(L"更新プログラムを確認できませんでした");return;}
            RefreshInstalled();
            UpdatePresentation();
        });
    }

    void RefreshInstalled() {
        if(m_gamePath.empty()){m_versions.clear();UpdatePresentation();return;}
        RunEngine(L"inspect",m_gamePath,[this](auto const& r){
            m_versions.clear();
            if(auto v=ReadData(r)){
                try{
                    auto a=v->GetObject().GetNamedArray(L"installedVersions");
                    for(auto const& value:a) m_versions.push_back(value.GetString().c_str());
                }catch(...) {}
            }
            UpdatePresentation();
        });
    }

    void LoadGames(){
        RunEngine(L"detect",L"",[this](auto const& response){
            m_folderList.Items().Clear();
            m_detectedPaths.clear();
            if(auto value=ReadData(response)){
                try {
                    auto entries=value->GetArray();
                    for(auto const& entry:entries){
                        auto object=entry.GetObject();
                        auto path=object.GetNamedString(L"path",L"");
                        auto source=object.GetNamedString(L"source",L"Steam");
                        if(path.empty())continue;
                        std::wstring real=path.c_str();
                        m_detectedPaths.push_back(real);
                        StackPanel lines;
                        lines.Spacing(5);
                        auto origin=MakeText(source,13);
                        origin.FontWeight(Windows::UI::Text::FontWeights::SemiBold());
                        lines.Children().Append(origin);
                        std::wstring friendly=real;
                        if(friendly.rfind(L"\\\\?\\",0)==0)friendly=friendly.substr(4);
                        auto folder=MakeText(hstring(friendly),11.5);
                        folder.Opacity(0.7);
                        lines.Children().Append(folder);
                        ListViewItem item;
                        item.MinHeight(74);
                        item.Padding(Thickness{18,13,18,13});
                        item.HorizontalContentAlignment(HorizontalAlignment::Stretch);
                        item.Content(lines);
                        m_folderList.Items().Append(item);
                        if(m_gamePath.empty())m_gamePath=real;
                    }
                }catch(...) {}
            }
            if(m_detectedPaths.empty()) {
                auto empty=ListViewItem();
                empty.Content(MakeText(L"SteamからSCP: Secret Laboratoryを検出できませんでした",12));
                empty.IsEnabled(false);
                m_folderList.Items().Append(empty);
            }else {
                auto it=std::find(m_detectedPaths.begin(),m_detectedPaths.end(),m_gamePath);
                int index=it==m_detectedPaths.end()?0:static_cast<int>(it-m_detectedPaths.begin());
                m_folderList.SelectedIndex(index);
            }
            UpdatePresentation();
            RefreshInstalled();
        });
    }

    void LoadHistory(){
        if(m_historyStatus)m_historyStatus.Text(L"GitHubから更新履歴を取得しています");
        RunEngine(L"history",L"",[this](auto const& response){
            m_historyList.Children().Clear();
            m_releaseExpanders.clear();
            size_t count=0;
            if(auto result=ReadData(response)){
                try{
                    auto entries=result->GetArray();
                    for(auto const& entry:entries){
                        auto object=entry.GetObject();
                        auto tag=object.GetNamedString(L"tag",L"");
                        auto title=object.GetNamedString(L"title",L"");
                        auto summary=object.GetNamedString(L"summary",L"");
                        auto notes=object.GetNamedString(L"notes",L"");
                        auto published=object.GetNamedString(L"publishedAt",L"");
                        auto ex=Expander();
                        ex.HorizontalAlignment(HorizontalAlignment::Stretch);
                        ex.BorderThickness(Thickness{1});
                        ex.BorderBrush(ThemeBrush(L"CardStrokeColorDefaultBrush"));
                        ex.Background(ThemeBrush(L"CardBackgroundFillColorDefaultBrush"));
                        StackPanel header;
                        header.Spacing(6);
                        auto name=MakeText(title.empty()?tag:title,14);
                        name.FontWeight(Windows::UI::Text::FontWeights::SemiBold());
                        header.Children().Append(name);
                        auto digest=MakeText(summary,12);
                        digest.Opacity(0.78);
                        header.Children().Append(digest);
                        std::wstring date=published.c_str();
                        if(date.size()>=10)
                            date=date.substr(0,4)+L"年"+date.substr(5,2)+L"月"+date.substr(8,2)+L"日";
                        auto meta=MakeText(hstring(std::wstring(tag.c_str())+L"  ·  "+date),11);
                        meta.Opacity(0.65);
                        header.Children().Append(meta);
                        ex.Header(header);
                        StackPanel details;
                        details.Spacing(13);
                        details.Padding(Thickness{20,16,20,23});
                        auto label=MakeText(L"この更新プログラムの内容",14);
                        label.FontWeight(Windows::UI::Text::FontWeights::SemiBold());
                        details.Children().Append(label);
                        details.Children().Append(scpsl::RenderMarkdown(notes.c_str()));
                        auto external=HyperlinkButton();
                        external.Content(box_value(L"GitHubでこのリリースを表示"));
                        external.Padding(Thickness{0});
                        external.HorizontalAlignment(HorizontalAlignment::Left);
                        std::wstring url=L"https://github.com/hayabusa255/SCPSLTranslationJP/releases/tag/";
                        url+=tag.c_str();
                        external.Click([url](auto const&,auto const&){OpenUri(url.c_str());});
                        details.Children().Append(external);
                        ex.Content(details);
                        m_historyList.Children().Append(ex);
                        m_releaseExpanders.emplace_back(std::wstring(tag.c_str()),ex);
                        count++;
                    }
                }catch(...) {}
            }
            if(count==0) {
                m_historyList.Children().Append(MakeText(L"更新履歴を取得できませんでした",13));
                if(m_historyStatus)m_historyStatus.Text(L"通信状態をご確認ください");
            }else {
                if(m_historyStatus)m_historyStatus.Text(hstring(
                    L"GitHubから"+std::to_wstring(count)+L"件の更新履歴を取得しました"));
            }
            if(m_expandLatestWhenLoaded) {
                m_expandLatestWhenLoaded=false;
                OpenLatestHistory();
            }
        });
    }

    void OpenLatestHistory(){
        m_nav.SelectedIndex(1);
        for(auto const& entry:m_releaseExpanders){
            if(entry.first==m_latestTag) {
                entry.second.IsExpanded(true);
                return;
            }
        }
        m_expandLatestWhenLoaded=true;
    }

    void BrowseFolder() {
        IFileOpenDialog* dialog=nullptr;
        auto hr=CoCreateInstance(CLSID_FileOpenDialog,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&dialog));
        if(FAILED(hr)||!dialog)return;
        DWORD options{};
        dialog->GetOptions(&options);
        dialog->SetOptions(options|FOS_PICKFOLDERS);
        if(SUCCEEDED(dialog->Show(GetActiveWindow()))){
            IShellItem* selected=nullptr;
            if(SUCCEEDED(dialog->GetResult(&selected))&&selected){
                PWSTR rawPath=nullptr;
                if(SUCCEEDED(selected->GetDisplayName(SIGDN_FILESYSPATH,&rawPath))&&rawPath){
                    std::wstring folder(rawPath);
                    CoTaskMemFree(rawPath);
                    RunEngine(L"validate",folder,[this,folder](auto const& r){
                        if(auto data=ReadData(r)){
                            m_gamePath=folder;
                            if(std::find(m_detectedPaths.begin(),m_detectedPaths.end(),folder)
                               ==m_detectedPaths.end()) {
                                m_detectedPaths.push_back(folder);
                                auto item=ListViewItem();
                                item.Content(MakeText(hstring(folder),12));
                                item.MinHeight(62);
                                m_folderList.Items().Append(item);
                            }
                            auto it=std::find(m_detectedPaths.begin(),m_detectedPaths.end(),folder);
                            m_folderList.SelectedIndex(static_cast<int>(it-m_detectedPaths.begin()));
                            RefreshInstalled();
                            UpdatePresentation();
                        }
                    });
                }
                selected->Release();
            }
        }
        dialog->Release();
    }

    void InstallTranslation() {
        if(m_gamePath.empty()||m_latestTag.empty()||m_busy)return;
        m_busy=true;
        m_installButton.IsEnabled(false);
        m_progress.Visibility(Visibility::Visible);
        m_progress.Value(0);
        m_progressText.Text(L"準備しています");
        RunEngine(L"install",m_gamePath,[this](auto const& r){
            m_busy=false;
            m_installButton.IsEnabled(true);
            if(auto data=ReadData(r)){
                m_progress.Value(100);
                m_progressText.Text(L"インストールが完了しました");
                RefreshInstalled();
            }else {
                m_progressText.Text(L"インストールに失敗しました");
            }
        });
    }
};

int WINAPI wWinMain(HINSTANCE,HINSTANCE,LPWSTR,int){
    StartupTrace("wWinMain-enter");
    try {
        init_apartment(apartment_type::single_threaded);
        StartupTrace("Apartment-initialized");
        StartupTrace("Bootstrap-enter");
        HRESULT hr=E_FAIL;
        std::wstring detail;
        if(!InitializeBundledRuntime(hr,detail)){
            StartupTrace("Bootstrap-failed");
            MessageBoxW(nullptr,detail.c_str(),
                L"SCP: SL 日本語化インストーラー",MB_ICONERROR);
            ShutdownBundledRuntime();
            return static_cast<int>(hr);
        }
        StartupTrace("Bootstrap-returned");
        StartupTrace("Application-start-enter");
        Application::Start([](auto&&){make<App>();});
        StartupTrace("Application-start-returned");
        ShutdownBundledRuntime();
    }catch(hresult_error const& ex){
        StartupTrace("HRESULT-exception");
        ShutdownBundledRuntime();
        MessageBoxW(nullptr,ex.message().c_str(),L"WinUI 3の起動に失敗しました",MB_ICONERROR);
        return static_cast<int>(ex.code());
    }catch(std::exception const&){
        StartupTrace("STD-exception");
        ShutdownBundledRuntime();
        MessageBoxW(nullptr,L"予期しないエラー",L"WinUI 3",MB_ICONERROR);
        return 1;
    }
    return 0;
}