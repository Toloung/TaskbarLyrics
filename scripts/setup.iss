; LyricsBar — Inno Setup 安装脚本
; 使用前先通过 build-installer.ps1 打包，或者手动：
;   1. dotnet publish TaskbarLyrics.App/TaskbarLyrics.App.csproj ^
;        -c Release -r win-x64 --self-contained true ^
;        -p:DebugType=None -p:DebugSymbols=false -o publish\win-x64
;   2. ISCC scripts\setup.iss

#define MyAppName "LyricsBar"
#define MyAppPublisher "LyricsBar"
#define MyAppURL "https://github.com/apoint123/TaskbarLyrics"
#define MyAppExeName "LyricsBar.exe"
#define MyLegacyAppExeName "TaskbarLyrics.exe"

; 版本号从命令行传入，例如 /dMyAppVersion=1.0.0.0
; 未传入时使用占位符
#ifndef MyAppVersion
  #define MyAppVersion "2.0.0.0"
#endif

[Setup]
AppId={{609AC5E1-04AD-4A6F-89ED-7BE563B42134}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableDirPage=no
AllowNoIcons=yes
OutputDir=..\dist
OutputBaseFilename=LyricsBar-{#MyAppVersion}-Setup
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=admin
SetupIconFile=..\TaskbarLyrics.App\Assets\AppIcon\TaskbarLyrics.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式(&D)"; GroupDescription: "快捷方式："; Flags: checkedonce

[Files]
; 应用程序文件（所有发布输出）
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent

; WebView2 Runtime 检测与静默安装
;#expr EmitPreprocessor
[Code]
const
  WebView2RegKey = 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00FB3A68797D}';
  WebView2RegKeyWow = 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00FB3A68797D}';
  WebView2Url = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';

function IsUpgradeInstall: Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\{#MyAppExeName}')) or
            FileExists(ExpandConstant('{app}\{#MyLegacyAppExeName}'));
end;

function IsTaskbarLyricsRunning: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'),
    '/C tasklist /FI "IMAGENAME eq {#MyAppExeName}" /NH | find /I "{#MyAppExeName}" >NUL',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
  if not Result then
  begin
    Result := Exec(ExpandConstant('{cmd}'),
      '/C tasklist /FI "IMAGENAME eq {#MyLegacyAppExeName}" /NH | find /I "{#MyLegacyAppExeName}" >NUL',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
  end;
end;

function CloseTaskbarLyrics: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'),
    '/C taskkill /IM "{#MyAppExeName}" /T /F',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and
    ((ResultCode = 0) or (ResultCode = 128));
  Result := Result and Exec(ExpandConstant('{cmd}'),
    '/C taskkill /IM "{#MyLegacyAppExeName}" /T /F',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and
    ((ResultCode = 0) or (ResultCode = 128));
end;

function IsWebView2Installed: Boolean;
var
  Version: string;
begin
  Result := RegQueryStringValue(HKLM, WebView2RegKey, 'pv', Version) or
            RegQueryStringValue(HKCU, WebView2RegKey, 'pv', Version) or
            RegQueryStringValue(HKLM, WebView2RegKeyWow, 'pv', Version) or
            RegQueryStringValue(HKCU, WebView2RegKeyWow, 'pv', Version);
end;

function URLDownloadToFile(pCaller: LongInt; szURL: string; szFileName: string; dwReserved: LongInt; lpfnCB: LongInt): Integer;
  external 'URLDownloadToFileW@urlmon.dll stdcall';

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  InstallerPath: string;
begin
  Result := '';

  if IsUpgradeInstall then
  begin
    MsgBox('检测到已安装的 {#MyAppName}。' + #13#10 +
           '安装程序将升级现有版本，并保留你的设置、歌词缓存和数据库。',
           mbInformation, MB_OK);
  end;

  if IsTaskbarLyricsRunning then
  begin
    MsgBox('检测到 {#MyAppName} 正在运行。' + #13#10 +
           '安装程序将先关闭正在运行的程序，然后继续安装。',
           mbInformation, MB_OK);

    if not CloseTaskbarLyrics then
    begin
      Result := '无法自动关闭 {#MyAppName}。请手动退出程序后重新运行安装程序。';
      Exit;
    end;
  end;

  if IsWebView2Installed then
    Exit;

  if MsgBox('{#MyAppName} 需要 WebView2 Runtime 来显示歌词窗口。' + #13#10 +
            '是否自动下载并安装？' + #13#10#13#10 +
            '选择「是」将自动下载并静默安装。' + #13#10 +
            '选择「否」可稍后自行安装。',
            mbConfirmation, MB_YESNO) = IDYES then
  begin
    InstallerPath := ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe');
    if URLDownloadToFile(0, WebView2Url, InstallerPath, 0, 0) = 0 then
    begin
      if Exec(InstallerPath, '/silent /install', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
      begin
        if ResultCode = 0 then
          Log('WebView2 Runtime installed successfully.')
        else
          Log('WebView2 Runtime installation failed with code: ' + IntToStr(ResultCode));
      end
      else
        Log('Failed to execute WebView2 installer.');
    end
    else
      MsgBox('下载失败，请手动安装 WebView2 Runtime：' + #13#10 + WebView2Url,
             mbError, MB_OK);
  end;
end;
