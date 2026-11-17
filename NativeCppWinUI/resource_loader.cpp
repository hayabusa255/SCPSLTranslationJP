#include "resource_loader.h"
#include <winuser.h>
#include <string>
#include <vector>
#include <cwchar>
#include <cstdint>

namespace {
constexpr int kBootstrap=101;
constexpr int kRust=102;
constexpr int kResearcher=103;
constexpr int kEmblem=104;
std::wstring s_directory;
HMODULE s_bootstrap=nullptr;
using BootstrapInitialize = HRESULT (__stdcall*)(UINT32,PCWSTR,PACKAGE_VERSION,UINT32);
using BootstrapShutdown = void (__stdcall*)();
BootstrapShutdown s_shutdown=nullptr;
bool s_initialized=false;

bool ExtractResource(int id, wchar_t const* name, std::wstring& error) {
    HMODULE self=GetModuleHandleW(nullptr);
    HRSRC item=FindResourceW(self, MAKEINTRESOURCEW(id), RT_RCDATA);
    if(!item) {error=L"EXEに必要なリソースがありません";return false;}
    HGLOBAL loaded=LoadResource(self,item);
    DWORD len=SizeofResource(self,item);
    auto bytes=LockResource(loaded);
    if(!bytes || len<1 || len>40*1024*1024) {
        error=L"埋め込みリソースが不正です";
        return false;
    }
    std::wstring path=s_directory+L"\\"+name;
    std::wstring pending=path+L".tmp";
    HANDLE f=CreateFileW(pending.c_str(),GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,
                         FILE_ATTRIBUTE_NORMAL,nullptr);
    if(f==INVALID_HANDLE_VALUE) {error=L"一時ファイルを書き込めません";return false;}
    DWORD written=0;
    bool ok=WriteFile(f,bytes,len,&written,nullptr) && written==len;
    if(!FlushFileBuffers(f)) ok=false;
    CloseHandle(f);
    if(!ok || !MoveFileExW(pending.c_str(),path.c_str(),
             MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH)) {
        DeleteFileW(pending.c_str());
        error=L"埋め込みファイルを展開できません";
        return false;
    }
    return true;
}
}

std::wstring BundledPath(wchar_t const* name) {
    return s_directory+L"\\"+name;
}
std::wstring BundledDirectory() {return s_directory;}

bool InitializeBundledRuntime(HRESULT& hr, std::wstring& detail) {
    hr=E_FAIL;
    detail.clear();
    wchar_t tmp[MAX_PATH]{};
    if(!GetTempPathW(MAX_PATH,tmp)) {
        detail=L"ユーザーの一時フォルダーが使えません";return false;
    }
    std::wstring root=std::wstring(tmp)+L"SCPSLJP-Native";
    if(!CreateDirectoryW(root.c_str(),nullptr) && GetLastError()!=ERROR_ALREADY_EXISTS) {
        detail=L"一時フォルダーを作成できません";return false;
    }
    wchar_t leaf[100]{};
    swprintf_s(leaf,L"session-%lu-%llu",GetCurrentProcessId(),GetTickCount64());
    s_directory=root+L"\\"+leaf;
    if(!CreateDirectoryW(s_directory.c_str(),nullptr)) {
        detail=L"一時フォルダーの初期化に失敗しました";return false;
    }
    // All resources are part of SCPSLWinUI.exe; no extra distributed files.
    if (!ExtractResource(kBootstrap,L"Microsoft.WindowsAppRuntime.Bootstrap.dll",detail) ||
        !ExtractResource(kRust,L"scpsl-native-core.exe",detail) ||
        !ExtractResource(kResearcher,L"researcher.jpg",detail) ||
        !ExtractResource(kEmblem,L"scp-emblem.png",detail)) {
        return false;
    }
    auto path=BundledPath(L"Microsoft.WindowsAppRuntime.Bootstrap.dll");
    s_bootstrap=LoadLibraryExW(path.c_str(),nullptr,
                       LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    if(!s_bootstrap) {
        auto err=GetLastError();
        detail=L"Windows App SDK Bootstrap.dllを読み込めません (Windows error "+
            std::to_wstring(err)+L")";
        return false;
    }
    auto init=reinterpret_cast<BootstrapInitialize>(
        GetProcAddress(s_bootstrap,"MddBootstrapInitialize2"));
    s_shutdown=reinterpret_cast<BootstrapShutdown>(
        GetProcAddress(s_bootstrap,"MddBootstrapShutdown"));
    if(!init || !s_shutdown) {
        detail=L"Windows App SDKのBootstrap APIが見つかりません";
        return false;
    }
    PACKAGE_VERSION minVersion{};
    // Framework-dependent design, the Windows App Runtime 1.8 should exist on
    // the machine. Native EXE itself has no .NET dependency.
    hr=init(0x00010008,L"",minVersion,0);
    if(FAILED(hr)) {
        detail=L"Windows App Runtime 1.8 の初期化に失敗しました (HRESULT "+
            std::to_wstring(static_cast<unsigned int>(hr))+L")";
        return false;
    }
    s_initialized=true;
    return true;
}

void ShutdownBundledRuntime() noexcept {
    if(s_initialized && s_shutdown) s_shutdown();
    s_initialized=false;
    if(s_bootstrap) FreeLibrary(s_bootstrap);
    s_bootstrap=nullptr;
    if(s_directory.empty()) return;
    for(auto name:{L"Microsoft.WindowsAppRuntime.Bootstrap.dll",L"scpsl-native-core.exe",
                   L"researcher.jpg",L"scp-emblem.png"}) {
        auto path=BundledPath(name);
        DeleteFileW(path.c_str());
    }
    RemoveDirectoryW(s_directory.c_str());
    s_directory.clear();
}