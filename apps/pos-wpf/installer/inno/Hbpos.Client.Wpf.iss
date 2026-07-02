#ifndef AppVersion
  #error AppVersion is required. Pass /DAppVersion=1.2.3 to ISCC.exe.
#endif

#ifndef VersionInfoVersion
  #error VersionInfoVersion is required. Pass /DVersionInfoVersion=1.2.3.0 to ISCC.exe.
#endif

#ifndef PublishDir
  #error PublishDir is required. Pass /DPublishDir=<publish-output> to ISCC.exe.
#endif

#ifndef OutputDir
  #define OutputDir "."
#endif

#ifndef LegacyMsiProductCodes
  #define LegacyMsiProductCodes ""
#endif

#define AppName "HB POS"
#define AppExeName "Hbpos.Client.Wpf.exe"

[Setup]
AppId={{58FBA7FB-3CCD-411F-B6EB-9F3B8B97AD68}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Hot Bargain
DefaultDirName={autopf}\HB POS
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=Hbpos.Client.Wpf-{#AppVersion}-x64
SetupIconFile=..\..\src\Hbpos.Client.Wpf\Resources\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#VersionInfoVersion}
VersionInfoCompany=Hot Bargain
VersionInfoDescription=HB POS installer
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"

[Code]
function NormalizeGuid(Value: String): String;
begin
  Result := Trim(Value);
  if (Length(Result) = 36) then
  begin
    Result := '{' + Result + '}';
  end;
end;

function IsGuid(Value: String): Boolean;
begin
  Result :=
    (Length(Value) = 38) and
    (Copy(Value, 1, 1) = '{') and
    (Copy(Value, 38, 1) = '}') and
    (Copy(Value, 10, 1) = '-') and
    (Copy(Value, 15, 1) = '-') and
    (Copy(Value, 20, 1) = '-') and
    (Copy(Value, 25, 1) = '-');
end;

function SameInstallDir(Value: String; Expected: String): Boolean;
begin
  Result :=
    (Trim(Value) <> '') and
    (CompareText(AddBackslash(Value), AddBackslash(Expected)) = 0);
end;

function IsLegacyHbPosMsiEntry(RootKey: Integer; UninstallKey: String; ProductCode: String): Boolean;
var
  DisplayName: String;
  Publisher: String;
  InstallLocation: String;
  NormalizedName: String;
begin
  Result := False;
  if not IsGuid(ProductCode) then
  begin
    exit;
  end;

  if not RegQueryStringValue(RootKey, UninstallKey + '\' + ProductCode, 'DisplayName', DisplayName) then
  begin
    exit;
  end;

  RegQueryStringValue(RootKey, UninstallKey + '\' + ProductCode, 'Publisher', Publisher);
  RegQueryStringValue(RootKey, UninstallKey + '\' + ProductCode, 'InstallLocation', InstallLocation);

  NormalizedName := Lowercase(Trim(DisplayName));
  Result :=
    ((NormalizedName = Lowercase('{#AppName}')) or
     (Pos('hb pos', NormalizedName) > 0) or
     (Pos('hbpos', NormalizedName) > 0)) and
    (Pos('hot bargain', Lowercase(Publisher)) > 0) and
    (SameInstallDir(InstallLocation, ExpandConstant('{app}')) or
     SameInstallDir(InstallLocation, ExpandConstant('{autopf}\HB POS')) or
     SameInstallDir(InstallLocation, ExpandConstant('{autopf32}\HB POS')));
end;

function UninstallLegacyMsiProductCode(ProductCode: String; var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  ProductCode := NormalizeGuid(ProductCode);
  Result := '';

  if not IsGuid(ProductCode) then
  begin
    Result := 'Legacy MSI ProductCode is invalid: ' + ProductCode;
    exit;
  end;

  Log('Uninstalling legacy MSI before installing HB POS: ' + ProductCode);
  if not Exec('msiexec.exe', '/x ' + ProductCode + ' /qn /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := 'Failed to start Windows Installer for legacy HB POS uninstall.';
    exit;
  end;

  if ResultCode = 3010 then
  begin
    NeedsRestart := True;
    exit;
  end;

  if (ResultCode <> 0) and (ResultCode <> 1605) then
  begin
    Result := 'Legacy HB POS MSI uninstall failed. Exit code: ' + IntToStr(ResultCode);
  end;
end;

function UninstallConfiguredLegacyMsiProductCodes(var NeedsRestart: Boolean): String;
var
  ProductCodes: String;
  Separator: Integer;
  ProductCode: String;
begin
  Result := '';
  ProductCodes := '{#LegacyMsiProductCodes}';

  while Trim(ProductCodes) <> '' do
  begin
    Separator := Pos(';', ProductCodes);
    if Separator = 0 then
    begin
      ProductCode := Trim(ProductCodes);
      ProductCodes := '';
    end
    else
    begin
      ProductCode := Trim(Copy(ProductCodes, 1, Separator - 1));
      Delete(ProductCodes, 1, Separator);
    end;

    if ProductCode <> '' then
    begin
      Result := UninstallLegacyMsiProductCode(ProductCode, NeedsRestart);
      if Result <> '' then
      begin
        exit;
      end;
    end;
  end;
end;

function FindAndUninstallLegacyMsiEntriesInRoot(RootKey: Integer; var NeedsRestart: Boolean): String;
var
  UninstallKey: String;
  ProductCodes: TArrayOfString;
  I: Integer;
  ProductCode: String;
begin
  Result := '';
  UninstallKey := 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall';

  if not RegGetSubkeyNames(RootKey, UninstallKey, ProductCodes) then
  begin
    exit;
  end;

  for I := 0 to GetArrayLength(ProductCodes) - 1 do
  begin
    ProductCode := ProductCodes[I];
    if IsLegacyHbPosMsiEntry(RootKey, UninstallKey, ProductCode) then
    begin
      // 中文注释：兜底迁移只接受 MSI GUID 子键，并同时匹配名称、发布商和安装目录，避免误卸载其它程序。
      Result := UninstallLegacyMsiProductCode(ProductCode, NeedsRestart);
      if Result <> '' then
      begin
        exit;
      end;
    end;
  end;
end;

function FindAndUninstallLegacyMsiEntries(var NeedsRestart: Boolean): String;
begin
  Result := FindAndUninstallLegacyMsiEntriesInRoot(HKLM64, NeedsRestart);
  if Result <> '' then
  begin
    exit;
  end;

  Result := FindAndUninstallLegacyMsiEntriesInRoot(HKLM32, NeedsRestart);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  // 中文注释：先按配置的旧 MSI ProductCode 精确迁移；未覆盖的门店再走严格注册表兜底。
  Result := UninstallConfiguredLegacyMsiProductCodes(NeedsRestart);
  if Result <> '' then
  begin
    exit;
  end;

  Result := FindAndUninstallLegacyMsiEntries(NeedsRestart);
end;
