mod github;
mod installer;
mod language;
mod steam;
mod runtime_archive;

use serde_json::{json, Value};
use std::env;
use std::path::PathBuf;

fn result(payload: Value) {
    println!("{}", json!({"kind":"result","data":payload}));
}
fn error(message: String) -> ! {
    eprintln!("{}", message);
    println!("{}", json!({"kind":"error","message":message}));
    std::process::exit(1);
}

#[tokio::main]
async fn main() {
    let mut args = env::args().skip(1);
    let command = args.next().unwrap_or_default();
    let directory = args.next();
    if command == "runtime-extract" {
        let archive = directory.unwrap_or_else(|| error("アーカイブを指定してください".into()));
        let target = args.next().unwrap_or_else(|| error("展開先を指定してください".into()));
        if args.next().is_some() { error("余分な引数があります".into()); }
        match runtime_archive::unpack(&PathBuf::from(archive), &PathBuf::from(target)) {
            Ok(count) => result(json!({"filesExtracted":count})),
            Err(err) => error(err),
        }
        return;
    }
    if command == "resolve-tag" {
        let tag = directory.unwrap_or_else(|| error("翻訳バージョンを指定してください".into()));
        if args.next().is_some() { error("予期しない引数があります".into()); }
        match github::by_tag(&tag).await {
            Ok(release) => result(json!(release)),
            Err(err) => error(err),
        }
        return;
    }
    if command == "install-tag" {
        let path = directory.unwrap_or_else(|| error("ゲームの場所を指定してください".into()));
        let tag = args.next().unwrap_or_else(|| error("翻訳バージョンを指定してください".into()));
        if args.next().is_some(){error("予期しない引数があります".into());}
        match installer::install_selected(path,Some(tag)).await {
            Ok(summary) => result(json!(summary)),
            Err(err) => error(err),
        }
        return;
    }
    if args.next().is_some() {
        error("予期しない引数があります".into());
    }
    match command.as_str() {
        "version" => result(json!({"version":env!("CARGO_PKG_VERSION")})),
        "detect" => result(json!(steam::detect_games())),
        "validate" => {
            let path = directory.unwrap_or_else(|| error("ゲームの場所を指定してください".into()));
            let candidate = steam::validate_manual(path).unwrap_or_else(|e| error(e));
            result(json!(candidate));
        }
        "inspect" => {
            let path = directory.unwrap_or_else(|| error("ゲームの場所を指定してください".into()));
            let candidate = steam::validate_manual(path).unwrap_or_else(|e| error(e));
            let versions = steam::listed_versions(&PathBuf::from(candidate.path));
            result(json!({"installedVersions":versions}));
        }
        "latest" => match github::latest().await {
            Ok(release) => result(json!(release)),
            Err(err) => error(err),
        },
        "history" => match github::release_history().await {
            Ok(releases) => result(json!(releases)),
            Err(err) => error(err),
        },
        "install" => {
            let path = directory.unwrap_or_else(|| error("ゲームの場所を指定してください".into()));
            match installer::install(path).await {
                Ok(summary) => result(json!(summary)),
                Err(err) => error(err),
            }
        }
        _ => error("サポートされないコマンドです".into()),
    }
}
