# SCP: Secret Laboratory v15.0 Private Beta 日本語翻訳

SCP: Secret Laboratory **v15.0 Private Beta** に対応した日本語訳です。新しく追加されたテキストや、以前から英語のまま残っていた部分を翻訳・見直しました。

> **ベータ版です。** v15.0 Private Beta向けのため、安定版のゲームでの使用は推奨しません。正式版で原文が変更された場合は、翻訳も更新する予定です。

## 主な変更点

### 翻訳
- **v15.0の新要素に対応**：SCP-953、各種ポップアップ、チュートリアル・ヒント設定、粒子分解装置の表示、ロシア向け接続リレーの案内など
- **表示崩れを修正**：行ずれ、`[key]`などのプレースホルダー、タグの不一致を見直し
- **用語を統一**：キーカード、陣営名、「駆け足」「手榴弾」、バッジ名などの表記を調整
- 翻訳チェッカーの整備、翻訳エディタなど開発ツールの改善

### 新しいWindowsインストーラー
- **C++/WinRT（WinUI 3）＋Rustのネイティブ版**に変更。約26MBの単一EXE
- Windows App Runtime・.NET・WebView2の**事前インストールは不要**。必要なWinUIファイルは初回起動時にアプリ専用領域へ展開されます
- Steamのゲームフォルダーを自動検出。手動でのフォルダー選択にも対応
- **更新履歴から安定版・Beta版を選び、バージョンを切り替え可能**
- 以前の翻訳版から切り替える際は、確認ダイアログを表示

## インストール方法

### Windows：インストーラーを使う方法（推奨）

1. **SCP: Secret Laboratoryを終了**してください。
2. このページの **[SCPSL.exe](https://github.com/hayabusa255/SCPSLTranslationJP/releases/download/JP-Ver15.0Beta/SCPSL.exe)** をダウンロードし、ダブルクリックして起動します。追加の.NETやWindows App Runtimeのインストールは不要です。
3. ゲームの保存先を確認してください。自動検出されない場合は、**「インストール先」→「フォルダーを参照」**から `SCP Secret Laboratory` フォルダーを選択します。
4. **「更新履歴」**を開き、表示を **「Beta・プレビュー版」** または **「すべて」** に切り替えます。
5. **`JP-Ver15.0Beta`** を展開し、**「このバージョンをインストール」**を押します。確認ダイアログが表示されたら、対象版を確認して **「切り替える」** を押してください。
6. インストール完了後、ゲームを起動して**ゲーム内の言語設定から「日本語」を選択**します。翻訳を完全に反映させるため、ゲームの再起動を推奨します。

**重要：** インストーラーの通常の「最新版」確認は安定版を指す場合があります。v15.0 Private Betaを導入する場合は、必ず**「更新履歴」から`JP-Ver15.0Beta`を指定**してください。

### Windows・Linux・Steam Deck：ZIPを使う手動インストール

1. **[JP-Ver15.0Beta.zip](https://github.com/hayabusa255/SCPSLTranslationJP/releases/download/JP-Ver15.0Beta/JP-Ver15.0Beta.zip)** をダウンロードし、解凍してください。
2. 解凍したファイルを **`JP-Ver15.0Beta` フォルダー**にまとめます。フォルダーの直下に `manifest.json` と翻訳用の `.txt` ファイルが入っている状態にしてください（フォルダーが二重にならないよう注意）。
3. SteamのライブラリでSCP: Secret Laboratoryの**「管理」→「ローカルファイルを閲覧」**を開き、ゲームフォルダー内の `Translations` に、`JP-Ver15.0Beta` フォルダーを丸ごと配置します。
4. 配置例（Windows）：
   ```text
   ...\SteamLibrary\steamapps\common\SCP Secret Laboratory\Translations\JP-Ver15.0Beta\manifest.json
   ```
5. 過去の日本語訳が残っている場合は、必要に応じてバックアップを取ってから旧 `JP-Ver*` フォルダーを整理してください。ゲーム起動後、言語を**日本語**に設定して再起動してください。

手動でのゲームフォルダーの開き方（参考画像）：

![Steamからゲームフォルダーを開く方法](https://github.com/hayabusa255/SCPSLTranslationJP/assets/140249828/b7a3309b-cee3-44ac-92be-e97165cda390)

![ゲーム内の言語設定](https://github.com/user-attachments/assets/8602ba68-a914-4ff1-9ed2-99b7bc28076a)

> Linux / Steam Deck向けの新しいネイティブ実行ファイルは提供していません。Linux環境では上記ZIPによる手動インストールをご利用ください。

## 不具合報告・注意事項

ベータ版につき、誤訳・不自然な表現・表示崩れが残っている可能性があります。見つけた場合は[GitHub Issues](https://github.com/hayabusa255/SCPSLTranslationJP/issues)などでご報告ください。

**インストーラーのWindows実機起動は確認済みですが、v15.0 Private Betaのゲーム本体への適用検証はまだ完了していません。**
