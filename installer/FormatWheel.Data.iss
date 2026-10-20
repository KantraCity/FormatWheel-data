; Format Wheel - Data extension installer (JSON, YAML, XML, TOML, CSV).
; Installs the extension DLL and its dependencies into the app's extensions folder.

#define ExtName    "Format Wheel - Data"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
AppId={{AD7FFE76-A4D3-4065-9287-6915E2F196DE}
AppName={#ExtName}
AppVersion={#AppVersion}
AppVerName={#ExtName} {#AppVersion}
AppPublisher=Format Wheel

PrivilegesRequired=lowest
; extensions live next to the app, each in its own subfolder
DefaultDirName={localappdata}\Programs\FormatWheel\extensions\data
DisableDirPage=yes
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage

OutputDir=..\dist
OutputBaseFilename=FormatWheel-Data-Setup-{#AppVersion}
UninstallDisplayName={#ExtName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
en.NeedApp=Format Wheel is not installed. Install the app first, then this extension.
ru.NeedApp=Format Wheel не установлен. Сначала установите программу, затем это расширение.

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Code]
// без приложения расширение бессмысленно
function InitializeSetup(): Boolean;
begin
  if not DirExists(ExpandConstant('{localappdata}\Programs\FormatWheel')) then
  begin
    MsgBox(ExpandConstant('{cm:NeedApp}'), mbError, MB_OK);
    Result := False;
  end
  else
    Result := True;
end;
