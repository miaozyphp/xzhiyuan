[CmdletBinding()]
param(
    [Parameter()]
    [string] $Version = '',

    [Parameter()]
    [string] $DotNetPath = 'dotnet',

    [Parameter()]
    [string] $IsccPath = '',

    [Parameter()]
    [string] $WebView2BootstrapperPath = '',

    [Parameter()]
    [string] $SignToolPath = '',

    [Parameter()]
    [string] $SigningCertificateThumbprint = '',

    [Parameter()]
    [string] $TimestampUrl = 'http://timestamp.digicert.com',

    [Parameter()]
    [ValidateSet('development', 'preview', 'release')]
    [string] $ReleaseChannel = 'development',

    [Parameter()]
    [switch] $RequireSigning
)

$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$appProjectPath = Join-Path $repoRoot 'src\ThemeStudio.App\ThemeStudio.App.csproj'

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml] $project = Get-Content -LiteralPath $appProjectPath -Raw
    $Version = @($project.Project.PropertyGroup | ForEach-Object { [string] $_.Version }) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -First 1
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Release version must use MAJOR.MINOR.PATCH format. Received: '$Version'"
}

function Resolve-InnoCompiler([string] $RequestedPath) {
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $explicitPath = [IO.Path]::GetFullPath($RequestedPath)
        if (-not (Test-Path -LiteralPath $explicitPath -PathType Leaf)) {
            throw "Inno Setup compiler was not found: $explicitPath"
        }
        return $explicitPath
    }

    $isccCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($isccCommand) {
        return $isccCommand.Source
    }

    $candidates = @()
    if (${env:ProgramFiles(x86)}) {
        $candidates += Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
    }
    if ($env:ProgramFiles) {
        $candidates += Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'
    }
    if ($env:LOCALAPPDATA) {
        $candidates += Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
    }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }

    throw 'Inno Setup 6 compiler (ISCC.exe) was not found. Existing release artifacts were preserved.'
}

function Resolve-SignTool([string] $RequestedPath) {
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $explicitPath = [IO.Path]::GetFullPath($RequestedPath)
        if (-not (Test-Path -LiteralPath $explicitPath -PathType Leaf)) {
            throw "SignTool was not found: $explicitPath"
        }
        return $explicitPath
    }

    $signToolCommand = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($signToolCommand) {
        return $signToolCommand.Source
    }

    if (${env:ProgramFiles(x86)}) {
        $sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
        $candidate = Get-ChildItem -LiteralPath $sdkBin -Filter signtool.exe -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object FullName -Match '\\x64\\signtool\.exe$' |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) {
            return $candidate.FullName
        }
    }

    throw 'SignTool.exe was not found. Install the Windows SDK or pass -SignToolPath.'
}

function Assert-CodeSigningCertificate([string] $Thumbprint) {
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$Thumbprint" -ErrorAction SilentlyContinue
    if (-not $certificate) {
        throw "The code-signing certificate was not found in Cert:\CurrentUser\My: $Thumbprint"
    }
    if (-not $certificate.HasPrivateKey) {
        throw 'The selected code-signing certificate has no private key.'
    }
    if ($certificate.NotBefore -gt [DateTime]::Now -or $certificate.NotAfter -le [DateTime]::Now) {
        throw 'The selected code-signing certificate is not currently valid.'
    }
    if ($certificate.EnhancedKeyUsageList.ObjectId.Value -notcontains '1.3.6.1.5.5.7.3.3') {
        throw 'The selected certificate is not valid for code signing.'
    }
}

function Sign-ReleaseFile(
    [string] $Path,
    [string] $ToolPath,
    [string] $Thumbprint,
    [string] $TimestampServer) {
    $productName = 'x' + [char]0x7EB8 + [char]0x9E22
    & $ToolPath sign /sha1 $Thumbprint /s My /fd SHA256 /tr $TimestampServer /td SHA256 /d $productName $Path
    if ($LASTEXITCODE -ne 0) {
        throw "Code signing failed: $Path"
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid') {
        throw "The signed file failed Authenticode validation: $Path ($($signature.Status))"
    }
}

