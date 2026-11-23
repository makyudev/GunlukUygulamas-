#define MyAppName "Kişisel Günlüğüm"
#define MyAppExeName "GunlukUygulamasi.exe"
#define MyAppVersion "1.0.2"

[Setup]
AppId={{9A9CC7CB-5D91-4E44-BF13-763CBA20CF26}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=GunlukUygulamasi
DefaultDirName={autopf}\KisiselGunlugum
DefaultGroupName={#MyAppName}
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UsePreviousAppDir=yes
UsePreviousGroup=yes
CloseApplications=yes
RestartApplications=no
OutputDir=Output
OutputBaseFilename=KisiselGunlugum_Setup
SetupIconFile=app.ico
UninstallDisplayIcon={app}\app.ico
WizardStyle=modern
WizardResizable=no
WizardImageFile=Assets\Installer\wizard_large.bmp
WizardSmallImageFile=Assets\Installer\wizard_small.bmp
WizardImageStretch=yes
DisableWelcomePage=no
DisableDirPage=auto
DisableProgramGroupPage=yes
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Messages]
WelcomeLabel2=Bu sihirbaz, şifreli ve özelleştirilebilir Kişisel Günlüğüm uygulamasını bilgisayarınıza kuracaktır. Mevcut günlük kayıtlarınız her zaman güvenle korunur.
FinishedLabel=Kişisel Günlüğüm bilgisayarınıza kuruldu. Uygulamayı başlatmak için Bitir'e tıklayın.
FinishedLabelNoIcons=Kişisel Günlüğüm kurulumu tamamlandı.

[Files]
Source: "bin\Release\net8.0-windows\win-x64\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "app.ico"; DestDir: "{app}"; Flags: ignoreversion

; Diary data is stored under %LOCALAPPDATA%\GunlukUygulamasi, outside {app}.
; No install or uninstall delete rules target the user's data directory.
[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Masaüstüne kısayol oluştur"; GroupDescription: "Ek seçenekler:"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{#MyAppName} uygulamasını şimdi çalıştır"; \
    Flags: postinstall nowait skipifsilent
