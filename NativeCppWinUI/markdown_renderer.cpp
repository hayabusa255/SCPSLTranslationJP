#include "markdown_renderer.h"
#include <windows.h>
#include <shellapi.h>
#include <cwctype>
#include <algorithm>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Microsoft.UI.Xaml.h>
#include <winrt/Microsoft.UI.Xaml.Controls.h>
#include <winrt/Microsoft.UI.Xaml.Documents.h>
#include <winrt/Microsoft.UI.Xaml.Media.h>
#include <winrt/Windows.UI.Text.h>

using namespace winrt;
using namespace Microsoft::UI::Xaml;
using namespace Microsoft::UI::Xaml::Controls;
using namespace Microsoft::UI::Xaml::Documents;
using namespace Microsoft::UI::Xaml::Media;

namespace {
std::wstring Trim(std::wstring text) {
    auto notSpace=[](wchar_t c){return !iswspace(c);};
    auto start=std::find_if(text.begin(),text.end(),notSpace);
    auto end=std::find_if(text.rbegin(),text.rend(),notSpace).base();
    return start>=end?L"":std::wstring(start,end);
}
bool SafeLink(std::wstring const& url) {
    if(url.size()>1500 || url.size()<9 || url.rfind(L"https://",0)!=0) return false;
    return url.find_first_of(L"\r\n\t\"'<>\\") == std::wstring::npos;
}
void AppendRun(TextBlock const& block,std::wstring const& text,int style=0) {
    if(text.empty()) return;
    Run r;r.Text(hstring(text));
    if(style==1) r.FontWeight(Windows::UI::Text::FontWeights::SemiBold());
    if(style==2) r.FontStyle(Windows::UI::Text::FontStyle::Italic);
    if(style==3) r.FontFamily(FontFamily(L"Consolas"));
    block.Inlines().Append(r);
}
void AppendLink(TextBlock const& block,std::wstring const& label,std::wstring const& url) {
    if(!SafeLink(url)){AppendRun(block,label);return;}
    Hyperlink link;
    Run r;r.Text(hstring(label));link.Inlines().Append(r);
    link.Click([url](auto const&,auto const&){
        ShellExecuteW(nullptr,L"open",url.c_str(),nullptr,nullptr,SW_SHOWNORMAL);
    });
    block.Inlines().Append(link);
}
void AppendMarkdownInlines(TextBlock const& block,std::wstring const& source) {
    size_t i=0;
    while(i<source.size()){
        if(source.compare(i,2,L"**")==0) {
            auto end=source.find(L"**",i+2);
            if(end!=std::wstring::npos){AppendRun(block,source.substr(i+2,end-i-2),1);i=end+2;continue;}
        }
        if(source[i]==wchar_t(96)){
            auto end=source.find(wchar_t(96),i+1);
            if(end!=std::wstring::npos){AppendRun(block,source.substr(i+1,end-i-1),3);i=end+1;continue;}
        }
        if(source[i]==L'['){
            auto close=source.find(L']',i+1);
            if(close!=std::wstring::npos && close+1<source.size() && source[close+1]==L'('){
                auto end=source.find(L')',close+2);
                if(end!=std::wstring::npos){
                    AppendLink(block,source.substr(i+1,close-i-1),source.substr(close+2,end-close-2));
                    i=end+1;continue;
                }
            }
        }
        if(source[i]==L'*' && i+1<source.size() && source[i+1]!=L' '){
            auto end=source.find(L'*',i+1);
            if(end!=std::wstring::npos && end>i+1){
                AppendRun(block,source.substr(i+1,end-i-1),2);i=end+1;continue;
            }
        }
        if(source.compare(i,8,L"https://")==0){
            auto end=source.find_first_of(L" \t\r\n)",i);
            if(end==std::wstring::npos)end=source.size();
            auto url=source.substr(i,end-i);
            while(!url.empty() && (url.back()==L'.'||url.back()==L','))url.pop_back();
            AppendLink(block,L"リンクを開く",url);i=end;continue;
        }
        size_t next=i+1;
        while(next<source.size() && source[next]!=L'*' && source[next]!=wchar_t(96)
            && source[next]!=L'[' && source[next]!=L'h')++next;
        AppendRun(block,source.substr(i,next-i));i=next;
    }
}
}

namespace scpsl {
StackPanel RenderMarkdown(std::wstring const& raw){
    StackPanel stack;stack.Spacing(5);
    size_t cursor=0;bool code=false;int lines=0;
    while(cursor<raw.size() && lines<450){
        auto end=raw.find(L'\n',cursor);
        if(end==std::wstring::npos)end=raw.size();
        auto line=Trim(raw.substr(cursor,end-cursor));
        cursor=end+1;lines++;
        if(line.empty())continue;
        if(line.size()>=3 && line.substr(0,3)==std::wstring(3,wchar_t(96))){code=!code;continue;}
        if(line.find(L"Full Changelog")!=std::wstring::npos){
            auto urlPos=line.find(L"https://");
            if(urlPos!=std::wstring::npos){
                TextBlock link;link.TextWrapping(TextWrapping::Wrap);link.FontSize(12);
                AppendLink(link,L"GitHubで変更履歴を比較",Trim(line.substr(urlPos)));
                stack.Children().Append(link);
            }
            continue;
        }
        TextBlock block;block.TextWrapping(TextWrapping::Wrap);
        block.FontSize(12.5);block.Margin(Thickness{0,3,0,3});
        if(code){block.FontFamily(FontFamily(L"Consolas"));AppendRun(block,line);}
        else {
            int heading=0;while(heading<6 && heading<static_cast<int>(line.size()) && line[heading]==L'#')heading++;
            if(heading>0 && line.size()>static_cast<size_t>(heading) && line[heading]==L' '){
                line=Trim(line.substr(heading));
                if(line==L"What's Changed")line=L"主な変更点";
                else if(line==L"New Contributors")line=L"新しい貢献者";
                block.FontSize(heading==1?19:heading==2?16:14);
                block.FontWeight(Windows::UI::Text::FontWeights::SemiBold());
                block.Margin(Thickness{0,12,0,6});
            }else if(line.rfind(L"- ",0)==0 || line.rfind(L"* ",0)==0){
                line=L"•  "+Trim(line.substr(2));block.Margin(Thickness{14,2,0,2});
            }else if(line.rfind(L"・",0)==0){
                line=L"•  "+Trim(line.substr(1));block.Margin(Thickness{14,2,0,2});
            }else if(line.rfind(L"> ",0)==0){
                line=L"引用: "+Trim(line.substr(2));block.Opacity(0.75);
            }else if(line.rfind(L"---",0)==0)continue;
            AppendMarkdownInlines(block,line);
        }
        stack.Children().Append(block);
    }
    if(stack.Children().Size()==0){
        auto empty=TextBlock();empty.Text(L"変更内容は記載されていません");empty.FontSize(12);
        stack.Children().Append(empty);
    }
    return stack;
}
}