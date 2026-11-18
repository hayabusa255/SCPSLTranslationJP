#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$(readlink -f "$0")")" && pwd)"
PROJ="$ROOT/NativeCppWinUI"
B="$PROJ/build-linux-clangcl"
XWIN="${XWIN_ROOT:-/root/scpsl-installer-build/xwin-sdk-cache/xwin}"
SDK="$XWIN/sdk/include"
CRT="$XWIN/crt/include"
NUGET="${WINAPP_NUGET_CACHE:-/root/scpsl-installer-build/winui-sdk-inspect}"
mkdir -p "$B"
test -s "$PROJ/.winapp/include/winrt/Microsoft.UI.Xaml.h" || { echo 'Missing C++/WinRT projections' >&2; exit 2; }
test -s "$ROOT/NativeCore/bin/win-x64/scpsl-native-core.exe" || { echo 'Missing Rust core exe' >&2; exit 2; }
test -s "$NUGET/foundation.nupkg" || { echo 'Missing Windows App SDK NuGet' >&2; exit 2; }
unzip -p "$NUGET/foundation.nupkg" runtimes/win-x64/native/Microsoft.WindowsAppRuntime.Bootstrap.dll > "$B/Microsoft.WindowsAppRuntime.Bootstrap.dll"
test -s "$B/Microsoft.WindowsAppRuntime.Bootstrap.dll"
for part in main resource_loader markdown_renderer; do
 echo "[cross] Compiling $part.cpp..."
 if ! "${CLANG_CL:-/usr/bin/clang-20}" --driver-mode=cl --target=x86_64-pc-windows-msvc \
      /nologo /std:c++20 /O2 /EHsc /MT /bigobj /utf-8 /Zc:__cplusplus \
      /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /DNOMINMAX \
      "/I$PROJ/.winapp/include" \
      "/imsvc$SDK/cppwinrt" "/imsvc$SDK/shared" "/imsvc$SDK/um" \
      "/imsvc$SDK/winrt" "/imsvc$SDK/ucrt" "/imsvc$CRT" \
      /c "$PROJ/$part.cpp" "/Fo$B/$part.obj" >"$B/$part.log" 2>&1; then
   tail -100 "$B/$part.log" >&2; exit 1
 fi
done
echo '[cross] Compiling embedded resources...'
cat > "$B/resources.rc" <<EOF
1 ICON "$PROJ/Assets/app.ico"
101 RCDATA "$B/Microsoft.WindowsAppRuntime.Bootstrap.dll"
102 RCDATA "$ROOT/NativeCore/bin/win-x64/scpsl-native-core.exe"
103 RCDATA "$PROJ/Assets/researcher.jpg"
104 RCDATA "$PROJ/Assets/scp-emblem.png"
1 24 "$PROJ/app.manifest"
EOF
if ! "${LLVM_RC:-/usr/bin/llvm-rc}" /fo "$B/resources.res" "$B/resources.rc" >"$B/resources.log" 2>&1; then
 tail -95 "$B/resources.log" >&2; exit 1
fi
echo '[cross] Linking SCPSLWinUI.exe...'
if ! "${LINKER:-/usr/bin/lld-link}" /nologo /machine:x64 /subsystem:windows /entry:wWinMainCRTStartup \
  "/out:$B/SCPSLWinUI.exe" \
  "$B/main.obj" "$B/resource_loader.obj" "$B/markdown_renderer.obj" "$B/resources.res" \
  "/libpath:$XWIN/sdk/lib/um/x86_64" "/libpath:$XWIN/sdk/lib/ucrt/x86_64" \
  "/libpath:$XWIN/crt/lib/x86_64" \
  WindowsApp.lib onecoreuap.lib shell32.lib ole32.lib user32.lib uuid.lib \
  >"$B/link.log" 2>&1; then
 tail -100 "$B/link.log" >&2; exit 1
fi
echo "[cross] Output: $B/SCPSLWinUI.exe"
file "$B/SCPSLWinUI.exe"
sha256sum "$B/SCPSLWinUI.exe"