$IsccPath = Resolve-InnoCompiler $IsccPath
$SigningCertificateThumbprint = $SigningCertificateThumbprint.Replace(' ', '').ToUpperInvariant()
$signingEnabled = -not [string]::IsNullOrWhiteSpace($SigningCertificateThumbprint)
if ($RequireSigning -and -not $signingEnabled) {
    throw 'A signing certificate thumbprint is required when -RequireSigning is used. Existing release artifacts were preserved.'
}
if (-not $signingEnabled -and $ReleaseChannel -eq 'release') {
    throw 'The release channel requires a valid code-signing certificate. Existing release artifacts were preserved.'
}
if ($signingEnabled) {
    Assert-CodeSigningCertificate $SigningCertificateThumbprint
    $SignToolPath = Resolve-SignTool $SignToolPath
    $ReleaseChannel = 'release'
}

$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$publishDir = [IO.Path]::GetFullPath((Join-Path $artifactsRoot 'publish\win-x64'))
$releaseDir = [IO.Path]::GetFullPath((Join-Path $artifactsRoot 'release'))

if (-not $artifactsRoot.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Artifacts directory escaped the repository root.'
}

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
if (Test-Path -LiteralPath $releaseDir) {
    Remove-Item -LiteralPath $releaseDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publishDir, $releaseDir | Out-Null

& $DotNetPath restore (Join-Path $repoRoot 'ThemeStudio.sln') --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

& $DotNetPath restore $appProjectPath --runtime win-x64 --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'dotnet win-x64 runtime restore failed.' }

& $DotNetPath test (Join-Path $repoRoot 'ThemeStudio.sln') --no-restore --configuration Release --logger 'console;verbosity=minimal'
if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }

