# 開発ガイド

翻訳データは `JP/`（日本語）と `en/`（英語原文）にあります。ツール類は .NET 8 で、`SCPSLTranslationJP.slnx` からまとめて開けます。

## 構成

| パス | 内容 |
|---|---|
| `SCPSLTranslationJP.slnx` | 全プロジェクトのソリューション |
| `Directory.Build.props` | 共通設定（ImplicitUsings / Nullable） |
| `src/SCPSL日本語化インストーラー/` | 日本語化インストーラー（リリースでは `SCPSL.exe` として配布） |
| `src/TranslationChecker/` | 翻訳チェッカー（旧 `translationchecker.exe` を ILSpy で復元したもの） |

## ビルド

```sh
dotnet build SCPSLTranslationJP.slnx -c Release
```

## 翻訳チェッカー

`en/` と `JP/` を行単位で比較し、未翻訳・欠落行・欠落ファイルを Markdown で出力します。

```sh
dotnet run --project src/TranslationChecker -c Release [リポジトリのルート]
```

- ルートを省略すると、カレントディレクトリから `en` と `JP` を両方持つフォルダを上にたどって探します。
- 環境変数 `GEMINI_API_KEY` を設定すると、Gemini による訳のニュアンスチェックも行います（時間がかかります）。
  - モデルは既定で `gemini-3.5-flash-lite`（無料枠あり・最新の軽量モデル）。`GEMINI_MODEL` で変更できます（例: `gemini-4-hydrogen`）。
  - 1回の実行で最大200行ずつ、ファイルをまたいでまとめて送ります（現状で約9リクエスト）。無料枠の1日あたりの上限はプロジェクトごとに [AI Studio](https://aistudio.google.com/rate-limit) で確認してください。
- GitHub Actions の「Translation Checker」ワークフロー（手動実行）で、結果が Issue として作成されます。

単体の exe が必要な場合:

```sh
dotnet publish src/TranslationChecker -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```
