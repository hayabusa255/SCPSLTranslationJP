use crate::{github, steam, language};
use serde::Serialize;
use std::{fs::{self, File}, io::{self, Cursor, Write}, path::{Path, PathBuf}, time::{SystemTime, UNIX_EPOCH}};


const MAX_FILE_COUNT: usize = 300;
const MAX_FILE_BYTES: u64 = 2 * 1024 * 1024;
const MAX_UNCOMPRESSED_BYTES: u64 = 32 * 1024 * 1024;

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct InstallSummary {
    pub version: String,
    pub installed_path: String,
    pub file_count: usize,
    pub bytes_downloaded: usize,
    pub language_status: String,
}

fn emit(percent: u8, stage: &str, detail: impl Into<String>) {
    let event = serde_json::json!({
        "kind": "progress",
        "percent": percent,
        "stage": stage,
        "detail": detail.into()
    });
    println!("{}", event);
    let _ = std::io::stdout().flush();
}

fn count_txt(dir: &Path) -> usize {
    fs::read_dir(dir).map(|entries| entries.flatten().filter(|e| e.path().extension().is_some_and(|ext| ext.eq_ignore_ascii_case("txt")) && e.path().is_file()).count()).unwrap_or(0)
}

fn extract_verified_zip(data: &[u8], temp_dir: &Path) -> Result<usize, String> {
    let mut archive = zip::ZipArchive::new(Cursor::new(data)).map_err(|e| format!("ZIPを開けません: {e}"))?;
    if archive.len() > MAX_FILE_COUNT { return Err("ZIPにファイルが多すぎます".into()); }
    let mut total = 0u64;
    let mut count = 0usize;
    for idx in 0..archive.len() {
        let mut entry = archive.by_index(idx).map_err(|e| format!("ZIPの読み取りエラー: {e}"))?;
        if let Some(mode) = entry.unix_mode() {
            if mode & 0o170000 == 0o120000 { return Err("ZIPのシンボリックリンクは使用できません".into()); }
        }
        // Preserve *every* asset from the release ZIP, including manifest.json.
        // zip::enclosed_name disallows absolute paths and traversal out of staging.
        let name = entry.enclosed_name().ok_or("ZIPに不正なファイル名があります")?.to_path_buf();
        let destination = temp_dir.join(&name);
        if entry.is_dir() {
            fs::create_dir_all(&destination).map_err(|e| format!("展開先の作成に失敗しました: {e}"))?;
            continue;
        }
        if entry.size() > MAX_FILE_BYTES { return Err("ZIP内に大きすぎるファイルがあります".into()); }
        total = total.saturating_add(entry.size());
        if total > MAX_UNCOMPRESSED_BYTES { return Err("ZIPの展開後サイズが上限を超えます".into()); }
        if let Some(parent) = destination.parent() {
            fs::create_dir_all(parent).map_err(|e| format!("展開先の作成に失敗しました: {e}"))?;
        }
        let mut output = File::options().write(true).create_new(true).open(&destination)
            .map_err(|e| format!("ZIPの展開先に書き込めません: {e}"))?;
        let copied = io::copy(&mut entry, &mut output).map_err(|e| format!("展開に失敗しました: {e}"))?;
        if copied != entry.size() { return Err("ファイルが途中で切れています".into()); }
        output.flush().map_err(|e| format!("書き込みに失敗しました: {e}"))?;
        count += 1;
    }
    if count < 10 { return Err("ZIP内のファイルが10件未満のため、適用を中止しました".into()); }
    Ok(count)
}

fn remove_old_versions(translations_dir: &Path, current: &str) -> Vec<String> {
    let mut failed = Vec::new();
    let Ok(entries) = fs::read_dir(translations_dir) else { return failed; };
    for entry in entries.flatten() {
        let name = entry.file_name().to_string_lossy().to_string();
        if name == current || !steam::is_translation_name(&name) { continue; }
        let Ok(typ) = entry.file_type() else { continue; };
        if !typ.is_dir() || typ.is_symlink() || count_txt(&entry.path()) == 0 { continue; }
        if let Err(e) = fs::remove_dir_all(entry.path()) { failed.push(format!("{name}: {e}")); }
    }
    failed
}

