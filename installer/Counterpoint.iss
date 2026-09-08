; Counterpoint Windows installer script (SRS FR-11, NFR-M2, P0-T07).
;
; Compiled by Inno Setup (iscc) against the self-contained win-x64 publish output
; (`dotnet publish src/Counterpoint.App -c Release -r win-x64 --self-contained true
; -p:PublishReadyToRun=true`), in the "windows-publish" job of .github/workflows/ci.yml.
;
; Installs to %ProgramFiles%\Counterpoint and creates %ProgramData%\Counterpoint\{db,backups,logs}
; - the data directory PosDataDirectory resolves to on Windows (engineering guide §4.9) - with
; write access for the shop's own Windows account, which runs the till day to day with no admin
; rights. A desktop shortcut and the standard Inno Setup uninstaller are included.
;
; Running this on a clean machine, and everything that needs an actual install to prove, is
; HW-T06. This script is only proven to *compile into an installer* here (P0-T07's own
; "Done when"); CI does not run it.

#define MyAppName "Counterpoint"
#define MyAppPublisher "Counterpoint"
#define MyAppExeName "Counterpoint.exe"

; Overridable from the ISCC command line with /DMyAppVersion=..., so CI can pass the version
; semantic-release computed without hand-editing this file (CLAUDE.md: "Never hand-edit <Version>
; in Directory.Build.props" - the installer name follows the same rule, by the same mechanism).
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-dev"
#endif

; Both resolved from the script's own location (SourcePath, which Inno Setup guarantees ends in a
; backslash) rather than from the compiler's current working directory, so `iscc` produces the
; same result whether it is run from the repository root or from inside installer\.
#ifndef MyPublishDir
  #define MyPublishDir SourcePath + "..\artifacts\publish"
#endif
#ifndef MyOutputDir
  #define MyOutputDir SourcePath + "..\artifacts\installer"
#endif

[Setup]
; A plain, stable string rather than a GUID: AppId only has to be unique to this product and
; consistent release to release, and the docs' own example (jrsoftware.org/ishelp,
; "[Setup]: AppId") uses exactly this form.
AppId=CounterpointPOS
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
OutputDir={#MyOutputDir}
OutputBaseFilename=CounterpointSetup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
; %ProgramFiles% and custom ACLs under %ProgramData% both need elevation - one machine, one till,
; installed once by whoever sets the shop's PC up (CLAUDE.md "single-cashier hardware shop").
PrivilegesRequired=admin
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; The whole self-contained publish output: the managed assemblies, the ReadyToRun images and the
; native SQLCipher asset the "windows-publish" CI job already checked is there.
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Dirs]
; PosDataDirectory's three sub-folders (docs/01_DATA_MODEL.md, engineering guide §4.9): the
; encrypted database, the FR-11 backup snapshots, and application logs. users-modify grants the
; shop's own account write access to its own data; every other account keeps the ACL Windows
; applies to %ProgramData% by default (read, not write).
Name: "{commonappdata}\{#MyAppName}"; Permissions: users-modify
Name: "{commonappdata}\{#MyAppName}\db"; Permissions: users-modify
Name: "{commonappdata}\{#MyAppName}\backups"; Permissions: users-modify
Name: "{commonappdata}\{#MyAppName}\logs"; Permissions: users-modify

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent unchecked
