use std::{fs, path::{Path, PathBuf}};
#[cfg(target_os = "windows")]
use std::{env, process::Command};

const KEY: &str = "translation_path::-%(|::";

// SCP:SL persists the chosen translation folder in registry.txt.
// Do not edit while the game is running, since it can overwrite settings on exit.
fn game_running() -> bool {
    #[cfg(target_os = "windows")]
    {
        let mut command = Command::new("tasklist.exe");
        command.args(["/FI", "IMAGENAME eq SCPSL.exe", "/FO", "CSV", "/NH"]);
        // Our app is a GUI subsystem executable. Suppress the child console window too.
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x08000000;
        command.creation_flags(CREATE_NO_WINDOW);
        if let Ok(result) = command.output() {
            let output = String::from_utf8_lossy(&result.stdout).to_lowercase();
            return output.lines().any(|line| line.starts_with("\"scpsl.exe\","));
        }
        // If we cannot query processes, be conservative.
        true
    }
    #[cfg(target_os = "linux")]
    {
        if let Ok(procs) = fs::read_dir("/proc") {
            for proc in procs.flatten() {
                if !proc.file_name().to_string_lossy().bytes().all(|b| b.is_ascii_digit()) { continue; }
                if let Ok(name) = fs::read_to_string(proc.path().join("comm")) {
                    let name = name.trim().to_ascii_lowercase();
                    if name == "scpsl.exe" || name == "scpsl.x86_64" { return true; }
                }
            }
        }
        false
    }
    #[cfg(not(any(target_os = "windows", target_os = "linux")))]
    { true }
}

fn registry_candidates(game: &Path) -> Vec<PathBuf> {
    let mut result = Vec::new();
    #[cfg(target_os = "windows")]
    if let Some(appdata) = env::var_os("APPDATA") {
        result.push(PathBuf::from(appdata).join("SCP Secret Laboratory").join("registry.txt"));
    }
    #[cfg(target_os = "linux")]
    {
        // Steam install: <library>/steamapps/common/SCP Secret Laboratory
        // Proton settings: <library>/steamapps/compatdata/700330/pfx/drive_c/users/steamuser/AppData/Roaming/...
        let steamapps = game.parent().and_then(|p| p.parent());
        if let Some(steamapps) = steamapps {
            result.push(steamapps.join("compatdata/700330/pfx/drive_c/users/steamuser/AppData/Roaming/SCP Secret Laboratory/registry.txt"));
        }
        if let Some(home) = dirs::home_dir() {
            for prefix in [".local/share/Steam", ".steam/steam", ".steam/root",
                ".var/app/com.valvesoftware.Steam/data/Steam"] {
                result.push(home.join(prefix).join("steamapps/compatdata/700330/pfx/drive_c/users/steamuser/AppData/Roaming/SCP Secret Laboratory/registry.txt"));
            }
        }
    }
    result
}

fn update_registry_file(registry: &Path, version: &str) -> Result<bool, String> {
    if !crate::github::valid_tag(version) { return Err("翻訳タグの形式が不正です".into()); }
    let raw = fs::read_to_string(registry).map_err(|e| format!("言語設定を読み取れません: {e}"))?;
    let newline = if raw.contains("\r\n") { "\r\n" } else { "\n" };
    let mut out = String::with_capacity(raw.len() + 64);
    let mut found = false;
    for line in raw.split_inclusive('\n') {
        let trimmed = line.trim_end_matches(['\r', '\n']);
        let suffix = &line[trimmed.len()..];
        if let Some(after_prefix) = trimmed.get(2..) {
            if after_prefix.starts_with(KEY) {
                if !found {
                    out.push_str(&trimmed[..2]);
                    out.push_str(KEY);
                    out.push_str(version);
                    out.push_str(suffix);
                }
                found = true;
                continue;
            }
        }
        out.push_str(line);
    }
    if !found {
        if !out.is_empty() && !out.ends_with('\n') { out.push_str(newline); }
        out.push_str("13");
        out.push_str(KEY);
        out.push_str(version);
        out.push_str(newline);
    }
    if out == raw { return Ok(false); }
    // Preserve the first backup rather than overwriting it with a modified registry.
    let backup = registry.with_extension("txt.scpsljp.bak");
    if !backup.exists() {
        fs::copy(registry, &backup).map_err(|e| format!("言語設定のバックアップを作成できません: {e}"))?;
    }
    let temporary = registry.with_extension(format!("txt.scpsljp-{}.tmp", std::process::id()));
    fs::write(&temporary, out.as_bytes()).map_err(|e| format!("一時設定ファイルを保存できません: {e}"))?;
    if let Err(e) = fs::rename(&temporary, registry) {
        let _ = fs::remove_file(&temporary);
        return Err(format!("言語設定を書き換えられません: {e}"));
    }
    Ok(true)
}

pub fn activate_japanese(game: &Path, version: &str) -> String {
    if game_running() {
        return "ゲーム実行中のため言語の自動変更を見送りました。ゲームを終了し再度実行してください".into();
    }
    let registry = registry_candidates(game).into_iter().find(|p| p.is_file());
    let Some(registry) = registry else {
        return "ゲーム設定 registry.txt が見つからないため言語は未変更です。一度ゲームを起動し設定を生成してください".into();
    };
    match update_registry_file(&registry, version) {
        Ok(true) => format!("日本語に自動設定しました（{}）", registry.display()),
        Ok(false) => "すでに日本語に設定されています".into(),
        Err(e) => format!("翻訳は導入済みですが、言語の自動設定は失敗しました: {e}"),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    fn tmp(name: &str) -> PathBuf {
        std::env::temp_dir().join(format!("scpsl-lang-{}-{}-{name}", std::process::id(),
            std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()))
    }
    #[test]
    fn changes_only_language_and_preserves_crlf() {
        let path=tmp("settings.txt");
        fs::write(&path, "07AudioSettings_Effects::-%(|::42\r\n13translation_path::-%(|::English (default)\r\n00y_invert::-%(|::false\r\n").unwrap();
        assert!(update_registry_file(&path, "JP-Ver14.2Fix").unwrap());
        assert_eq!(fs::read_to_string(&path).unwrap(), "07AudioSettings_Effects::-%(|::42\r\n13translation_path::-%(|::JP-Ver14.2Fix\r\n00y_invert::-%(|::false\r\n");
        assert!(!update_registry_file(&path, "JP-Ver14.2Fix").unwrap());
        fs::remove_file(&path).unwrap();
        fs::remove_file(path.with_extension("txt.scpsljp.bak")).unwrap();
    }
    #[test]
    fn inserts_key_when_missing() {
        let path=tmp("missing.txt");
        fs::write(&path, "07SomeFlag::-%(|::true\n").unwrap();
        assert!(update_registry_file(&path, "JP-Ver14.2Fix").unwrap());
        assert!(fs::read_to_string(&path).unwrap().ends_with("13translation_path::-%(|::JP-Ver14.2Fix\n"));
        fs::remove_file(&path).unwrap();
        fs::remove_file(path.with_extension("txt.scpsljp.bak")).unwrap();
    }
}