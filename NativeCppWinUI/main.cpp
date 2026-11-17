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
#include <winrt/Microsoft.UI.Xaml.Controls.h>
#include <winrt/Microsoft.UI.Xaml.Controls.Primitives.h>
#include <winrt/Microsoft.UI.Xaml.Media.h>
#include <winrt/Microsoft.UI.Xaml.Media.Imaging.h>
#include <winrt/Microsoft.UI.Xaml.Shapes.h>
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
    Microsoft::UI::Xaml::Markup::IXamlType GetXamlType(Windows::UI::Xaml::Interop::TypeName const&) {return nullptr;}
    Microsoft::UI::Xaml::Markup::IXamlType GetXamlType(hstring const&) {return nullptr;}
    com_array<Microsoft::UI::Xaml::Markup::XmlnsDefinition> GetXmlnsDefinitions(){return {};}

    Window m_window{nullptr};
    NavigationView m_nav{nullptr};
    NavigationViewItem m_updateItem{nullptr},m_historyItem{nullptr},m_folderItem{nullptr},m_aboutItem{nullptr};
    ScrollViewer m_updatePage{nullptr},m_historyPage{nullptr},m_folderPage{nullptr},m_aboutPage{nullptr};
    StackPanel m_historyList{nullptr},m_folderList{nullptr},m_installArea{nullptr},m_ecoRow{nullptr};
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
        StartupTrace("Window-create-enter");
        m_window=Window();
        StartupTrace("Window-create-ok");
        m_window.Title(L"SCP: SL 日本語化インストーラー");
        m_window.SystemBackdrop(MicaBackdrop());
        m_nav=NavigationView();
        m_nav.PaneDisplayMode(NavigationViewPaneDisplayMode::Left);
        m_nav.IsPaneOpen(true);
        m_nav.IsPaneToggleButtonVisible(false);
        m_nav.IsSettingsVisible(false);
        m_nav.OpenPaneLength(285);
        m_nav.CompactPaneLength(55);
        m_nav.PaneHeader(BuildProfile());
        m_updateItem=MakeNavItem(L"日本語訳アップデート",L"\uE895");
        m_historyItem=MakeNavItem(L"更新履歴",L"\uE81C");
        m_folderItem=MakeNavItem(L"インストール先",L"\uE8B7");
        m_aboutItem=MakeNavItem(L"このアプリについて",L"\uE946");
        m_nav.MenuItems().Append(m_updateItem);
        m_nav.MenuItems().Append(m_historyItem);
        m_nav.MenuItems().Append(m_folderItem);
        m_nav.MenuItems().Append(m_aboutItem);
        m_nav.Content(BuildContent());
        m_nav.SelectionChanged([this](auto const&,NavigationViewSelectionChangedEventArgs const& args) {
            auto selection=args.SelectedItem();
            if(selection == m_updateItem) SwitchPage(0);
            else if(selection == m_historyItem) SwitchPage(1);
            else if(selection == m_folderItem) SwitchPage(2);
            else if(selection == m_aboutItem) SwitchPage(3);
        });
        m_window.Content(m_nav);
        m_nav.SelectedItem(m_updateItem);
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

    NavigationViewItem MakeNavItem(hstring const& label,wchar_t const* glyph) {
        NavigationViewItem item;
        item.Content(box_value(label));
        item.Icon(MakeIcon(glyph));
        return item;
    }

    StackPanel BuildProfile(){
        StackPanel wrapper;
        wrapper.Spacing(9);
        wrapper.Padding(Thickness{4,22,8,20});
        StackPanel row;
        row.Orientation(Orientation::Horizontal);
        row.Spacing(12);
        Microsoft::UI::Xaml::Shapes::Ellipse avatar;
        avatar.Width(62);
        avatar.Height(62);
        try {
            auto imagePath=BundledPath(L"researcher.jpg");
            std::replace(imagePath.begin(),imagePath.end(),L'\\',L'/');
            auto brush=ImageBrush();
            brush.ImageSource(BitmapImage(Uri(L"file:///"+imagePath)));
            brush.Stretch(Stretch::UniformToFill);
            avatar.Fill(brush);
        } catch(...) {}
        row.Children().Append(avatar);
        StackPanel account;
        account.Spacing(4);
        account.VerticalAlignment(VerticalAlignment::Center);
        account.Children().Append(MakeText(L"SCP Foundation",14));
        auto email=MakeText(L"researcher@scp-foundation.jp",11);
        account.Children().Append(email);
        row.Children().Append(account);
        wrapper.Children().Append(row);
        StackPanel managed;
        managed.Orientation(Orientation::Horizontal);
        managed.Spacing(6);
        managed.Children().Append(MakeIcon(L"\uE80F",13));
        managed.Children().Append(MakeText(L"組織によって管理されています",11));
        wrapper.Children().Append(managed);
        return wrapper;
    }

    ScrollViewer MakePage(StackPanel content) {
        content.Padding(Thickness{30,30,36,50});
        content.Spacing(19);
        ScrollViewer scroll;
        scroll.Content(content);
        return scroll;
    }

    TextBlock Title(hstring const& label) {
        auto t=MakeText(label,30);
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

    ScrollViewer BuildUpdate() {
        StackPanel page;
        page.Children().Append(Title(L"日本語訳アップデート"));
        StackPanel statusRow;
        statusRow.Orientation(Orientation::Horizontal);
        statusRow.Spacing(20);
        statusRow.VerticalAlignment(VerticalAlignment::Center);
        statusRow.Children().Append(MakeIcon(L"\uE895",68));
        StackPanel middle;
        middle.Spacing(8);
        middle.VerticalAlignment(VerticalAlignment::Center);
        m_status=MakeText(L"更新プログラムを確認しています",20);
        m_subtitle=MakeText(L"GitHub Releasesから更新情報を取得しています",13);
        m_lastChecked=MakeText(L"最終チェック: 未実施",12);
        middle.Children().Append(m_status);
        middle.Children().Append(m_subtitle);
        middle.Children().Append(m_lastChecked);
        m_releaseLink=HyperlinkButton();
        m_releaseLink.Content(box_value(L"この更新プログラムの内容を見る"));
        m_releaseLink.Visibility(Visibility::Collapsed);
        m_releaseLink.Click([this](auto const&,auto const&){
            m_nav.SelectedItem(m_historyItem); OpenLatestHistory();
        });
        middle.Children().Append(m_releaseLink);
        statusRow.Children().Append(middle);
        page.Children().Append(statusRow);
        m_checkButton=MakeButton(L"更新プログラムのチェック");
        m_checkButton.Click([this](auto const&,auto const&){ LoadLatest();LoadHistory(); });
        page.Children().Append(m_checkButton);
        m_ecoRow=StackPanel();
        m_ecoRow.Orientation(Orientation::Horizontal);
        m_ecoRow.Spacing(7);
        m_ecoRow.Visibility(Visibility::Collapsed);
        // Segoe Fluent Icons Leaf (U+E8BE): Windows' native icon glyph.
        m_ecoRow.Children().Append(MakeIcon(L"\uE8BE",15));
        m_ecoRow.Children().Append(MakeText(L"SCPSL 日本語化インストーラーは二酸化炭素排出量の削減に取り組んでいます",12));
        auto ecoLink=HyperlinkButton();
        ecoLink.Content(box_value(L"詳細情報"));
        ecoLink.Click([](auto const&,auto const&){
            OpenUri(L"https://support.microsoft.com/ja-jp/windows/deployment/updates-lifecycle/windows-update-is-now-carbon-aware");
        });
        m_ecoRow.Children().Append(ecoLink);
        page.Children().Append(m_ecoRow);
        m_installArea=StackPanel();
        m_installArea.Spacing(9);
        m_installArea.Visibility(Visibility::Collapsed);
        m_installArea.Children().Append(MakeText(L"利用可能な更新プログラム",16));
        m_installButton=MakeButton(L"ダウンロードしてインストール");
        m_installButton.Click([this](auto const&,auto const&){ InstallTranslation(); });
        m_installArea.Children().Append(m_installButton);
        page.Children().Append(m_installArea);
        m_progress=ProgressBar();
        m_progress.Minimum(0);
        m_progress.Maximum(100);
        m_progress.Visibility(Visibility::Collapsed);
        page.Children().Append(m_progress);
        m_progressText=MakeText(L"",12);
        page.Children().Append(m_progressText);
        page.Children().Append(MakeText(L"その他のオプション",17));
        auto location=MakeButton(L"インストール先を変更");
        location.Click([this](auto const&,auto const&){m_nav.SelectedItem(m_folderItem);});
        page.Children().Append(location);
        m_gameText=MakeText(L"Steamのゲームを検出しています",12);
        page.Children().Append(m_gameText);
        m_installed=MakeText(L"インストール済み: 確認中",12);
        page.Children().Append(m_installed);
        return MakePage(page);
    }

    ScrollViewer BuildHistory() {
        StackPanel page;
        page.Children().Append(Title(L"更新履歴"));
        page.Children().Append(MakeText(L"GitHub Releasesから最新の更新内容を取得します",12));
        auto refresh=MakeButton(L"更新履歴を再取得");
        refresh.Click([this](auto const&,auto const&){LoadHistory();});
        page.Children().Append(refresh);
        m_historyList=StackPanel();
        m_historyList.Spacing(8);
        page.Children().Append(m_historyList);
        return MakePage(page);
    }

    ScrollViewer BuildFolders() {
        StackPanel page;
        page.Children().Append(Title(L"インストール先"));
        page.Children().Append(MakeText(L"SCP: Secret Laboratory の保存先を選択してください",12));
        m_folderList=StackPanel();
        m_folderList.Spacing(7);
        page.Children().Append(m_folderList);
        auto browse=MakeButton(L"フォルダーを参照...");
        browse.Click([this](auto const&,auto const&){ BrowseFolder(); });
        page.Children().Append(browse);
        auto refresh=MakeButton(L"再スキャン");
        refresh.Click([this](auto const&,auto const&){ LoadGames(); });
        page.Children().Append(refresh);
        return MakePage(page);
    }

    ScrollViewer BuildAbout() {
        StackPanel page;
        page.Children().Append(Title(L"このアプリについて"));
        page.Children().Append(MakeText(L"SCPSL 日本語化インストーラー",17));
        page.Children().Append(MakeText(L"Steamのゲームを検出し、翻訳プロジェクトのGitHub Releasesから日本語訳を取得します",13));
        auto git=HyperlinkButton();
        git.Content(box_value(L"GitHubリポジトリを開く"));
        git.Click([](auto const&,auto const&){OpenUri(L"https://github.com/hayabusa255/SCPSLTranslationJP");});
        page.Children().Append(git);
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

    void UpdatePresentation() {
        bool current=!m_latestTag.empty() &&
            std::find(m_versions.begin(),m_versions.end(),m_latestTag)!=m_versions.end();
        if(!m_status)return;
        if(m_gamePath.empty()){
            m_status.Text(L"インストール先を選択してください");
            m_subtitle.Text(L"SCP: Secret Laboratory の保存先を指定してください");
        }else if(current){
            m_status.Text(L"最新の状態です");
            m_subtitle.Text(L"日本語翻訳は最新の状態です");
        }else if(!m_latestTag.empty()) {
            m_status.Text(L"更新プログラムを利用できます");
            m_subtitle.Text(L"日本語翻訳の新しいバージョンをダウンロードできます");
        }
        m_gameText.Text(m_gamePath.empty()?L"ゲームが見つかっていません":hstring(m_gamePath));
        std::wstring installed=L"インストール済み: ";
        if(m_versions.empty()) installed+=L"未インストール";
        else for(auto const& v:m_versions)installed+=v+L" ";
        m_installed.Text(installed);
        m_ecoRow.Visibility(current?Visibility::Visible:Visibility::Collapsed);
        m_installArea.Visibility(!m_gamePath.empty()&&!current&&!m_latestTag.empty()
            ?Visibility::Visible:Visibility::Collapsed);
        m_releaseLink.Visibility(m_latestTag.empty()?Visibility::Collapsed:Visibility::Visible);
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

    void LoadGames() {
        RunEngine(L"detect",L"",[this](auto const& r){
            m_folderList.Children().Clear();
            if(auto value=ReadData(r)){
                try{
                    auto a=value->GetArray();
                    for(auto const& item:a){
                        auto object=item.GetObject();
                        auto path=object.GetNamedString(L"path",L"");
                        auto source=object.GetNamedString(L"source",L"Steam");
                        auto button=MakeButton(source+L"    "+path);
                        button.HorizontalAlignment(HorizontalAlignment::Stretch);
                        std::wstring selected=path.c_str();
                        button.Click([this,selected](auto const&,auto const&){
                            m_gamePath=selected;RefreshInstalled();UpdatePresentation();
                        });
                        m_folderList.Children().Append(button);
                        if(m_gamePath.empty())m_gamePath=path.c_str();
                    }
                }catch(...) {}
            }
            UpdatePresentation();
            RefreshInstalled();
        });
    }

    void LoadHistory() {
        RunEngine(L"history",L"",[this](auto const& r){
            m_historyList.Children().Clear();
            if(auto v=ReadData(r)){
                try{
                    auto entries=v->GetArray();
                    for(auto const& item:entries){
                        auto x=item.GetObject();
                        auto title=x.GetNamedString(L"title",L"");
                        auto notes=x.GetNamedString(L"notes",L"");
                        auto tag=x.GetNamedString(L"tag",L"");
                        auto summary=x.GetNamedString(L"summary",L"");
                        auto ex=Expander();
                        StackPanel header;header.Spacing(4);
                        header.Children().Append(MakeText(title,15));
                        header.Children().Append(MakeText(summary,12));
                        ex.Header(header);
                        StackPanel details;details.Spacing(10);
                        details.Children().Append(MakeText(notes,12));
                        auto link=HyperlinkButton();
                        link.Content(box_value(L"GitHubでこのリリースを見る"));
                        std::wstring url=L"https://github.com/hayabusa255/SCPSLTranslationJP/releases/tag/";
                        url+=tag.c_str();
                        link.Click([url](auto const&,auto const&){OpenUri(url.c_str());});
                        details.Children().Append(link);
                        ex.Content(details);
                        m_historyList.Children().Append(ex);
                    }
                }catch(...) {}
            }
        });
    }

    void OpenLatestHistory(){
        m_nav.SelectedItem(m_historyItem);
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