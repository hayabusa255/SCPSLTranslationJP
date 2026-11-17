# SCP: SL 日本語化インストーラー — Native WinUI 3 + Rust

## 目的

Windows版からTauri/WebView2/HTML/CSSを完全に排除し、Windows App SDKのWinUI 3でUIを実装した置き換え版です。
Steam検出・GitHub Releases・翻訳インストール・SCP:SLの言語設定はRust製の独立CLIが担当します。
Linuxで同じRust coreを利用することは可能ですが、WinUI 3のGUI自体はWindows専用です。

## UI

- WinUI 3: MicaBackdrop、NavigationView、Expander、ListView、ProgressBar、InfoBar
- Windows標準の葉アイコン: Segoe Fluent Icons Leaf, Unicode U+E8BE（FontIcon、フォント同梱なし）
- Windowsのライト／ダーク／アクセント色をThemeResourceや標準AccentButtonStyleで反映
- 背景や本文文字色をハードコードせずOSのWinUIテーマに従う
- 文章: SCPSL 日本語化インストーラーは二酸化炭素排出量の削減に取り組んでいます
  - 句読点なし、改行なし、本文は通常のテーマ色。パロディとしての表示で、実際の環境実績ではありません
- 詳細情報 リンク → Microsoft公式
  https://support.microsoft.com/ja-jp/windows/deployment/updates-lifecycle/windows-update-is-now-carbon-aware
- 左上: SCP Foundationの研究員プロフィール、財団マーク、架空の業務用アカウント（実際のMicrosoft Entra IDとは無関係）
- リリース履歴: 毎回GitHub Releases APIから自動取得。本文をHTMLとして実行しない
- インストール先: ListViewItemのネイティブなSelected/Hover/Focusテーマを使用し、旧HTML版のダークモード選択行バグを解消

## バックエンド (NativeCore)

Tauri依存を削除したscpsl-native-core.exeをWinUIアプリの隣に配置します。WinUI側はProcessStartInfoで非表示起動し、標準出力のJSONを読みます。

- detect: SteamライブラリからSCP:SL (AppID 700330) を探す
- latest: GitHub APIから最新版・ダウンロードURLを取得
- history: GitHub APIから更新履歴を取得
- inspect [path]: ローカル翻訳フォルダーを確認
- validate [path]: 手動選択されたフォルダーを検証
- install [path]: ZIPをダウンロード・検証・全ファイル展開し、日本語設定も適用可能なら変更

ZIPはファイル数・サイズ・パス・シンボリックリンクを検証、配置時に既存バージョンの退避と復旧を行います。
古いJP-Verフォルダーは新バージョンの配置成功後に自動で整理します。旧版保持の設定項目はありません。

## ビルド手順

### Rust core
Linux（Rust、cargo-xwinを使用）:
    cd NativeCore
    cargo xwin build --release --target x86_64-pc-windows-msvc

### WinUI 3
Windows 11、.NET 10 SDKとWindows App SDKビルド環境が必要です。
    dotnet restore WinUI/SCPSL.WinUI.csproj -p:Platform=x64
    dotnet build WinUI/SCPSL.WinUI.csproj -c Release -p:Platform=x64

WindowsPackageType=NoneでMSIXを強制せず、Windows App SDKランタイムが存在することを前提とした軽量な配布形式です。
Rust coreはNativeCore/bin/win-x64/scpsl-native-core.exeに置くとWinUIビルド時に同梱されます。

## 検証状況 (2026-11-17)

- Rust coreはUbuntu 24.04 (RTX 2060)でテスト10件合格
- Linux版/Windows x64版のRust coreコンパイル成功
- Windows機でRust coreを実行しversion、latest、detectの動作を確認済み
- WinUI 3のC#/XAMLプロジェクトはソース作成済みだが実ビルド未確認
- 現在使用可能なWindows機は空き容量不足でWindows App SDKのNuGet復元を完了できず、WinUIアプリは未実行
- Ubuntuに配置したRust core・WinUIソースは /home/hayabusa255/SCPSLWinUINative に保存
- 実際のSCP: SLへの翻訳インストールは未検証

GitHub本体・PR #36・ユーザーの実ゲームファイルには変更を加えていません。