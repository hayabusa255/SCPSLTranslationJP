#pragma once
#include <windows.h>
#include <appmodel.h>
#include <string>

// A single distributable EXE embeds Rust engine, UI assets and Windows
// App Runtime Bootstrap.dll as Win32 RCDATA resources. They are extracted
// under a per-process user temporary directory, then cleaned up on exit.
std::wstring BundledPath(wchar_t const* name);
std::wstring BundledDirectory();
bool InitializeBundledRuntime(HRESULT& hr, std::wstring& detail);
void ShutdownBundledRuntime() noexcept;