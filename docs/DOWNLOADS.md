# SCPSLTranslationJP — Downloads and code signing

This is the public download page for the open-source Japanese localization and native Windows installer for SCP: Secret Laboratory.

## Windows native installer
- [Download the latest stable Windows SCPSL.exe](https://github.com/hayabusa255/SCPSLTranslationJP/releases/download/JP-Ver14.2Fix/SCPSL.exe)
- [Download the v15.0 Private Beta Windows SCPSL.exe](https://github.com/hayabusa255/SCPSLTranslationJP/releases/download/JP-Ver15.0Beta/SCPSL.exe)
- [Browse all published releases](https://github.com/hayabusa255/SCPSLTranslationJP/releases)

The Windows installer is a single self-contained x64 executable. It detects Steam game installations, retrieves Japanese-language translation files from GitHub Releases and installs the selected stable or beta translation into the game's Translations directory. The launcher includes its WinUI runtime and does not require separate .NET or Windows App Runtime setup.

## Manual ZIP installation (Windows / Linux / Steam Deck)
- [Latest stable translation ZIP](https://github.com/hayabusa255/SCPSLTranslationJP/releases/download/JP-Ver14.2Fix/JP-Ver14.2Fix.zip)
- [v15.0 Private Beta translation ZIP](https://github.com/hayabusa255/SCPSLTranslationJP/releases/download/JP-Ver15.0Beta/JP-Ver15.0Beta.zip)

Extract the ZIP into a folder named after the release tag (for example JP-Ver15.0Beta), place that folder in SCP Secret Laboratory/Translations, select Japanese in the game's language settings, and restart the game. Exit the game before replacing translation files. To uninstall, switch the game to another language and delete only the corresponding JP-Ver* translation folder.

## Code signing policy
Current status (2026-11-19): **Windows executables are not yet signed**. We are preparing an application to the SignPath Foundation open-source signing program. If approved and integrated, we plan to use the following attribution for signed builds: **Free code signing provided by SignPath.io, certificate by SignPath Foundation.** Signing has not been activated and we do not represent unsigned downloads as signed.

See the full [Code signing policy](CODE_SIGNING_POLICY.md) and [Privacy policy](PRIVACY_POLICY.md). Unsigned executables may be restricted by Windows Smart App Control; users should not disable operating-system protection just to run the installer. The manual translation ZIP is an alternative.

Project repository: https://github.com/hayabusa255/SCPSLTranslationJP
