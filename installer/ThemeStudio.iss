#define AppVersion GetEnv("THEME_STUDIO_VERSION")
#define PublishDir GetEnv("THEME_STUDIO_PUBLISH_DIR")
#define ReleaseDir GetEnv("THEME_STUDIO_OUTPUT_DIR")
#define WebView2Bootstrapper GetEnv("THEME_STUDIO_WEBVIEW2_BOOTSTRAPPER")

[Setup]
AppId={{5F3D0C90-0860-4D91-9A02-5E7D06AE3774}
AppName=x纸鸢
AppVersion={#AppVersion}
AppVerName=x纸鸢 {#AppVersion}
AppPublisher=x纸鸢 contributors
VersionInfoVersion={#AppVersion}
VersionInfoProductName=x纸鸢
VersionInfoDescription=x纸鸢 Codex 主题工作台
DefaultDirName={localappdata}\Programs\x纸鸢
DefaultGroupName=x纸鸢
UninstallDisplayName=x纸鸢
UninstallDisplayIcon={app}\ThemeStudioForCodex.exe
OutputDir={#ReleaseDir}
OutputBaseFilename=XZhiYuan-Setup-{#AppVersion}-win-x64
SetupIconFile=..\src\ThemeStudio.App\x-zhiyuan.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
CloseApplications=yes
RestartApplications=no
DisableProgramGroupPage=yes
DisableWelcomePage=no
DisableDirPage=no
SetupLogging=yes
UsePreviousAppDir=yes
ChangesAssociations=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: checkedonce

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#WebView2Bootstrapper}"; DestDir: "{tmp}"; DestName: "MicrosoftEdgeWebview2Setup.exe"; Flags: deleteafterinstall

[Icons]
Name: "{group}\x纸鸢"; Filename: "{app}\ThemeStudioForCodex.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\x纸鸢"; Filename: "{app}\ThemeStudioForCodex.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "正在准备界面运行组件..."; Flags: waituntilterminated; Check: NeedsWebView2Runtime
Filename: "{app}\ThemeStudioForCodex.exe"; Description: "打开 x纸鸢"; Flags: nowait postinstall skipifsilent runasoriginaluser
Filename: "{app}\ThemeStudioForCodex.exe"; Flags: nowait runasoriginaluser; Check: WizardSilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\WebView2"

[Code]
const
  WebView2ClientId = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  LegacyWebView2ClientId = '{F3017226-FE2A-4295-8BDF-00C25E1CBBE8}';

function HasWebView2Version(RootKey: Integer; const SubKey: String): Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(RootKey, SubKey, 'pv', Version) and
    (Version <> '') and (Version <> '0.0.0.0');
end;

function WebView2RuntimeInstalled(): Boolean;
var
  ClientKey: String;
  LegacyClientKey: String;
begin
  ClientKey := 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + WebView2ClientId;
  LegacyClientKey := 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + LegacyWebView2ClientId;
  Result := HasWebView2Version(HKLM32, ClientKey) or
    HasWebView2Version(HKCU32, ClientKey) or
    HasWebView2Version(HKLM64, ClientKey) or
    HasWebView2Version(HKCU64, ClientKey) or
    HasWebView2Version(HKLM32, LegacyClientKey) or
    HasWebView2Version(HKCU32, LegacyClientKey) or
    HasWebView2Version(HKLM64, LegacyClientKey) or
    HasWebView2Version(HKCU64, LegacyClientKey);
end;

function NeedsWebView2Runtime(): Boolean;
begin
  Result := not WebView2RuntimeInstalled();
end;

function TryOpenInstalledVersion(): Boolean;
var
  AppPath: String;
  InstallLocation: String;
  InstalledVersion: String;
  ResultCode: Integer;
  UninstallKey: String;
begin
  Result := False;
  UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{5F3D0C90-0860-4D91-9A02-5E7D06AE3774}_is1';
  if not RegQueryStringValue(HKCU, UninstallKey, 'DisplayVersion', InstalledVersion) then
    Exit;
  if CompareText(InstalledVersion, '{#AppVersion}') <> 0 then
    Exit;
  if not RegQueryStringValue(HKCU, UninstallKey, 'InstallLocation', InstallLocation) then
    Exit;

  AppPath := AddBackslash(InstallLocation) + 'ThemeStudioForCodex.exe';
  if not FileExists(AppPath) then
    Exit;

  Result := ShellExec('', AppPath, '', InstallLocation, SW_SHOWNORMAL, ewNoWait, ResultCode);
end;

function InitializeSetup(): Boolean;
begin
  Result := not TryOpenInstalledVersion();
end;
