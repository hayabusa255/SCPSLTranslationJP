#pragma once
#include <string>
#include <winrt/Microsoft.UI.Xaml.Controls.h>

// Release Markdown -> native WinUI TextBlock/Inlines, never HTML or WebView.
// External hyperlinks open only after strict HTTPS validation.
namespace scpsl {
winrt::Microsoft::UI::Xaml::Controls::StackPanel RenderMarkdown(
    std::wstring const& input);
}