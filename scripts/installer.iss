#ifndef AppVersion
  #define AppVersion "0.3.0"
#endif
#ifndef Runtime
  #define Runtime "win-x64"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\win-x64\publish"
#endif
#ifndef OutDir
  #define OutDir "..\artifacts\win-x64"
#endif
#if Runtime == "win-arm64"
  #define Arch "arm64"
#else
  #define Arch "x64"
#endif
[Setup]
AppId={{A34ED4DE-2A9B-43D8-AB4C-4F9C737B3095}
AppName=SoopaDriveTool
AppVersion={#AppVersion}
AppPublisher=Everett Jenkins
AppPublisherURL=https://github.com/eMacTh3Creator/SoopaDriveTool
AppSupportURL=https://github.com/eMacTh3Creator/SoopaDriveTool/issues
AppUpdatesURL=https://github.com/eMacTh3Creator/SoopaDriveTool/releases
DefaultDirName={autopf}\SoopaDriveTool
DefaultGroupName=SoopaDriveTool
UninstallDisplayIcon={app}\SoopaDriveTool.exe
SetupIconFile=..\src\SoopaDriveTool.Windows\Assets\app.ico
OutputDir={#OutDir}
OutputBaseFilename=SoopaDriveTool-{#AppVersion}-{#Arch}-Setup
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
MinVersion=10.0.22000
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
LicenseFile=..\LICENSE
CloseApplications=yes
RestartApplications=no
Uninstallable=yes
[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\SoopaDriveTool"; Filename: "{app}\SoopaDriveTool.exe"
Name: "{autodesktop}\SoopaDriveTool"; Filename: "{app}\SoopaDriveTool.exe"; Tasks: desktopicon
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
