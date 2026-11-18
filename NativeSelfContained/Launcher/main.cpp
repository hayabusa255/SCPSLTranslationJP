#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <filesystem>
#include <string>
#include <system_error>
#include <cstdio>
namespace fs=std::filesystem;
namespace {
constexpr int kRust=201,kBundle=202;
constexpr auto kCacheName=L"WinUI-v3.2-sourcebuild-20261118";
void Error(std::wstring const& message){
 MessageBoxW(nullptr,message.c_str(),L"SCP: SL 日本語化インストーラー",
             MB_OK|MB_ICONERROR|MB_SETFOREGROUND);
}
std::wstring Code(DWORD d){return std::to_wstring(d);}
bool ExtractResource(int id,fs::path const& dest,DWORD max,std::wstring& error){
 HMODULE self=GetModuleHandleW(nullptr);
 HRSRC r=FindResourceW(self,MAKEINTRESOURCEW(id),RT_RCDATA);
 if(!r){error=L"EXEのデータがありません";return false;}
 DWORD bytes=SizeofResource(self,r);
 void const* buffer=LockResource(LoadResource(self,r));
 if(!buffer || !bytes || bytes>max){error=L"EXEのデータが不正です";return false;}
 HANDLE f=CreateFileW(dest.c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);
 if(f==INVALID_HANDLE_VALUE){error=L"書き込み先を作成できません ("+Code(GetLastError())+L")";return false;}
 DWORD written=0;
 bool ok=WriteFile(f,buffer,bytes,&written,nullptr)&&written==bytes;
 if(ok)ok=FlushFileBuffers(f)!=FALSE;
 CloseHandle(f);
 if(!ok){DeleteFileW(dest.c_str());error=L"埋め込みファイルを書き込めません";return false;}
 return true;
}
bool RunExtract(fs::path const& helper,fs::path const& zip,
                fs::path const& dir,std::wstring& error){
 std::wstring cmd=L"\""+helper.wstring()+L"\" runtime-extract \""+
    zip.wstring()+L"\" \""+dir.wstring()+L"\"";
 STARTUPINFOW start{};start.cb=sizeof(start);
 PROCESS_INFORMATION process{};
 if(!CreateProcessW(helper.c_str(),cmd.data(),nullptr,nullptr,FALSE,CREATE_NO_WINDOW,
                    nullptr,dir.c_str(),&start,&process)){
    error=L"ランタイム展開エンジンを起動できません ("+Code(GetLastError())+L")";return false;
 }
 CloseHandle(process.hThread);
 DWORD wait=WaitForSingleObject(process.hProcess,120000);
 DWORD exit=1;
 if(wait==WAIT_OBJECT_0)GetExitCodeProcess(process.hProcess,&exit);
 else{TerminateProcess(process.hProcess,1);WaitForSingleObject(process.hProcess,2000);}
 CloseHandle(process.hProcess);
 if(wait!=WAIT_OBJECT_0 || exit!=0){
    error=L"ランタイム展開に失敗しました (exit "+Code(exit)+L")";return false;
 }
 return true;
}
bool Ready(fs::path const& p){
 return fs::is_regular_file(p/L".ready") &&
        fs::is_regular_file(p/L"SCPSLWinUI.exe") &&
        fs::is_regular_file(p/L"resources.pri") &&
        fs::is_regular_file(p/L"Microsoft.UI.Xaml.dll") &&
        fs::is_regular_file(p/L"Microsoft.WindowsAppRuntime.dll");
}
bool Prepare(fs::path& dir,std::wstring& error){
 wchar_t buf[32768]{};
 auto len=GetEnvironmentVariableW(L"LOCALAPPDATA",buf,32768);
 if(!len || len>=32768){error=L"LOCALAPPDATAを取得できません";return false;}
 auto parent=fs::path(buf)/L"SCPSL-JP-Installer"/L"RuntimeCache";
 std::error_code ec;
 fs::create_directories(parent,ec);
 if(ec){error=L"アプリの保存先を作成できません";return false;}
 dir=parent/kCacheName;
 if(Ready(dir))return true;
 wchar_t name[100]{};
 swprintf_s(name,L"stage-%lu-%llu",GetCurrentProcessId(),GetTickCount64());
 auto stage=parent/name;
 fs::create_directories(stage,ec);
 if(ec){error=L"一時展開先を作成できません";return false;}
 auto cleanup=[&](){std::error_code ignored;fs::remove_all(stage,ignored);};
 auto helper=stage/L"scpsl-native-core.exe";
 auto zip=stage/L"runtime-payload.zip";
 if(!ExtractResource(kRust,helper,12*1024*1024,error) ||
    !ExtractResource(kBundle,zip,35*1024*1024,error) ||
    !RunExtract(helper,zip,stage,error)){cleanup();return false;}
 if(!fs::is_regular_file(stage/L"SCPSLWinUI.exe") ||
    !fs::is_regular_file(stage/L"resources.pri") ||
    !fs::is_regular_file(stage/L"Microsoft.UI.Xaml.dll")){
    error=L"展開したWinUIのファイルが不足しています";cleanup();return false;
 }
 fs::remove(helper,ec);fs::remove(zip,ec);
 auto mark=stage/L".ready";
 HANDLE f=CreateFileW(mark.c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);
 if(f==INVALID_HANDLE_VALUE){error=L"展開マーカーを作成できません";cleanup();return false;}
 constexpr char text[]="SCPSL Native WinUI selfcontained v3.2 sourcebuild 20261118";
 DWORD written=0;
 bool ok=WriteFile(f,text,sizeof(text)-1,&written,nullptr)&&written==sizeof(text)-1;
 CloseHandle(f);
 if(!ok){error=L"展開マーカーの書込に失敗しました";cleanup();return false;}
 if(fs::exists(dir)){fs::remove_all(dir,ec);if(ec){error=L"旧ランタイムキャッシュを更新できません";cleanup();return false;}}
 ec.clear();fs::rename(stage,dir,ec);
 if(ec){error=L"ランタイムを配置できません";cleanup();return false;}
 return true;
}
bool Launch(fs::path const& dir,std::wstring& error){
 auto exe=dir/L"SCPSLWinUI.exe";
 std::wstring command=L"\""+exe.wstring()+L"\"";
 STARTUPINFOW start{};start.cb=sizeof(start);
 PROCESS_INFORMATION p{};
 if(!CreateProcessW(exe.c_str(),command.data(),nullptr,nullptr,FALSE,0,
                    nullptr,dir.c_str(),&start,&p)){
    error=L"ネイティブ画面を起動できません ("+Code(GetLastError())+L")";return false;
 }
 CloseHandle(p.hThread);
 DWORD result=WaitForSingleObject(p.hProcess,1500);
 if(result==WAIT_OBJECT_0){
   DWORD code=0;GetExitCodeProcess(p.hProcess,&code);
   CloseHandle(p.hProcess);
   if(code!=0){error=L"WinUI起動時にエラーが発生しました ("+Code(code)+L")";return false;}
 }else CloseHandle(p.hProcess);
 return true;
}
}
int WINAPI wWinMain(HINSTANCE,HINSTANCE,LPWSTR,int){
 fs::path root;
 std::wstring error;
 if(!Prepare(root,error)){Error(error);return 1;}
 if(!Launch(root,error)){Error(error);return 2;}
 return 0;
}
