use regex::Regex;
use serde::Serialize;
use std::{collections::HashSet, fs, path::{Path, PathBuf}};

const APP_ID: &str = "700330";
const DEFAULT_GAME: &str = "SCP Secret Laboratory";

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct GameCandidate {
    pub path: String,
    pub source: String,
}

fn clean_path(path: PathBuf) -> PathBuf {
    path.canonicalize().unwrap_or(path)
}

pub fn validate_game(path: &Path) -> bool {
    path.is_dir() && (path.join("SCPSL.exe").exists()
        || path.join("SCPSL.x86_64").exists()
        || path.join("SCPSL_Data").is_dir()
        || (path.file_name().and_then(|f| f.to_str()) == Some(DEFAULT_GAME)
            && path.join("Translations").is_dir()))
}

pub fn validate_manual(path: String) -> Result<GameCandidate, String> {
    let raw = PathBuf::from(path);
    if !validate_game(&raw) {
        return Err("選択されたフォルダーに SCP: Secret Laboratory が見つかりません。SCPSL.exe または SCPSL_Data を含むゲームフォルダーを選択してください。".into());
    }
    Ok(GameCandidate {
        path: clean_path(raw).to_string_lossy().to_string(),
        source: "手動選択".into(),
    })
}

fn steam_roots() -> Vec<PathBuf> {
    let mut roots = Vec::new();
    #[cfg(target_os = "windows")]
    {
        use winreg::enums::{HKEY_CURRENT_USER, HKEY_LOCAL_MACHINE};
        use winreg::RegKey;
        for hive in [HKEY_CURRENT_USER, HKEY_LOCAL_MACHINE] {
            let key = RegKey::predef(hive);
            for key_name in [r"SOFTWARE\Valve\Steam", r"SOFTWARE\WOW6432Node\Valve\Steam"] {
                if let Ok(subkey) = key.open_subkey(key_name) {
                    for value_name in ["SteamPath", "InstallPath"] {
                        if let Ok(p) = subkey.get_value::<String, _>(value_name) {
                            roots.push(PathBuf::from(p));
                        }
                    }
                }
            }
        }
        for env in ["ProgramFiles(x86)", "ProgramFiles", "LOCALAPPDATA"] {
            if let Ok(value) = std::env::var(env) { roots.push(PathBuf::from(value).join("Steam")); }
        }
        for disk in ['C', 'D', 'E', 'F'] { roots.push(PathBuf::from(format!("{disk}:\\Steam"))); }
    }
    #[cfg(not(target_os = "windows"))]
    {
        if let Some(home) = dirs::home_dir() {
            for relative in [
                ".local/share/Steam", ".steam/steam", ".steam/root", 
                ".var/app/com.valvesoftware.Steam/data/Steam",
                "snap/steam/common/.local/share/Steam",
                "snap/steam/common/.steam/steam",
            ] { roots.push(home.join(relative)); }
        }
    }
    roots
}

// Steam's KeyValues1 format stores library directories as escaped quoted strings.
// Restrict to a key called "path" or old-style numbered entries, not arbitrary VDF values.
fn parse_library_paths(vdf: &str) -> Vec<String> {
    let path_re = Regex::new(r#"(?i)"path"\s+"([^"\r\n]+)""#).expect("valid regex");
    let legacy_re = Regex::new(r#"(?m)^\s*"\d+"\s+"([^"\r\n]+)"\s*$"#).expect("valid regex");
    path_re.captures_iter(vdf).chain(legacy_re.captures_iter(vdf))
        .map(|m| m[1].replace("\\\\", "\\"))
        .collect()
}

fn game_name_from_manifest(manifest: &Path) -> Option<String> {
    let raw = fs::read_to_string(manifest).ok()?;
    let re = Regex::new(r#"(?i)"installdir"\s+"([^"\r\n]+)""#).ok()?;
    let value = re.captures(&raw)?[1].to_string();
    // Never let game install directory metadata redirect outside steamapps/common.
    if value.is_empty() || value == "." || value == ".." || value.contains('/') || value.contains('\\') { return None; }
    Some(value)
}

fn steam_libraries(root: &Path) -> Vec<PathBuf> {
    let mut libraries = vec![root.to_path_buf()];
    let vdf = root.join("steamapps").join("libraryfolders.vdf");
    if let Ok(data) = fs::read_to_string(vdf) {
        for path in parse_library_paths(&data) { libraries.push(PathBuf::from(path)); }
    }
    libraries
}

pub fn detect_games() -> Vec<GameCandidate> {
    let mut games = Vec::new();
    let mut visited_libraries = HashSet::new();
    let mut visited_games = HashSet::new();
    for root in steam_roots() {
        if !root.is_dir() { continue; }
        for library in steam_libraries(&root) {
            let library = clean_path(library);
            if !visited_libraries.insert(library.clone()) { continue; }
            let manifest = library.join("steamapps").join(format!("appmanifest_{APP_ID}.acf"));
            if !manifest.is_file() { continue; }
            let installed_name = game_name_from_manifest(&manifest).unwrap_or_else(|| DEFAULT_GAME.to_owned());
            let game = clean_path(library.join("steamapps").join("common").join(installed_name));
            if validate_game(&game) && visited_games.insert(game.clone()) {
                games.push(GameCandidate {
                    path: game.to_string_lossy().to_string(),
                    source: "Steam ライブラリを検出".into(),
                });
            }
        }
    }
    games
}

pub fn listed_versions(game_dir: &Path) -> Vec<String> {
    let mut list = Vec::new();
    let base = game_dir.join("Translations");
    if let Ok(entries) = fs::read_dir(base) {
        for entry in entries.flatten() {
            let Ok(meta) = entry.file_type() else { continue; };
            if !meta.is_dir() || meta.is_symlink() { continue; }
            let name = entry.file_name().to_string_lossy().to_string();
            if is_translation_name(&name)
                && fs::read_dir(entry.path()).ok().is_some_and(|mut f| f.any(|e| e.ok().is_some_and(|x| x.path().extension().is_some_and(|ext| ext.eq_ignore_ascii_case("txt"))))) {
                list.push(name);
            }
        }
    }
    list.sort();
    list.reverse();
    list
}

pub fn is_translation_name(name: &str) -> bool {
    name.starts_with("JP-Ver") && name.len() <= 90 && name.len() > 6
        && name[6..].bytes().all(|b| b.is_ascii_alphanumeric() || b"._-".contains(&b))
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn finds_steam_libraries() {
        let s = r#""libraryfolders" { "0" { "path" "C:\\Program Files (x86)\\Steam" } "1" { "path" "D:\\Library" } }"#;
        let paths = parse_library_paths(s);
        assert_eq!(paths.len(), 2);
        assert!(paths[1].contains("D:\\Library"));
    }
    #[test]
    fn rejects_unsafe_names() {
        assert!(is_translation_name("JP-Ver14.2Fix"));
        assert!(!is_translation_name("JP-../../tmp"));
        assert!(!is_translation_name("JP-Ver../\\evil"));
    }
}