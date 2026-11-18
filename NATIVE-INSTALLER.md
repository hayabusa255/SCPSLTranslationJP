# SCP:SL 日本語化インストーラー — C++/Rustネイティブ版

## 推奨: 完全自己完結のv3.2版

- Windows x64ネイティブC++/WinRT + Rust。
- ランタイムの事前インストール不要（Windows App Runtime 1.8をアプリへ同梱）。
- 単一EXE約26.8MB。初回だけユーザーのLOCALAPPDATAへ必要DLLを展開。
- .NET / Tauri / WebView2 不要。管理者権限、追加インストール操作不要。
- WinUI 3のテーマ、Beta版を含む任意バージョンの翻訳切替、確認ダイアログ対応。
- Windows 11実機で初回展開・GUI起動成功。WinUI DLLがすべてアプリ専用領域から
  読み込まれていることをプロセスの実モジュールパスで確認済み。
- 実ゲームへの翻訳インストールは未検証。クリーンOSでのテストも今後推奨。

詳しい手順: NativeSelfContained/README.md
Ubuntu RTX2060ビルド: bash NativeSelfContained/build-on-ubuntu.sh

## 軽量版（約3.99MB）

NativeCore + NativeCppWinUIディレクトリは、Windows App Runtime 1.8依存の
Windows GUIネイティブ版です。こちらもWindows 11とGitHub Actionsでビルド成功済み。
詳しくは NativeCppWinUI/README.md を参照してください。

GitHubへの追加はインストーラー関連ソースのみで、翻訳データは変更しません。
