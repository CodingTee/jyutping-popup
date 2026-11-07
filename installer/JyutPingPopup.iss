; 用法: 用 Inno Setup 打开本文件 -> Compile (或 iscc JyutPingPopup.iss)
; 装到 AppData\Local\Programs\ (per-user, 不弹 UAC), 自启走 HKCU Run, 卸载干净
; 路径均相对本 .iss 所在目录, clone 后可直接在任意机器上编译

#define MyAppName "粤语拼音弹窗"
#define MyAppVersion "1.0"
#define MyExe "JyutPingPopup.exe"
#define PublishDir "..\csharp\publish"

[Setup]
AppId={{A9F3C1E2-4B57-4D8A-9C12-3E6F7A8B9C0D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=CodingTee
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=out
OutputBaseFilename=JyutPingPopup-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0.10240
SetupIconFile=..\assets\logo.ico
UninstallDisplayName={#MyAppName}
; 不打包 .pdb / selftest 产物, 只装运行必需文件

[Languages]
Name: "chinese"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
Source: "{#PublishDir}\{#MyExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\data\*"; DestDir: "{app}\data"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyExe}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "额外快捷方式:"; Flags: unchecked
Name: "tray"; Description: "授权系统托盘常驻（开机自启，推荐）"; GroupDescription: "启动选项:";

[Registry]
; 自启注册表, 仅勾选 startup 任务时写入, 卸载时删除该值
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "JyutPingPopup"; ValueData: """{app}\{#MyExe}"""; Tasks: tray; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyExe}"; Description: "安装完成后启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
// .NET 9 desktop runtime check; abort with prompt if missing
function Net9Installed(): Boolean;
var
  v: string;
begin
  if RegQueryStringValue(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost', 'Version', v) then
    Result := (Copy(v, 1, 2) = '9.')
  else
    Result := False;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not Net9Installed() then begin
    MsgBox('未检测到 .NET 9 桌面运行时。' + #13#10 +
           '请先安装 Windows Desktop Runtime 9.0:' + #13#10 +
           'https://dotnet.microsoft.com/download/dotnet/9.0' + #13#10#13#10 +
           '安装完成后再运行本安装程序。', mbError, MB_OK);
    Result := False;
  end;
end;
