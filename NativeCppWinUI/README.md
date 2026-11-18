# Native SCP:SL Japanese translation installer

This is the **native C++/WinRT + Rust** version of the installer.
The GUI is implemented using WinUI 3 and does **not** require .NET,
Tauri, Electron or WebView2.

## What is verified
- Native Windows x64 EXE compiled on Ubuntu with Clang/LLVM and Windows SDK.
- GUI starts on Windows 11.
- The confirmation dialog follows dark theme, with a blue primary
  install/switch button and neutral Cancel button.
- Rust tests cover Steam path validation, safe ZIP extraction and
  GitHub Releases version selection.
- Release history supports stable and Beta versions and a confirmation
  before replacing installed translations.

**Important:** The current single-file EXE uses Windows App Runtime **1.8**
installed on the target system. It embeds the native Rust core and bootstrap
DLL, but is **not yet a self-contained runtime-free build**.
The self-contained WinUI attempt currently fails during XAML resource
initialization; do not claim it works. No real-game installation test was run.

## Build on Windows

Build NativeCore first (Rust stable, Windows x64 MSVC target). Copy
scpsl-native-core.exe into NativeCore/bin/win-x64/.

Use Visual Studio C++ Build Tools, Windows SDK, CMake and the Windows
App SDK / WinApp CLI to configure NativeCppWinUI/CMakeLists.txt.
Build the SCPSLWinUI Release/x64 target.

For Windows CI see .github/workflows/winui-cpp.yml. The Ubuntu script
(build-on-ubuntu.sh) cross-builds with locally provisioned Windows SDK
headers and generated WinRT projections; set XWIN_ROOT and WINAPP_NUGET_CACHE
for non-default paths.

This native app lives alongside the SCP:SL translation project, without
modifying its translation data.

SCP Foundation emblem usage follows the repo's attribution file.
