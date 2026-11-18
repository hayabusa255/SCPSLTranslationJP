#include "resource_loader.h"
#include <string>
#include <cstdio>
#include <cwchar>
#include <cstdint>
namespace {
constexpr int kRust=102, kResearcher=103, kEmblem=104;
std::wstring s_directory;
HMODULE s_runtime=nullptr;
bool s_initialized=false;
bool ExtractResource(int id, wchar_t const* name,std::wstring& error){
    auto self=GetModuleHandleW(nullptr);
    HRSRC res=FindResourceW(self,MAKEINTRESOURCEW(id),RT_RCDATA);
    if(!res){error=L"埋め込みファイルがありません";return false;}
    auto data=LockResource(LoadResource(self,res));
    DWORD size=SizeofResource(self,res);
    if(!data || size==0 || size>40*1024*1024){error=L"不正な埋め込みファイル";return false;}
    auto target=s_directory+L"\\"+name;
    auto pending=target+L".tmp";
    HANDLE out=CreateFileW(pending.c_str(),GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,
                           FILE_ATTRIBUTE_NORMAL,nullptr);
    if(out==INVALID_HANDLE_VALUE){error=L"書込先が使えません";return false;}
    DWORD actual=0;
    bool ok=WriteFile(out,data,size,&actual,nullptr)&&actual==size;
    CloseHandle(out);
    if(!ok || !MoveFileExW(pending.c_str(),target.c_str(),MOVEFILE_REPLACE_EXISTING)) {
        DeleteFileW(pending.c_str());error=L"埋め込み展開に失敗";return false;
    }
    return true;
}
}
std::wstring BundledPath(wchar_t const* name){return s_directory+L"\\"+name;}
std::wstring BundledDirectory(){return s_directory;}
bool InitializeBundledRuntime(HRESULT& hr,std::wstring& detail){
    hr=E_FAIL;
    detail.clear();
    wchar_t dir[32768]{};
    if(!GetTempPathW(32768,dir)){detail=L"Tempが取得できません";return false;}
    std::wstring top=std::wstring(dir)+L"SCPSLJP-Native";
    CreateDirectoryW(top.c_str(),nullptr);
    wchar_t tail[100]{};
    swprintf_s(tail,L"session-%lu-%llu",GetCurrentProcessId(),GetTickCount64());
    s_directory=top+L"\\"+tail;
    if(!CreateDirectoryW(s_directory.c_str(),nullptr)){detail=L"Tempが使えません";return false;}
    if(!ExtractResource(kRust,L"scpsl-native-core.exe",detail)||
       !ExtractResource(kResearcher,L"researcher.jpg",detail)||
       !ExtractResource(kEmblem,L"scp-emblem.png",detail))return false;
    wchar_t exe[32768]{};
    DWORD n=GetModuleFileNameW(nullptr,exe,32768);
    if(!n || n>=32768){detail=L"EXEのパスが不正です";return false;}
    std::wstring local(exe);
    auto slash=local.find_last_of(L"\\/");
    if(slash==std::wstring::npos){detail=L"実行先ディレクトリがありません";return false;}
    local=local.substr(0,slash);
    SetEnvironmentVariableW(L"MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY",local.c_str());
    local+=L"\\Microsoft.WindowsAppRuntime.dll";
    s_runtime=LoadLibraryExW(local.c_str(),nullptr,
                   LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    if(!s_runtime){hr=HRESULT_FROM_WIN32(GetLastError());detail=L"ローカルWinApp SDK DLLを読み込めません";return false;}
    using Ensure=HRESULT(WINAPI*)();
    auto ensure=reinterpret_cast<Ensure>(GetProcAddress(s_runtime,"WindowsAppRuntime_EnsureIsLoaded"));
    if(!ensure){detail=L"WinApp SDKの自己完結APIが見つかりません";return false;}
    hr=ensure();
    if(FAILED(hr)){detail=L"WinApp SDKのローカル初期化に失敗";return false;}
    s_initialized=true;return true;
}
void ShutdownBundledRuntime() noexcept{
    s_initialized=false;
    if(s_runtime){FreeLibrary(s_runtime);s_runtime=nullptr;}
    for(auto f:{L"scpsl-native-core.exe",L"researcher.jpg",L"scp-emblem.png"}){
       DeleteFileW(BundledPath(f).c_str());
    }
    if(!s_directory.empty()){RemoveDirectoryW(s_directory.c_str());s_directory.clear();}
}
