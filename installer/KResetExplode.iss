; Instalador do RESET EXPLODE (Inno Setup 6: https://jrsoftware.org/isinfo.php)
; Instala em C:\Program Files\KResetExplode, pasta que só administradores alteram. Como o programa roda como administrador,
; isso impede que outro programa troque o .exe ou as DLLs dele. A pasta não pode ser trocada no assistente por esse motivo.
;
; Gerar:  ISCC.exe /DAppVersion=0.16.0 /DSourceDir=..\publish-install installer\KResetExplode.iss
; (SourceDir = saída de: dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish-install)

#ifndef AppVersion
  #define AppVersion "0.16.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish-install"
#endif

[Setup]
AppId={{DCDFECBD-B309-465B-97C7-5B9D33FAEBAC}
AppName=RESET EXPLODE
AppVersion={#AppVersion}
AppVerName=RESET EXPLODE {#AppVersion}
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\KResetExplode
DisableDirPage=yes
DefaultGroupName=RESET EXPLODE
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\installer-out
OutputBaseFilename=KResetExplode-Setup
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\KResetExplode.exe
UninstallDisplayName=RESET EXPLODE
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LEIA-ME.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\RESET EXPLODE"; Filename: "{app}\KResetExplode.exe"
Name: "{autodesktop}\RESET EXPLODE"; Filename: "{app}\KResetExplode.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\KResetExplode.exe"; Description: "{cm:LaunchProgram,RESET EXPLODE}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
; Arquivos criados pelo programa (downloads de drivers, temporários e o SDK da Corsair baixado pelo app)
Type: filesandordirs; Name: "{app}\Temp"
Type: filesandordirs; Name: "{app}\Downloads"
Type: files; Name: "{app}\iCUESDK.x64_2019.dll"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  data: String;
begin
  data := ExpandConstant('{app}\Usuarios');
  if (CurUninstallStep = usPostUninstall) and DirExists(data) then
    if SuppressibleMsgBox('Apagar também as configurações, o histórico de benchmarks e os backups do RESET EXPLODE?' + #13#10#13#10 +
                          'Escolha Não para manter (por exemplo, se for reinstalar ou usar a versão portátil neste PC).',
                          mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
    begin
      DelTree(data, True, True, True);
      RemoveDir(ExpandConstant('{app}'));
    end;
end;
