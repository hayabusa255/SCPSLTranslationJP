# SCP:SL 日本語化インストーラー — ネイティブC++/Rust版

## 現在の完成版
- Windows x64ネイティブ: C++20 / C++/WinRT / WinUI 3 と Rust。
- .NET、Tauri、Electron、WebView2不要。
- アプリのEXEは約3.99MBでRustエンジン・画像・Bootstrap DLLを内部に含みます。
- Windows 11でGUI起動、ライト/ダークテーマ、翻訳履歴の安定版/Betaフィルター、
  バージョン切替確認ダイアログを実機で確認済み。
- 確認ダイアログはWinUIのActualThemeに同期し、主要操作が青、キャンセルが中立色です。
- Rustの安全なZIP展開、GitHub Releaseのタグ選択とZIP URL検証を実装し、
  17件のテストを実行しました。

## 重要な未完了事項
- 現在の検証済みネイティブ版はWindows App Runtime 1.8が必要です。
- Windows App SDKの自己完結版（約23MB）は追加検証中ですが、XAMLリソース初期化で
  クラッシュしており、完成品としては公開していません。
- 実ゲームへの翻訳インストール・切替は未試験です。テストではゲーム本体を変更していません。
- Windows向けGitHub Actions CIの動作は、push後のジョブで確認が必要です。

## ソース構成
- NativeCppWinUI/ — WinUI 3ネイティブC++フロントエンド
- NativeCore/ — Steam検出、GitHub Releases、バージョン別導入・安全なZIP処理のRustコア
- .github/workflows/winui-cpp.yml — Windows x64ビルドと成果物のアップロード
- build-on-ubuntu.sh — Ubuntu上でClang/LLVM + xwinを使うクロスビルド（SDKキャッシュ必須）

## ビルド
RustをビルドしてからWindowsのMSVC/CMake/Windows App SDKでC++/WinRTをビルドします。
詳細は NativeCppWinUI/README.md を参照。Ubuntu cross-compileには生成済み
Windows C++/WinRTヘッダーとWindows SDKが必要で、ビルドスクリプトの環境変数
XWIN_ROOT と WINAPP_NUGET_CACHE でパスを指定できます。

このブランチは翻訳データと独立してNativeインストーラーの変更のみを追加します。
