//! Secure extraction of the application's bundled, version-pinned Microsoft
//! Windows App SDK native runtime assets. Never used for arbitrary network data.
use std::fs::{self, File};
use std::io::{self, BufReader};
use std::path::Path;
use zip::ZipArchive;

pub fn unpack(archive_path: &Path, target: &Path) -> Result<usize,String> {
    let zip_file = File::open(archive_path)
        .map_err(|e| format!("ランタイムアーカイブを開けません: {e}"))?;
    let mut archive = ZipArchive::new(BufReader::new(zip_file))
        .map_err(|e| format!("ランタイムアーカイブが不正です: {e}"))?;
    if archive.is_empty() || archive.len() > 300 {
        return Err("ランタイムのファイル数が不正です".into());
    }
    fs::create_dir_all(target)
        .map_err(|e| format!("ランタイムの一時展開先が使えません: {e}"))?;
    let mut total: u64 = 0;
    let mut extracted=0;
    for i in 0..archive.len() {
        let mut item = archive.by_index(i)
            .map_err(|e| format!("ランタイムエントリーを読めません: {e}"))?;
        if let Some(mode) = item.unix_mode() {
            if mode & 0o170000 == 0o120000 {
                return Err("アーカイブにシンボリックリンクがあります".into());
            }
        }
        let safe_name = item.enclosed_name().ok_or("アーカイブに不正なパスがあります")?;
        if safe_name.as_os_str().len() > 260 {
            return Err("ランタイムのファイル名が長すぎます".into());
        }
        if item.size() > 40*1024*1024 {
            return Err("ランタイムのファイルサイズが不正です".into());
        }
        total=total.checked_add(item.size())
            .ok_or("ランタイムの展開サイズが大きすぎます")?;
        if total>110*1024*1024 {
            return Err("ランタイムの展開サイズが大きすぎます".into());
        }
        let path = target.join(safe_name);
        if item.is_dir(){
            fs::create_dir_all(&path)
                .map_err(|e| format!("ランタイムのフォルダーを作成できません: {e}"))?;
            continue;
        }
        if let Some(parent)=path.parent(){
            fs::create_dir_all(parent)
                .map_err(|e| format!("ランタイムのフォルダーを作成できません: {e}"))?;
        }
        let mut output=File::create(&path)
            .map_err(|e| format!("ランタイムファイルを作成できません: {e}"))?;
        let written=io::copy(&mut item,&mut output)
            .map_err(|e| format!("ランタイムを展開できません: {e}"))?;
        if written!=item.size(){
            return Err("ランタイム展開結果が不完全です".into());
        }
        extracted+=1;
    }
    Ok(extracted)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn extract_small_bundled_archive(){
        use std::io::Write;
        let d=std::env::temp_dir().join(format!("scpsl-bundle-test-{}",std::process::id()));
        fs::create_dir_all(&d).unwrap();
        let src=d.join("test.zip");
        let mut writer=zip::ZipWriter::new(File::create(&src).unwrap());
        writer.start_file("folder/test.txt",zip::write::SimpleFileOptions::default()).unwrap();
        writer.write_all(b"test data").unwrap();
        writer.finish().unwrap();
        assert_eq!(unpack(&src,&d.join("out")).unwrap(),1);
        assert_eq!(fs::read(d.join("out/folder/test.txt")).unwrap(),b"test data");
        fs::remove_dir_all(d).unwrap();
    }
    #[test]
    fn rejects_empty_archive(){
        let d=std::env::temp_dir().join(format!("scpsl-bundle-empty-{}",std::process::id()));
        fs::create_dir_all(&d).unwrap();
        let src=d.join("empty.zip");
        zip::ZipWriter::new(File::create(&src).unwrap()).finish().unwrap();
        assert!(unpack(&src,&d.join("out")).is_err());
        fs::remove_dir_all(d).unwrap();
    }
}
