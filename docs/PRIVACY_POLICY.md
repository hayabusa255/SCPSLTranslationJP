# Privacy policy — SCPSLTranslationJP Native Installer

Last updated: 2026-11-19

## What this app does
SCPSLTranslationJP distributes Japanese language files for the game SCP: Secret Laboratory. Its Windows installer reads local Steam game installation locations, downloads the language files selected by the user, and places them inside the game's Translations directory.

## Network requests
At application startup, the installer automatically queries GitHub Releases for the latest stable version and release history. When a user chooses a version to install, it downloads that release's translation ZIP from GitHub. Clicking a GitHub or documentation link opens that website in the user's browser. Requests to GitHub and its download infrastructure reveal ordinary network metadata, such as the user's IP address and HTTP request information, to GitHub. They do not intentionally include the user's game directory path, personal files, or Steam credentials.

We do not operate a telemetry server, require an application account, collect analytics or advertising identifiers, or automatically upload logs, game files, Steam IDs, or user-created content. The application does not have a setting to suppress its automatic release-information requests; users who do not want these requests can use the downloadable translation ZIP manually without running the installer.

GitHub's handling of requests is governed by the [GitHub Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).

## Local files and settings
The installer reads Steam installation manifests and local game settings to find and manage language files. It creates or updates translation folders under the selected game's Translations directory. When a compatible registry.txt setting exists, it may update the language preference; a backup is kept where supported. The Windows installer extracts its embedded runtime into the user's LOCALAPPDATA/SCPSL-JP-Installer/RuntimeCache directory and may write local diagnostic startup information under the temporary directory. These files are not uploaded to the project maintainers automatically.

## Retention, deletion and uninstalling
The project does not retain installer telemetry on its own servers. Downloaded translation files, runtime cache files and any locally written diagnostic files remain on the user's computer until removed. To uninstall the translation, close SCP: Secret Laboratory, switch to another language in the game if needed, then remove the corresponding JP-Ver* folder from its Translations directory. To remove the portable installer, delete the downloaded SCPSL.exe file and, if desired, the app's LOCALAPPDATA/SCPSL-JP-Installer/RuntimeCache folder. Be careful not to delete unrelated translations or game data. A game registry.txt backup may remain.

## Contact and revisions
For privacy questions, contact the project maintainers through [GitHub Issues](https://github.com/hayabusa255/SCPSLTranslationJP/issues) without posting sensitive logs or personal data publicly. This policy may change when the software's data-handling behavior changes.
