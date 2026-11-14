# 開発ガイド

翻訳データは `JP/`（日本語）と `en/`（英語原文）にあります。ツール類は .NET 8 で、`SCPSLTranslationJP.slnx` からまとめて開けます。

## 構成

| パス | 内容 |
|---|---|
| `SCPSLTranslationJP.slnx` | 全プロジェクトのソリューション |
| `Directory.Build.props` | 共通設定（ImplicitUsings / Nullable） |
| `src/SCPSL日本語化インストーラー/` | 日本語化インストーラー（リリースでは `SCPSL.exe` として配布） |
| `src/TranslationChecker/` | 翻訳チェッカー（旧 `translationchecker.exe` を ILSpy で復元したもの） |
| `src/TranslationEditor/` | 翻訳エディタ（en と JP を並べて JP を編集する、ローカル専用の Web ツール） |

## ビルド

```sh
dotnet build SCPSLTranslationJP.slnx -c Release
```

## 翻訳チェッカー

`en/` と `JP/` を行単位で比較し、未翻訳・欠落行・欠落ファイルを Markdown で出力します。AI なしでも次の静的チェックを行います。

- プレースホルダーの不一致: `[key]`、`{0}`、`%s`、`<color>` などのタグ名が、原文と訳で食い違っている行を報告します（集合で比較するので、同じものを訳中で複数回使うのは問題になりません）。
- 行数の不一致: `en` と `JP` の行数が違うファイルは、行ずれの可能性があるとして先頭に報告します。

```sh
dotnet run --project src/TranslationChecker -c Release [リポジトリのルート]
```

- ルートを省略すると、カレントディレクトリから `en` と `JP` を両方持つフォルダを上にたどって探します。
- 環境変数 `GEMINI_API_KEY` を設定すると、Gemini による訳のニュアンスチェックも行います（時間がかかります）。
  - モデルは既定で `gemini-3.5-flash-lite`（無料枠あり・最新の軽量モデル）。`GEMINI_MODEL` で変更できます（例: `gemini-4-hydrogen`）。
  - ファイル単位でまとめて送ります（1リクエストは最大400行程度。全行を数え、ファイルは途中で分割しません。400行を超えて訳されているファイルだけ分割します）。現状で約6リクエスト。モデルには未翻訳行も文脈として渡し、指摘は実際の行番号に対応付けます。無料枠の1日あたりの上限はプロジェクトごとに [AI Studio](https://aistudio.google.com/rate-limit) で確認してください。
  - プレースホルダーとタグは静的チェックの担当なので、AI には報告させません。
  - レビューの方針は [`docs/translation-review-guidelines.md`](translation-review-guidelines.md) に書かれていて、あればプロンプトにそのまま追加されます。このファイルを編集すると AI の指摘の基準が変わります（重大度の基準、口調の扱い、固有名詞など）。
- GitHub Actions の「Translation Checker」ワークフロー（手動実行）で、結果が Issue として作成されます。

単体の exe が必要な場合:

```sh
dotnet publish src/TranslationChecker -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

## 翻訳エディタ

`en/` と `JP/` を左右に並べて、ブラウザ上で JP を編集します。未翻訳の行、プレースホルダーの不一致、未保存の行が強調されます（プレースホルダーの判定は翻訳チェッカーと同じ考え方）。

```sh
dotnet run --project src/TranslationEditor [リポジトリのルート] [--port 5080] [--no-browser] [--base <git ref>]
```

- ルートを省略すると、カレントディレクトリから `en` と `JP` を両方持つフォルダを上にたどって探します。
- `http://localhost:5080` で localhost のみ待ち受けし、起動時に既定のブラウザを開きます（`--no-browser` で無効化、ポートは `--port` か環境変数 `TRANSLATION_EDITOR_PORT`）。
- 保存は Ctrl+S または保存ボタン。JP ファイルの BOM・改行コード・末尾改行の有無は保たれます。新規ファイルは BOM なし LF で作られます。
- 1行は1つの `<textarea>` で、Enter では改行できません（JP は行番号で en と対応するため）。
- 差分レビュー: 比較元の git ref（既定 `HEAD`、`--base <ref>` か画面上部の入力欄で変更）の JP と現在の JP を比べ、変わった行に修正前の訳と文字単位の差分、「元に戻す」ボタン、「確認済み」チェックを出します。ファイル一覧の `Δ12 / 未確認3` は変更行数と未確認数、「変更のあるファイルだけ」「変更行だけ」で絞り込めます。
  - 一覧の先頭の「全ファイルの変更」は、変更のある全ファイルの変更行だけを1画面に並べます（ここでも編集でき、Ctrl+S で未保存行のあるファイルごとに保存。ファイル名の見出しをクリックすると通常ビューへ）。`j` / `k` で次/前の変更行へ移動できます。
  - 「元に戻す」は入力欄を修正前に戻すだけで、保存するまでファイルは変わりません（もう一度押すと「やり直す」）。
  - 「確認済み」は JP の文字列ごとに、そのブラウザの localStorage にだけ保存されます。JP が変わると未確認に戻り、他のブラウザや PC とは共有されません。
  - ルートが git リポジトリでない、または git が無い場合は、差分の機能だけが無効になります。
