# Microsoft Store (MSIX) build

This is a separate **Store-only** packaging pipeline for the native C++/WinUI 3 + Rust translation installer. The GitHub release SCPSL.exe remains unchanged: it is a standalone launcher and **is not signed** by the Store. The Store signs **only** the submitted MSIX package after certification.

## Partner Center setup

1. In Partner Center, create **MSIX or PWA app** (not EXE/MSI app).
2. Reserve the display name SCPSLTranslationJP (or your chosen approved name).
3. Open the app's **Product identity** page and copy the exact:
   - **Package/Identity Name** (may have a numeric prefix);
   - **Publisher** (complete CN=... identity, not the friendly company name);
   - **Publisher display name**.
4. Run [Build Microsoft Store MSIX](../../actions/workflows/build-store-msix.yml) using **Run workflow** and supply those three values and a four-part version, e.g. 1.0.0.0. For subsequent updates, increase the version.
5. Download the SCPSL-MSIX-STORE-SUBMISSION-UNSIGNED Actions artifact and upload the contained MSIX to the Partner Center package submission. The Store will re-sign it after successful certification.

**Do not guess the identity values.** A manifest with the wrong Publisher or Name cannot be submitted for your reserved Store product.

## Developer test build

Every relevant push creates SCPSL-MSIX-DEV-TEST-ONLY, with:
- SCPSLTranslationJP-DEV-x64.msix: self-signed development MSIX.
- SCPSLTranslationJP-DEV.cer: public test certificate only (no private key).

This build has a separate SCPSLTranslationJP.Dev identity and **cannot** be uploaded as the production Store app. It is for controlled tests on a developer's own Windows 11 PC. Only install the test certificate if you understand the temporary trust it grants; remove the certificate after testing. Never advise public users to install or trust this certificate.

## Technical design

- NativeSelfContained/InnerWinUI builds with SCPSL_STORE_PACKAGE=ON.
- The MSIX contains SCPSLWinUI.exe, scpsl-native-core.exe, application image assets, and pinned Windows App SDK 1.8 native runtime.
- Unlike the standalone EXE, the Store version **does not extract and execute an unsigned Rust engine from Temp**. Both executables live in the installed MSIX package directory and run with normal user rights.
- The application uses the runFullTrust capability and mediumIL. It can access user-writable Steam game directories, but it does not bypass Windows ACLs, UAC or antivirus policies.
- The package targets Windows 11 x64; no .NET runtime is needed.
- The Store manages MSIX installation, uninstall, package integrity, and application updates. Translation ZIPs are still fetched from GitHub Releases.

## Required pre-publication checks

- Install and launch the developer MSIX on a Windows 11 machine.
- Verify Steam detection and manual folder selection.
- Verify stable translation install, switching to Beta, and language preference updates with a real or isolated test game. In particular, check for MSIX app-data virtualization when writing game settings.
- Verify uninstall and reinstall. Do not test against a real user's game without explicit consent.
- Review Microsoft Store content/copyright and trademark policies for SCP: Secret Laboratory and third-party bundled WinAppSDK DLLs.
- Run the Windows App Certification Kit and submit screenshots, privacy-policy link, age rating and Store listing information.

The automated workflow tests the Rust engine and MSIX package contents; a successful build is **not** proof that the Store has certified the app.

References:
- https://learn.microsoft.com/en-us/windows/apps/publish/store-readiness-checklist
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/choose-distribution-path
