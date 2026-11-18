use serde::{Deserialize, Serialize};
use std::time::Duration;

const API: &str = "https://api.github.com/repos/hayabusa255/SCPSLTranslationJP/releases/latest";
const MAX_DOWNLOAD_BYTES: usize = 20 * 1024 * 1024;
const HISTORY_API: &str = "https://api.github.com/repos/hayabusa255/SCPSLTranslationJP/releases?per_page=30";
const MAX_HISTORY_BYTES: usize = 2 * 1024 * 1024;

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ReleaseInfo {
    pub tag: String,
    pub download_url: String,
    pub published_at: Option<String>,
}

#[derive(Deserialize)]
struct GithubAsset {
    name: String,
    browser_download_url: String,
}
#[derive(Deserialize)]
struct GithubRelease {
    tag_name: String,
    name: Option<String>,
    body: Option<String>,
    published_at: Option<String>,
    #[serde(default)]
    prerelease: bool,
    #[serde(default)]
    draft: bool,
    assets: Vec<GithubAsset>,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ReleaseNote {
    pub tag: String,
    pub title: String,
    pub published_at: Option<String>,
    pub summary: String,
    pub notes: String,
    pub is_beta: bool,
}

fn client() -> Result<reqwest::Client, String> {
    reqwest::Client::builder()
        .user_agent("SCPSLTranslationJP-Installer/2.0 (+https://github.com/hayabusa255/SCPSLTranslationJP)")
        .timeout(Duration::from_secs(45))
        .https_only(true)
        .build()
        .map_err(|e| format!("HTTPクライアントを初期化できません: {e}"))
}

fn beta_release(tag: &str, title: &str, prerelease: bool) -> bool {
    let tag_lower=tag.to_ascii_lowercase();
    prerelease || tag_lower.contains("beta") || tag_lower.contains("preview")
        || tag_lower.contains("alpha") || title.contains('β')
}

fn extract_release_info(release: GithubRelease) -> Result<ReleaseInfo, String> {
    if release.draft { return Err("下書きリリースはインストールできません".into()); }
    let tag = release.tag_name;
    if !valid_tag(&tag) { return Err("リリースタグが想定外の形式です".into()); }
    let desired = format!("{tag}.zip");
    let asset = release.assets.into_iter().find(|a| a.name == desired)
        .ok_or_else(|| format!("リリース {tag} に翻訳ZIPがありません"))?;
    let url = reqwest::Url::parse(&asset.browser_download_url)
        .map_err(|_| "ダウンロードURLが不正です")?;
    let expected = format!("/hayabusa255/SCPSLTranslationJP/releases/download/{tag}/{tag}.zip");
    if url.scheme() != "https" || url.host_str() != Some("github.com")
        || url.path() != expected || url.query().is_some() || url.fragment().is_some() {
        return Err("予期しないダウンロード先のため停止しました".into());
    }
    Ok(ReleaseInfo { tag, download_url: asset.browser_download_url,
                     published_at: release.published_at })
}

pub async fn latest() -> Result<ReleaseInfo, String> {
    let response = client()?.get(API).header("Accept", "application/vnd.github+json")
        .send().await.map_err(|e| format!("GitHubとの通信に失敗しました: {e}"))?
        .error_for_status().map_err(|e| format!("GitHubの応答エラー: {e}"))?;
    let release: GithubRelease = response.json().await
        .map_err(|e| format!("最新版の情報を読み取れません: {e}"))?;
    extract_release_info(release)
}

/// Select exactly one release from the trusted translation project's GitHub tags.
/// A user-controlled URL is never accepted.
pub async fn by_tag(tag: &str) -> Result<ReleaseInfo,String> {
    if !valid_tag(tag) { return Err("インストールする翻訳のタグが不正です".into()); }
    let url=format!("https://api.github.com/repos/hayabusa255/SCPSLTranslationJP/releases/tags/{tag}");
    let response=client()?.get(&url).header("Accept","application/vnd.github+json")
        .send().await.map_err(|e| format!("指定したバージョンを取得できません: {e}"))?
        .error_for_status().map_err(|e| format!("指定したバージョンの取得に失敗しました: {e}"))?;
    let release: GithubRelease=response.json().await
        .map_err(|e| format!("リリース情報を解析できません: {e}"))?;
    if release.tag_name!=tag { return Err("GitHubのリリースタグが一致しません".into()); }
    extract_release_info(release)
}

// Return only the update-information section. Installation manuals repeat across
// releases and do not belong in the Windows Update-style change history.
fn changelog_section(body: &str) -> String {
    let normalized = body.replace("\r\n", "\n");
    let end = normalized.find("\n## インストール方法")
        .or_else(|| normalized.find("\n## インストーラーを使用するインストール方法"))
        .unwrap_or(normalized.len());
    normalized[..end].trim().chars().take(24_000).collect()
}

fn changelog_summary(body: &str) -> String {
    let section = changelog_section(body);
    section.lines()
        .map(str::trim)
        .filter(|line| !line.is_empty()
            && !line.starts_with('#')
            && !line.to_lowercase().contains("full changelog")
            && !line.starts_with("変更点") && !line.starts_with("What's Changed")
            && !line.starts_with("New Contributors"))
        .map(|line| line.trim_start_matches(['*', '-', '・', ' ']))
        .find(|line| !line.is_empty())
        .unwrap_or("リリースノートをご確認ください")
        .chars().take(180).collect()
}

// Always retrieve release notes from GitHub; no bundled or hard-coded changelog.
// No user-provided URLs or tokens are accepted.
pub async fn release_history() -> Result<Vec<ReleaseNote>, String> {
    let response = client()?.get(HISTORY_API)
        .header("Accept", "application/vnd.github+json")
        .send().await.map_err(|e| format!("更新履歴の取得に失敗しました: {e}"))?
        .error_for_status().map_err(|e| format!("GitHubの応答エラー: {e}"))?;
    if response.content_length().is_some_and(|n| n > MAX_HISTORY_BYTES as u64) {
        return Err("更新履歴の応答が大きすぎます".into());
    }
    let data = response.bytes().await.map_err(|e| format!("更新履歴を読み取れません: {e}"))?;
    if data.len() > MAX_HISTORY_BYTES { return Err("更新履歴の応答が大きすぎます".into()); }
    let entries: Vec<GithubRelease> = serde_json::from_slice(&data)
        .map_err(|e| format!("GitHubの更新履歴を解析できません: {e}"))?;
    Ok(entries.into_iter()
        .filter(|release| !release.draft && valid_tag(&release.tag_name)
            && release.assets.iter().any(|a| a.name == format!("{}.zip", release.tag_name)))
        .take(30)
        .map(|release| {
            let is_beta=beta_release(&release.tag_name,
                release.name.as_deref().unwrap_or(""),release.prerelease);
            let body = release.body.unwrap_or_default();
            ReleaseNote {
                tag: release.tag_name.clone(),
                title: release.name.filter(|n| !n.trim().is_empty()).unwrap_or(release.tag_name),
                published_at: release.published_at,
                summary: changelog_summary(&body),
                notes: changelog_section(&body),
                is_beta,
            }
        }).collect())
}

pub fn valid_tag(tag: &str) -> bool {
    tag.starts_with("JP-") && tag.len() > 3 && tag.len() <= 90
        && tag[3..].bytes().all(|c| c.is_ascii_alphanumeric() || b"._-".contains(&c))
}

pub async fn download<F>(url: &str, progress: F) -> Result<Vec<u8>, String>
where F: Fn(usize, Option<usize>) {
    use futures_util::StreamExt;
    let response = client()?.get(url).send().await
        .map_err(|e| format!("ダウンロードに失敗しました: {e}"))?
        .error_for_status().map_err(|e| format!("ダウンロード先がエラーを返しました: {e}"))?;
    if let Some(size) = response.content_length() {
        if size > MAX_DOWNLOAD_BYTES as u64 { return Err("翻訳ZIPのサイズが上限（20MiB）を超えています".into()); }
    }
    let total = response.content_length().map(|s| s as usize);
    let mut data = Vec::with_capacity(total.unwrap_or(256 * 1024).min(MAX_DOWNLOAD_BYTES));
    let mut stream = response.bytes_stream();
    while let Some(chunk) = stream.next().await {
        let chunk = chunk.map_err(|e| format!("ダウンロードが中断されました: {e}"))?;
        if data.len() + chunk.len() > MAX_DOWNLOAD_BYTES {
            return Err("ダウンロードが20MiBを超えたため停止しました".into());
        }
        data.extend_from_slice(&chunk);
        progress(data.len(), total);
    }
    if data.len() < 4 || &data[..2] != b"PK" { return Err("取得ファイルがZIP形式ではありません".into()); }
    Ok(data)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn extracts_release_changes_without_install_guide() {
        let notes = "変更点-\r\n字幕翻訳が最新版と異なっていた点を修正\r\n\r\n## インストール方法\n長い手順";
        assert_eq!(changelog_section(notes), "変更点-\n字幕翻訳が最新版と異なっていた点を修正");
        assert_eq!(changelog_summary(notes), "字幕翻訳が最新版と異なっていた点を修正");
    }

    #[test]
    fn beta_detection_uses_release_flag_and_legacy_names() {
        assert!(beta_release("JP-Ver14.0Beta", "", false));
        assert!(beta_release("JP-Ver14.2", "ベータ版 β", false));
        assert!(beta_release("JP-Ver14.2", "Pre-release", true));
        assert!(!beta_release("JP-Ver14.2Fix", "安定版", false));
    }

    #[test]
    fn selected_release_cannot_download_arbitrary_asset() {
        let release=GithubRelease {
            tag_name:"JP-Ver14.2Fix".into(),name:None,body:None,
            published_at:None,prerelease:false,draft:false,
            assets:vec![GithubAsset {
                name:"JP-Ver14.2Fix.zip".into(),
                browser_download_url:"https://example.com/evil.zip".into()
            }]
        };
        assert!(extract_release_info(release).is_err());
    }

    #[tokio::test]
    async fn invalid_tag_rejected_without_network() {
        assert!(by_tag("JP-../fake").await.is_err());
    }

    #[test]
    fn validates_release_tags() {
        assert!(valid_tag("JP-Ver14.2Fix"));
        assert!(!valid_tag("/tmp/file"));
        assert!(!valid_tag("JP-../foo"));
    }
}
