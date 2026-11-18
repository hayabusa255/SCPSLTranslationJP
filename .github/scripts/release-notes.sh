#!/usr/bin/env bash
# リリース本文を標準出力に書き出す。使い方: TAG=JP-Ver14.2Fix REPO=owner/name .github/scripts/release-notes.sh
# 構成: 手書きノート(docs/releases/<tag>.md) または自動生成の変更点 -> 翻訳の統計 -> インストール方法
set -euo pipefail

TAG="${TAG:?TAG を指定してください}"
REPO="${REPO:-${GITHUB_REPOSITORY:-Slaviaaa2/SCPSLTranslationJP}}"
INSTALLER_DIR="src/SCPSL日本語化インストーラー"

prev=$(git describe --tags --abbrev=0 --match 'JP-*' "${TAG}^" 2>/dev/null || true)
if [ -n "$prev" ]; then range="${prev}..${TAG}"; else range="$TAG"; fi

manual="docs/releases/${TAG}.md"
if git cat-file -e "${TAG}:${manual}" 2>/dev/null; then
  git show "${TAG}:${manual}"
  echo
else
  echo "## 主な変更点"
  echo
  tr_list=""; in_list=""; other_list=""
  while IFS= read -r hash; do
    [ -n "$hash" ] || continue
    subject=$(git log -1 --format=%s "$hash")
    short=$(git log -1 --format=%h "$hash")
    files=$(git show --name-only --format= "$hash")
    line="- ${subject} (${short})"$'\n'
    if grep -q '^JP/' <<<"$files"; then tr_list+="$line"
    elif grep -qF "${INSTALLER_DIR}/" <<<"$files"; then in_list+="$line"
    else other_list+="$line"; fi
  done < <(git rev-list --no-merges "$range")
  [ -n "$tr_list" ] && printf '### 翻訳\n%s\n' "$tr_list"
  [ -n "$in_list" ] && printf '### インストーラー\n%s\n' "$in_list"
  [ -n "$other_list" ] && printf '### ツール・その他\n%s\n' "$other_list"
  if [ -n "$prev" ]; then
    echo "**全差分**: https://github.com/${REPO}/compare/${prev}...${TAG}"
    echo
  fi
fi

if [ -n "$prev" ]; then
  stat=$(git diff --numstat "$prev" "$TAG" -- 'JP/*.txt' | awk '{ f++; n += $1 + $2 } END { print f + 0, n + 0 }')
  echo "翻訳の変更（${prev} から）: $(cut -d' ' -f1 <<<"$stat") ファイル、$(cut -d' ' -f2 <<<"$stat") 行"
  echo
fi

cat <<'NOTES_EOF' | sed "s/__TAG__/${TAG}/g"
## インストール方法
1. **__TAG__.zipをダウンロード**：
   [![image](https://github.com/hayabusa255/SCPSLTranslationJP/assets/140249828/47f1eb3f-0004-4834-801a-248fd2f8a4d0)]()
2. **__TAG__.zipを解凍する**：
   必ず解凍してください。
3. **ディレクトリに解凍した__TAG__フォルダごと入れる**：
   `\SteamLibrary\steamapps\common\SCP Secret Laboratory\Translations` に解凍した__TAG__フォルダごと入れてください。ディレクトリの開き方はSteamを開いて以下の画像のようにして開きます。
   [![image](https://github.com/hayabusa255/SCPSLTranslationJP/assets/140249828/b7a3309b-cee3-44ac-92be-e97165cda390)]()

   [![image](https://cdn.wikiwiki.jp/to/w/scpsl/FAQ/::attach/%E3%82%B9%E3%82%AF%E3%83%AA%E3%83%BC%E3%83%B3%E3%82%B7%E3%83%A7%E3%83%83%E3%83%88%202023-09-10%20164322.png?rev=fa8748a3e2e5ad64e6c518908e3d01d1&t=20230910164350)]()
4. **ゲーム内設定から言語を変更する**：
   [![image](https://github.com/user-attachments/assets/8602ba68-a914-4ff1-9ed2-99b7bc28076a)]()
   翻訳ファイルを完全に適用するためにゲームを再起動することを推奨します。

## インストーラーを使用するインストール方法
### Windows
1. **SCPSL.exeをダウンロード** ：
    ![image](https://github.com/hayabusa255/SCPSLTranslationJP/assets/140249828/6f6a1a08-de22-4e60-ad33-b2af9acf1416)
2. **SCPSL.exeを実行**：
    SCPSL.exeをダブルクリックします。
    ![SCPSL.exe](https://github.com/hayabusa255/SCPSLTranslationJP/assets/140249828/a69c5917-356e-464e-ade4-c4008316bede)
    クリックするとこのような画面が表示されます。詳細情報より、実行を選択して実行します。

    Steamフォルダに書き込めないときだけ、次のような管理者権限の確認が表示されます。表示されたら、許可（はい）を選択してください。
    ![Run as Admin](https://github.com/hayabusa255/SCPSLTranslationJP/assets/140249828/e31991a3-6f39-48d6-8e6f-f9073c95b52c)
3. **ゲーム内設定から言語を変更する**：
    [![image](https://github.com/user-attachments/assets/8602ba68-a914-4ff1-9ed2-99b7bc28076a)]()

    翻訳ファイルを完全に適用するためにゲームを再起動することを推奨します。

### Linux / Steam Deck
1. **SCPSL-linux-x64をダウンロード**
2. **実行権限を付けて実行**：
   ダウンロードしたフォルダで、端末（Steam Deck ではデスクトップモードのKonsole）から次を実行します。
   ```sh
   chmod +x SCPSL-linux-x64 && ./SCPSL-linux-x64
   ```
   Steam（通常版 / Flatpak / Snap / Steam Deck）のSCPSLは自動で検出されます。見つからないときはゲームフォルダのパスを入力できます。
3. **ゲーム内設定から言語を変更する**：
   翻訳ファイルを完全に適用するためにゲームを再起動することを推奨します。

主なオプション：`--game-dir <パス>`（ゲームフォルダを指定）、`--dry-run`（書き込まずに内容だけ確認）、`-y` / `--yes`（確認を省略）、`--force`（再インストール）、`-h` / `--help`。

**※インストーラーは最新版の日本語訳を自動で取得してインストールします。githubで一度でもダウンロードした事がある場合それを再利用しましょう。**
**更新時も自動で更新してくれます。**
NOTES_EOF
