; ============================================================
; Zentwik 安装包脚本（Inno Setup 6）
;
; 编译：ISCC.exe build\installer\Zentwik.iss
; 产物：dist\Zentwik-2.0.0-can3-setup-win-x64.exe
;
; 【为什么不用 MSIX】
; 应用清单要求提权，而 MSIX 的声明式提权受限且必须签名、
; 必须走证书信任链。目标用户是「装完就用」的个人用户，MSIX 的门槛
; （开发者证书 / 商店）与他们使用此工具的动机不匹配。
;
; 【权限模型（批次五收尾补入）】
; 主程序 asInvoker，Worker requireAdministrator，安装包 PrivilegesRequired=admin。
; UI 侧零提权：写 SMU / 注册任务计划 / 改注册表一律走 IPC 交给 Worker。
; 安装仍要 admin —— 装到 {autopf} 需要写 Program Files，否则会退化成用户目录，
; 让「同一台机器上两个用户各装一份」成为可能，而 Worker 的单实例互斥体是 Global\ 的。
; ============================================================

#define AppName "Zentwik"
#define AppVersion "2.0.0-can3"
#define AppPublisher "Zentwik"
#define AppExe "Zentwik.UI.exe"

[Setup]
AppId={{8E3F1C42-7B6D-4A15-9C2E-5D0F8B7A1E63}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName} {#AppVersion}
OutputDir=..\..\dist
OutputBaseFilename={#AppName}-{#AppVersion}-setup-win-x64
SetupIconFile=..\..\Zentwik.UI\Assets\Zentwik.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; 主程序要求管理员，安装也必须提权：否则 {autopf} 会退化成用户目录。
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=

; 应用运行时会占住自身 DLL，卸载前必须先结束进程，否则删不掉。
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "chinese"; MessagesFile: "compiler:Default.isl"
Name: "english"; MessagesFile: "compiler:Languages\English.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"; Flags: checkedonce

[Files]
; 整目录打包：便携目录本身就是一个完整运行时，逐文件列举只会在
; 下次升级依赖时静默漏文件。
Source: "..\..\dist\portable\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\卸载 {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "立即启动 {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 只清安装目录里由我们生成的内容；用户配置在 %APPDATA%\Zentwik，不在此列。
Type: filesandordirs; Name: "{app}"

[Code]
// 卸载时明确告知用户：配置与日志不会随卸载删除。
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if MsgBox('是否同时删除配置与日志？' + #13#10 +
              '位置：%APPDATA%\Zentwik' + #13#10 + #13#10 +
              '选择「否」将保留，下次安装可继续使用原有方案与调校记录。',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{userappdata}\Zentwik'), True, True, True);
  end;
end;
