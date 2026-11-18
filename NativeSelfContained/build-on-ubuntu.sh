#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$(readlink -f "$0")")/.." && pwd)"
HERE="$ROOT/NativeSelfContained"
B="$HERE/build"
XWIN=/root/scpsl-installer-build/xwin-sdk-cache/xwin
SDK="$XWIN/sdk/include"
CRT="$XWIN/crt/include"
NUGET=/root/scpsl-installer-build/winui-sdk-inspect
mkdir -p "$B/runtime"
python3 "$HERE/generate-runtime.py" --cache "$NUGET" --output "$B"
if ! test -s "$ROOT/NativeCore/bin/win-x64/scpsl-native-core.exe";then
 (cd "$ROOT/NativeCore" && cargo xwin build --release --target x86_64-pc-windows-msvc)
 mkdir -p "$ROOT/NativeCore/bin/win-x64"
 cp "$ROOT/NativeCore/target/x86_64-pc-windows-msvc/release/scpsl-native-core.exe" "$ROOT/NativeCore/bin/win-x64/"
fi
compile_winui(){
 /usr/bin/clang-20 --driver-mode=cl --target=x86_64-pc-windows-msvc \
  /nologo /std:c++20 /O2 /EHsc /MT /bigobj /utf-8 /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /DNOMINMAX \
  "/I$ROOT/NativeCppWinUI" "/I$ROOT/NativeCppWinUI/.winapp/include" \
  "/imsvc$SDK/cppwinrt" "/imsvc$SDK/shared" "/imsvc$SDK/um" \
  "/imsvc$SDK/winrt" "/imsvc$SDK/ucrt" "/imsvc$CRT" \
  /c "$1" "/Fo$2"
}
echo '[self-contained] Compile WinUI C++ frontend'
compile_winui "$ROOT/NativeCppWinUI/main.cpp" "$B/main.obj" >"$B/main.log" 2>&1 \
  || { tail -90 "$B/main.log"; exit 1; }
compile_winui "$ROOT/NativeCppWinUI/markdown_renderer.cpp" "$B/markdown.obj" >"$B/markdown.log" 2>&1 \
  || { tail -90 "$B/markdown.log"; exit 1; }
compile_winui "$HERE/InnerWinUI/resource_loader.cpp" "$B/loader.obj" >"$B/loader.log" 2>&1 \
  || { tail -90 "$B/loader.log"; exit 1; }
cat > "$B/inner.rc" <<EOF
1 ICON "$ROOT/NativeCppWinUI/Assets/app.ico"
102 RCDATA "$ROOT/NativeCore/bin/win-x64/scpsl-native-core.exe"
103 RCDATA "$ROOT/NativeCppWinUI/Assets/researcher.jpg"
104 RCDATA "$ROOT/NativeCppWinUI/Assets/scp-emblem.png"
1 24 "$B/app-selfcontained.manifest"
EOF
/usr/bin/llvm-rc /fo "$B/inner.res" "$B/inner.rc" >"$B/inner-rc.log" 2>&1 \
 || { tail -90 "$B/inner-rc.log"; exit 1; }
/usr/bin/lld-link /nologo /machine:x64 /subsystem:windows /entry:wWinMainCRTStartup \
 "/out:$B/runtime/SCPSLWinUI.exe" "$B/main.obj" "$B/markdown.obj" "$B/loader.obj" "$B/inner.res" \
 "/libpath:$XWIN/sdk/lib/um/x86_64" "/libpath:$XWIN/sdk/lib/ucrt/x86_64" "/libpath:$XWIN/crt/lib/x86_64" \
 WindowsApp.lib onecoreuap.lib shell32.lib ole32.lib user32.lib uuid.lib \
 >"$B/inner-link.log" 2>&1 || { tail -90 "$B/inner-link.log"; exit 1; }
echo '[self-contained] Compress native Windows App SDK assets'
python3 "$HERE/generate-runtime.py" --cache "$NUGET" --output "$B" --pack-only
echo '[self-contained] Compile native Win32 launcher'
/usr/bin/clang-20 --driver-mode=cl --target=x86_64-pc-windows-msvc /nologo \
 /std:c++20 /O2 /EHsc /MT /utf-8 /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /DNOMINMAX \
 "/imsvc$SDK/shared" "/imsvc$SDK/um" "/imsvc$SDK/ucrt" "/imsvc$CRT" \
 /c "$HERE/Launcher/main.cpp" "/Fo$B/launcher.obj" >"$B/launcher.log" 2>&1 \
 || { tail -90 "$B/launcher.log"; exit 1; }
cat > "$B/launcher.rc" <<EOF
1 ICON "$ROOT/NativeCppWinUI/Assets/app.ico"
201 RCDATA "$ROOT/NativeCore/bin/win-x64/scpsl-native-core.exe"
202 RCDATA "$B/SCPSL-WinUI-1.8-selfcontained-bundle.zip"
1 24 "$HERE/Launcher/app.manifest"
EOF
/usr/bin/llvm-rc /fo "$B/launcher.res" "$B/launcher.rc" >"$B/launcher-rc.log" 2>&1 \
 || { tail -90 "$B/launcher-rc.log"; exit 1; }
/usr/bin/lld-link /nologo /machine:x64 /subsystem:windows /entry:wWinMainCRTStartup \
 "/out:$B/SCPSL-Japanese-Installer-SelfContained.exe" "$B/launcher.obj" "$B/launcher.res" \
 "/libpath:$XWIN/sdk/lib/um/x86_64" "/libpath:$XWIN/sdk/lib/ucrt/x86_64" "/libpath:$XWIN/crt/lib/x86_64" \
 kernel32.lib user32.lib shell32.lib ole32.lib uuid.lib \
 >"$B/launcher-link.log" 2>&1 || { tail -90 "$B/launcher-link.log"; exit 1; }
file "$B/SCPSL-Japanese-Installer-SelfContained.exe"
ls -lh "$B/SCPSL-Japanese-Installer-SelfContained.exe"
sha256sum "$B/SCPSL-Japanese-Installer-SelfContained.exe"
