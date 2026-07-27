# Release Process

## Local release

Run the release script from the repository root:

```powershell
.\scripts\build-release.ps1
```

When `dotnet` or Inno Setup is not on `PATH`, pass their executable paths:

```powershell
.\scripts\build-release.ps1 `
  -Version 1.2.3 `
  -DotNetPath C:\path\to\dotnet.exe `
  -IsccPath C:\path\to\ISCC.exe
```

When `-Version` is omitted, the script reads the version from
`src/ThemeStudio.App/ThemeStudio.App.csproj`. The script restores locked
dependencies, tests, publishes a self-contained x64 application, builds the
installer, and writes SHA-256 checksums plus a machine-readable release manifest
under `artifacts/release`.

When an installer is built, the script downloads and signature-checks Microsoft's
official WebView2 Evergreen Bootstrapper under `artifacts/dependencies`. Pass
`-WebView2BootstrapperPath` to use an existing signed copy in offline build
environments. The setup package runs it only when WebView2 is missing.

## Unsigned GitHub previews

The current public release channel intentionally publishes unsigned prereleases.
Push a `vMAJOR.MINOR.PATCH` tag that exactly matches the project version to run
`.github/workflows/release.yml`. The workflow:

1. restores locked dependencies and runs all tests;
2. builds the installer and portable archive with channel `preview`;
3. publishes `SHA256SUMS.txt` and `release-manifest.json`;
4. creates GitHub build-provenance attestations for every release file;
5. creates a GitHub prerelease with the unsigned-build notice.

Windows SmartScreen warnings are expected. Release notes must never instruct
users to disable SmartScreen or antivirus protection. Download verification is
documented in [verify-download.md](verify-download.md).

## Future Authenticode signing

The build script retains support for a future trusted Authenticode certificate.
Signed releases must sign both `ThemeStudioForCodex.exe` and the final setup
executable before checksums are published. Private keys must remain in a
CA/B Forum-compliant hardware token, HSM, or cloud signing service.

Import a trusted code-signing certificate with a private key into the current
user's `Cert:\CurrentUser\My` store, then run:

```powershell
.\scripts\build-release.ps1 `
  -RequireSigning `
  -SigningCertificateThumbprint CERTIFICATE_THUMBPRINT
```

The script signs and verifies the application before packaging, signs and
verifies the setup executable, and only then creates checksums and the release
manifest. `-RequireSigning` fails before existing artifacts are removed when a
certificate is not supplied. Self-signed certificates are not suitable for
public distribution because they do not establish publisher trust.

Do not place an exportable public-trust signing key in GitHub Secrets. When a
provider is selected, replace the preview signing step with that provider's HSM
client or signing API. Physical USB tokens require a protected self-hosted
runner or a controlled local release machine.

Before publishing any preview, verify the checksums on a clean Windows virtual
machine and record the tested Windows and Codex versions in the release notes.
Unsigned files must remain visibly labeled as previews.

## Rollback

User data is version-independent and stored under
`%LocalAppData%\ThemeStudioForCodex`. Installing an older release over a newer
one replaces application files without deleting themes or media. Keep the
previous setup executable and checksum alongside every release until the next
version has passed compatibility verification.