pub async fn install(game_dir: String) -> Result<InstallSummary, String> {
    let validated = steam::validate_manual(game_dir)?;
    let game = PathBuf::from(validated.path);
    emit(3, "準備しています", "ゲームのインストール先を確認しました");
    let release = github::latest().await?;
    emit(10, "最新版を確認しました", format!("{} をダウンロードします", release.tag));
    let data = github::download(&release.download_url, |done, total| {
        let percent = if let Some(total) = total.filter(|v| *v > 0) {
            (10 + (done.saturating_mul(50) / total).min(50)) as u8
        } else { 32 };
        emit(percent, "翻訳をダウンロード中", format!("{:.1} KiB を取得", done as f64 / 1024.0));
    }).await?;
    emit(64, "展開の準備", "翻訳ファイルの安全性と配置先を確認しています");
    let translations = game.join("Translations");
    fs::create_dir_all(&translations).map_err(|e| format!("Translationsフォルダーを作成できません: {e}"))?;
    // Prevent directory symlinks from unexpectedly redirecting installation outside the game folder.
    if fs::symlink_metadata(&translations).is_ok_and(|m| m.file_type().is_symlink()) {
        return Err("Translations がシンボリックリンクのため、手動で確認してください".into());
    }
    let nonce = SystemTime::now().duration_since(UNIX_EPOCH)
        .map_err(|_| "システム時刻が不正です")?.as_nanos();
    let temp_dir = translations.join(format!(".scpsl-jp-stage-{}-{nonce}", std::process::id()));
    fs::create_dir(&temp_dir).map_err(|e| format!("一時フォルダーを作成できません: {e}"))?;
    let result = (|| -> Result<InstallSummary, String> {
        let file_count = extract_verified_zip(&data, &temp_dir)?;
        emit(88, "翻訳を配置中", format!("{file_count} ファイルの検証が完了しました"));
        let destination = translations.join(&release.tag);
        let backup = translations.join(format!(".scpsl-jp-backup-{}-{nonce}", release.tag));
        let had_destination = destination.exists();
        if had_destination {
            if destination.symlink_metadata().map_err(|e| format!("既存フォルダーを確認できません: {e}"))?.file_type().is_symlink() {
                return Err("既存翻訳がシンボリックリンクです。手動で確認してください".into());
            }
            fs::rename(&destination, &backup).map_err(|e| format!("既存フォルダーを退避できません: {e}"))?;
        }
        if let Err(err) = fs::rename(&temp_dir, &destination) {
            if had_destination { let _ = fs::rename(&backup, &destination); }
            return Err(format!("翻訳の配置に失敗しました: {err}"));
        }
        if had_destination { let _ = fs::remove_dir_all(&backup); }
        emit(96, "旧版を整理中", "以前の翻訳バージョンを自動で削除しています");
        let cleanup_failures = remove_old_versions(&translations, &release.tag);
        for failure in &cleanup_failures {
            emit(97, "旧版の整理", format!("旧版の削除に失敗: {failure}"));
        }
        let language_status = language::activate_japanese(&game, &release.tag);
        emit(99, "言語設定", &language_status);
        Ok(InstallSummary {
            version: release.tag,
            installed_path: destination.to_string_lossy().to_string(),
            file_count,
            bytes_downloaded: data.len(),
            language_status,
        })
    })();
    if temp_dir.is_dir() { let _ = fs::remove_dir_all(temp_dir); }
    result
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn automatic_cleanup_removes_old_jp_versions_only() {
        let root = std::env::temp_dir().join(format!("scpsl-cleanup-{}", std::process::id()));
        let old = root.join("JP-Ver13.8");
        let current = root.join("JP-Ver14.2Fix");
        let other = root.join("English (default)");
        for path in [&old, &current, &other] {
            fs::create_dir_all(path).unwrap();
            fs::write(path.join("example.txt"), b"text").unwrap();
        }
        assert!(remove_old_versions(&root, "JP-Ver14.2Fix").is_empty());
        assert!(!old.exists());
        assert!(current.exists());
        assert!(other.exists());
        fs::remove_dir_all(root).unwrap();
    }
    #[test]
    fn actual_release_zip_contains_every_file() {
        let Ok(path) = std::env::var("SCPSL_REAL_ZIP") else { return; };
        let data = fs::read(path).unwrap();
        let mut original = zip::ZipArchive::new(Cursor::new(&data)).unwrap();
        let original_count = original.len();
        let temp = std::env::temp_dir().join(format!("scpsl-full-release-{}", std::process::id()));
        fs::create_dir_all(&temp).unwrap();
        assert_eq!(extract_verified_zip(&data, &temp).unwrap(), original_count);
        for idx in 0..original_count {
            let mut entry = original.by_index(idx).unwrap();
            if entry.is_dir() { continue; }
            let mut bytes = Vec::new();
            io::copy(&mut entry, &mut bytes).unwrap();
            assert_eq!(fs::read(temp.join(entry.enclosed_name().unwrap())).unwrap(), bytes);
        }
        assert!(temp.join("manifest.json").is_file());
        fs::remove_dir_all(temp).unwrap();
    }
    #[test]
    fn extracts_all_files_including_manifest_json() {
        use std::io::Write as _;
        let cursor = Cursor::new(Vec::<u8>::new());
        let mut zip = zip::ZipWriter::new(cursor);
        for i in 0..10 {
            zip.start_file(format!("Sample{i}.txt"), zip::write::SimpleFileOptions::default()).unwrap();
            zip.write_all(b"some text").unwrap();
        }
        zip.start_file("manifest.json", zip::write::SimpleFileOptions::default()).unwrap();
        zip.write_all(r#"{"Name":"日本語"}"#.as_bytes()).unwrap();
        let data = zip.finish().unwrap().into_inner();
        let dir = std::env::temp_dir().join(format!("scpsl-all-files-{}", std::process::id()));
        fs::create_dir_all(&dir).unwrap();
        assert_eq!(extract_verified_zip(&data, &dir).unwrap(), 11);
        assert_eq!(fs::read_to_string(dir.join("manifest.json")).unwrap(), r#"{"Name":"日本語"}"#);
        let _ = fs::remove_dir_all(dir);
    }
    #[test]
    fn directory_with_no_files_is_empty() {
        let temp = std::env::temp_dir().join(format!("scpsl-test-{}", std::process::id()));
        fs::create_dir_all(&temp).unwrap();
        assert_eq!(count_txt(&temp), 0);
        let _ = fs::remove_dir_all(temp);
    }
}