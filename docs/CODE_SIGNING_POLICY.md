# Code signing policy — SCPSLTranslationJP

Status on 2026-11-19: **SignPath Foundation signing is not active; current standalone Windows downloads are unsigned. A Microsoft Store MSIX build is in development, but not yet certified.** The project must not claim that current binaries have a SignPath signature or that SignPath Foundation has approved it.

## Intended code-signing attribution
If the application is approved and code signing is activated, the following required attribution will apply to signed releases:

> Free code signing provided by SignPath.io, certificate by SignPath Foundation.

Relevant program: [SignPath Foundation](https://signpath.org/).

## Project ownership and team roles
- Project and source repository: [hayabusa255/SCPSLTranslationJP](https://github.com/hayabusa255/SCPSLTranslationJP).
- Authors / trusted committers: repository owner [@hayabusa255](https://github.com/hayabusa255) and any maintainers granted write access.
- Reviewers: project maintainers, with external pull requests reviewed before merging.
- Signing approver: repository owner [@hayabusa255](https://github.com/hayabusa255); every signing request must be manually approved.
- Contributors use GitHub accounts with multi-factor authentication as required by the SignPath Foundation program.

## Build and release policy
The native Windows application is implemented in C++/WinRT (WinUI 3) and Rust and publicly built with [GitHub Actions](https://github.com/hayabusa255/SCPSLTranslationJP/actions). Sources and build workflows are in the repository under NativeCppWinUI, NativeCore, NativeSelfContained and .github/workflows. Signing will only be requested for release artifacts whose origin can be traced to reviewed repository source code, successful CI builds, and explicit approval by the signing approver.

The self-contained launcher contains project-owned native executables and Microsoft's redistributable Windows App SDK runtime components. The signing configuration must account for the outer Windows launcher and the project-owned inner WinUI and Rust executables. Upstream Microsoft DLLs will not be represented as project-authored binaries or individually signed with the project's certificate. The exact nested signing procedure requires SignPath review before production signing is enabled.

Installer code is licensed under MIT; translation files are licensed separately under Creative Commons Attribution 4.0. See [LICENSE](../LICENSE) and [JP/LICENSE.md](../JP/LICENSE.md). The licensing status of third-party system libraries must be reviewed for SignPath eligibility.

## Releases, privacy and removal
- [Download page](DOWNLOADS.md)
- [Privacy policy](PRIVACY_POLICY.md)
- The application is portable and does not require a system-wide installer. To remove it, delete SCPSL.exe, the app-specific LOCALAPPDATA runtime cache, and any language folders the user chooses to remove. See the privacy policy for details.

Any future signed release will clearly identify its signing status and publish a checksum. Existing unsigned releases remain unsigned unless they are rebuilt and signed under the approved policy.

## Microsoft Store distribution

The separate [Store MSIX build](../NativeStoreMSIX/README.md) is intended for Microsoft Store certification. If accepted, the Store signs the MSIX package; it does not sign or replace the existing standalone GitHub EXE. The Store package is not yet published.