& $DotNetPath publish (Join-Path $repoRoot 'src\ThemeStudio.App\ThemeStudio.App.csproj') `
    --no-restore `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDir `
    "/p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$uiSource = Join-Path $repoRoot 'src\ThemeStudio.App\ui'
$uiTarget = Join-Path $publishDir 'ui'
New-Item -ItemType Directory -Force -Path $uiTarget | Out-Null
Get-ChildItem -LiteralPath $uiSource -Force |
    Copy-Item -Destination $uiTarget -Recurse -Force

# The WebView2 host serves this external folder. A single-file bundle cannot
# satisfy those requests, so verify every source UI file before packaging.
$pathSeparators = [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
Get-ChildItem -LiteralPath $uiSource -Recurse -File | ForEach-Object {
    $relativePath = $_.FullName.Substring($uiSource.Length).TrimStart($pathSeparators)
    $publishedFile = Join-Path $uiTarget $relativePath
    if (-not (Test-Path -LiteralPath $publishedFile -PathType Leaf)) {
        throw "UI resource was not published: $relativePath"
    }
    if ((Get-Item -LiteralPath $publishedFile).Length -ne $_.Length) {
        throw "Published UI resource has the wrong size: $relativePath"
    }
}

$requiredUiFiles = @(
    'index.html',
    'styles.css',
    'app.js',
    'vendor\lucide.min.js'
)
foreach ($relativePath in $requiredUiFiles) {
    $publishedFile = Join-Path $uiTarget $relativePath
    if (-not (Test-Path -LiteralPath $publishedFile -PathType Leaf)) {
        throw "Required UI resource was not published: $relativePath"
    }
}

$publishedLucide = Join-Path $uiTarget 'vendor\lucide.min.js'
if ((Get-Item -LiteralPath $publishedLucide).Length -lt 100KB) {
    throw 'The published Lucide icon runtime is missing or incomplete.'
}

$seedSource = Join-Path $repoRoot 'src\ThemeStudio.App\SeedAssets'
$seedTarget = Join-Path $publishDir 'SeedAssets'
New-Item -ItemType Directory -Force -Path $seedTarget | Out-Null
Get-ChildItem -LiteralPath $seedSource -Force |
    Copy-Item -Destination $seedTarget -Recurse -Force

$requiredSeedAssets = @(
    'rain-archive.png',
    'x-zhiyuan-emblem.png'
)
foreach ($assetName in $requiredSeedAssets) {
    $publishedAsset = Join-Path $seedTarget $assetName
    if (-not (Test-Path -LiteralPath $publishedAsset -PathType Leaf)) {
        throw "Required seed asset was not published: $assetName"
    }
}

$publishedEmblem = Join-Path $seedTarget 'x-zhiyuan-emblem.png'
if ((Get-Item -LiteralPath $publishedEmblem).Length -lt 20KB) {
    throw 'The published X ZhiYuan emblem is missing or incomplete.'
}

$publishedExecutable = Join-Path $publishDir 'ThemeStudioForCodex.exe'
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw 'The X ZhiYuan executable was not published.'
}
if ($signingEnabled) {
    Sign-ReleaseFile $publishedExecutable $SignToolPath $SigningCertificateThumbprint $TimestampUrl
}

Get-ChildItem -LiteralPath $publishDir -File |
    Where-Object Extension -in @('.pdb', '.xml') |
    Remove-Item -Force

$releaseDocuments = @(
    'LICENSE',
    'README.md',
    'CHANGELOG.md',
    'THIRD_PARTY_NOTICES.md',
    'PRIVACY.md',
    'SECURITY.md'
)
foreach ($document in $releaseDocuments) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $document) -Destination $publishDir
}

if ([string]::IsNullOrWhiteSpace($WebView2BootstrapperPath)) {
    $dependencyDir = Join-Path $artifactsRoot 'dependencies'
    New-Item -ItemType Directory -Force -Path $dependencyDir | Out-Null
    $WebView2BootstrapperPath = Join-Path $dependencyDir 'MicrosoftEdgeWebview2Setup.exe'
    if (-not (Test-Path -LiteralPath $WebView2BootstrapperPath -PathType Leaf)) {
        Write-Host 'Downloading the official Microsoft WebView2 Evergreen Bootstrapper...'
        Invoke-WebRequest `
            -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' `
            -OutFile $WebView2BootstrapperPath `
            -UseBasicParsing
    }
}

$WebView2BootstrapperPath = [IO.Path]::GetFullPath($WebView2BootstrapperPath)
if (-not (Test-Path -LiteralPath $WebView2BootstrapperPath -PathType Leaf)) {
    throw "WebView2 bootstrapper was not found: $WebView2BootstrapperPath"
}

$webView2Signature = Get-AuthenticodeSignature -LiteralPath $WebView2BootstrapperPath
if ($webView2Signature.Status -ne 'Valid' -or
    $webView2Signature.SignerCertificate.Subject -notlike '*Microsoft Corporation*') {
    throw 'The WebView2 bootstrapper does not have a valid Microsoft signature.'
}

$env:THEME_STUDIO_VERSION = $Version
$env:THEME_STUDIO_PUBLISH_DIR = $publishDir
$env:THEME_STUDIO_OUTPUT_DIR = $releaseDir
$env:THEME_STUDIO_WEBVIEW2_BOOTSTRAPPER = $WebView2BootstrapperPath
& $IsccPath (Join-Path $repoRoot 'installer\ThemeStudio.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }

$setupExecutable = Join-Path $releaseDir "XZhiYuan-Setup-$Version-win-x64.exe"
if (-not (Test-Path -LiteralPath $setupExecutable -PathType Leaf)) {
    throw 'The setup executable was not generated.'
}
if ($signingEnabled) {
    Sign-ReleaseFile $setupExecutable $SignToolPath $SigningCertificateThumbprint $TimestampUrl
}
else {
    Write-Warning "The $ReleaseChannel packages are unsigned. Windows publisher and SmartScreen warnings are expected."
}

$portableZip = Join-Path $releaseDir "XZhiYuan-$Version-win-x64-portable.zip"
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $portableZip -CompressionLevel Optimal

$releaseFiles = Get-ChildItem -LiteralPath $releaseDir -File | Sort-Object Name
$checksums = foreach ($file in $releaseFiles) {
    $hash = Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), $file.Name
}
$checksumsPath = Join-Path $releaseDir 'SHA256SUMS.txt'
[IO.File]::WriteAllLines($checksumsPath, $checksums, [Text.UTF8Encoding]::new($false))

$manifestFiles = Get-ChildItem -LiteralPath $releaseDir -File |
    Where-Object Name -ne 'release-manifest.json' |
    Sort-Object Name |
    ForEach-Object {
        $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
        [ordered]@{ name = $_.Name; size = $_.Length; sha256 = $hash.Hash.ToLowerInvariant() }
    }
$manifest = [ordered]@{
    schemaVersion = 1
    version = $Version
    channel = $ReleaseChannel
    publishedAt = [DateTimeOffset]::UtcNow.ToString('O')
    files = @($manifestFiles)
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseDir 'release-manifest.json') -Encoding utf8

Write-Host "Release artifacts: $releaseDir"
