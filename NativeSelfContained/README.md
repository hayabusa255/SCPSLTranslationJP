# SCP:SL Japanese installer — fully self-contained native Windows x64

The single distributable EXE is built from a native Win32 C++20 launcher,
a C++/WinRT WinUI 3 frontend, and the existing Rust installer engine.
Unlike the smaller framework-dependent 3.99 MB build, this variant is
**not dependent on Windows App Runtime being installed on the computer**.

## Size and behavior

The EXE is approximately 26.8 MB. On first launch it extracts an app-private
WinUI runtime into LOCALAPPDATA/SCPSL-JP-Installer/RuntimeCache and launches
the inner native WinUI GUI. Later launches reuse the cache. It never installs
an MSIX runtime, does not modify system PATH, and requires no .NET, Tauri,
Electron, WebView2, or administrative privilege.

The correct WinUI registration-free activation manifest is embedded inside the
inner GUI's PE resources. Runtime DLLs are placed alongside the inner GUI EXE,
and Microsoft.UI.Xaml.Controls.pri is copied to resources.pri; the missing
resources.pri was the cause of the previous 0xC000027B WinUI startup crash.

Native DLLs are extracted from pinned Microsoft Windows App SDK 1.8 NuGet
packages (Foundation, InteractiveExperiences, WinUI, DWrite, Widgets).
The Rust extractor checks for traversal and ZIP size/file-count limits.

## Verified on Windows 11

- Clean first-launch extraction of the 26.8 MB distribution EXE succeeded.
- The native WinUI GUI opened and reported Window-activated.
- Both Microsoft.UI.Xaml.dll and Microsoft.WindowsAppRuntime.dll were loaded
  directly from the app-private extracted folder (not system framework paths).
- The same native UI includes stable/Beta version selection, fixed dark
  confirmation dialog, correct three-arrow SCP emblem, Steam discovery.
- No actual game directory or translation file was modified during QA.

Still not verified on an OS that has never had any Windows App Runtime
installed. A local-DLL-only process test supports the independent deployment
but a clean-VM smoke test is recommended before broad release.

## Build on the existing Ubuntu/RTX 2060 host

Requirements: clang-cl 20, LLVM lld-link and llvm-rc, cargo-xwin,
Windows SDK headers/import libraries, C++/WinRT generated headers located in
NativeCppWinUI/.winapp/include.

Run:
    bash NativeSelfContained/build-on-ubuntu.sh

The script prepares the pinned Microsoft runtime assets (downloads NuGet
packages if absent), generates the embedded WinRT activation manifest,
cross-compiles the inner WinUI GUI, includes the required resources.pri,
compresses the inner runtime, and cross-compiles the one-file Win32 launcher.

The generated EXE is:
    NativeSelfContained/build/SCPSL-Japanese-Installer-SelfContained.exe

Microsoft Windows App SDK files are not committed to Git. They are retrieved
from the vendor's NuGet feed as part of the build.

## Source structure
- Launcher/main.cpp: small native Win32 self-extracting launcher
- Launcher/app.manifest: Windows compatibility declaration
- InnerWinUI/resource_loader.cpp: app-local WinApp runtime bootstrap
- generate-runtime.py: pinned Windows App SDK components, reg-free class
  manifest, resources.pri, and bundle ZIP
- build-on-ubuntu.sh: C++/WinRT + Rust cross-build and single EXE assembly

The main repository translation data remains unchanged.
