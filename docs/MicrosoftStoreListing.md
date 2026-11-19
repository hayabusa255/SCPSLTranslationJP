# Microsoft Store listing draft (not published)

This is an **unpublished draft** for Partner Center. Do not claim the app is available in Microsoft Store until it has passed certification.

## Product

- Reserved app name: SCPSLTranslationJP (reserve this name in Partner Center first)
- Product type: MSIX or PWA app
- Suggested category: Utilities & tools
- Price: Free
- Platform: Windows 11 x64
- Privacy policy: https://github.com/hayabusa255/SCPSLTranslationJP/blob/main/docs/PRIVACY_POLICY.md
- Support: https://github.com/hayabusa255/SCPSLTranslationJP/issues
- Website: https://github.com/hayabusa255/SCPSLTranslationJP
- Download/build information: https://github.com/hayabusa255/SCPSLTranslationJP/blob/main/NativeStoreMSIX/README.md

## Short description (Japanese)

SCP: Secret Laboratory向けの非公式日本語翻訳を、Windowsから簡単に導入・更新できる無料ツールです。

## Full description (Japanese)

SCPSLTranslationJPは、SCP: Secret Laboratoryを日本語で楽しみたいプレイヤー向けの、コミュニティ運営による非公式日本語翻訳プロジェクトです。

このアプリでは、Steamにインストールされたゲームの保存先を検出し、公開されている翻訳データをダウンロードして導入できます。ゲームが自動検出されない場合は、インストール先を手動で選択できます。公開中の安定版・ベータ版の履歴を確認し、翻訳バージョンを切り替えることもできます。

翻訳ファイルはGitHub Releasesから取得します。利用にはインターネット接続とSCP: Secret Laboratory本体が必要です。ゲーム本体は付属していません。

本アプリは無料・オープンソースです。翻訳データはCC BY 4.0、アプリのソースコードはMITライセンスに基づいて公開されています。

**非公式ツールです。SCP: Secret Laboratoryの開発元・販売元による公式アプリではありません。**

## Short description (English)

An unofficial, open-source Windows utility for installing and updating Japanese translations for SCP: Secret Laboratory.

## Full description (English)

SCPSLTranslationJP is a community-maintained, unofficial Japanese localization project for SCP: Secret Laboratory.

The Windows utility detects Steam installations, downloads publicly released Japanese translation files, and installs them into the game's translation directory. Users can choose the game folder manually when automatic detection is unavailable, review translation release history, and switch between supported stable and beta versions.

The game itself is not included. An installed copy of SCP: Secret Laboratory and an internet connection are required to download translations from GitHub Releases.

The application is free and open source. The installer source code is under the MIT license; translation files are distributed under CC BY 4.0.

**This is an unofficial community utility and is not affiliated with or endorsed by the developers or publishers of SCP: Secret Laboratory.**

## Submission checklist

- Reserve the exact app name and copy Product identity Name, Publisher and PublisherDisplayName.
- Produce the Store-identity MSIX with the dedicated GitHub Actions workflow.
- Test install, launch, game detection, translation install, and removal on Windows 11.
- Run the Windows App Certification Kit.
- Upload screenshots captured from the actual app (do not fabricate Store screenshots).
- Provide the privacy-policy URL, support URL, required age rating, and a contact email in Partner Center.
- Review the final listing for game trademarks, community logos, translation licensing, and any third-party assets.
- Submit for certification. Only after certification can the Store distribute its Microsoft-signed MSIX.

## Important caveats

The current standalone SCPSL.exe on GitHub is unsigned and is **not** signed by Microsoft Store. The Store package is a separate MSIX build. Users who cannot run the standalone EXE because of Smart App Control should use the published Store package once approved, or manually install the translation ZIP.
