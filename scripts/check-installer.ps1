param([ValidateSet('win-arm64','win-x64')][string]$Runtime='win-x64',[string]$Version='0.2.1')
$ErrorActionPreference='Stop';$root=Split-Path $PSScriptRoot -Parent;$output=Join-Path $root "artifacts\$Runtime";$arch=$Runtime.Replace('win-','')
$installer=Join-Path $output "SoopaDriveTool-$Version-$arch-Setup.exe";$testDir=Join-Path $env:TEMP ('SoopaDriveTool-InstallCheck-'+[guid]::NewGuid().ToString('N'))
$preview=Join-Path $output 'installed-preview.png'
foreach($base in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall')){
 if(Test-Path (Join-Path $base '{A34ED4DE-2A9B-43D8-AB4C-4F9C737B3095}_is1')){throw 'An existing SoopaDriveTool installation is present. Run install/uninstall smoke checks only on an isolated clean host to preserve that installation.'}
}
try{
  $p=Start-Process $installer -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',"/DIR=$testDir",'/GROUP=SoopaDriveTool Install Check' -Wait -PassThru
  if($p.ExitCode -ne 0){throw "Installer failed: $($p.ExitCode)"}
  $guide=Get-Content (Join-Path $testDir 'README.md') -Raw;if(!$guide.Contains("Version $Version") -or $guide.Contains('@APP_VERSION@')){throw 'Installed release guide has the wrong version'}
  $exe=Join-Path $testDir 'SoopaDriveTool.exe';if(!(Test-Path $exe)){throw 'Installed executable missing'}
  $p=Start-Process $exe -ArgumentList '--render',"`"$preview`"" -PassThru -WindowStyle Hidden
  if(!$p.WaitForExit(30000)){$p.Kill();throw 'Installed render timeout'}
  if($p.ExitCode -ne 0 -or !(Test-Path $preview) -or (Get-Item $preview).Length -lt 20000){throw 'Installed UI render failed'}
  function Machine($file){$s=[IO.File]::OpenRead($file);try{$r=[IO.BinaryReader]::new($s);$s.Position=0x3c;$offset=$r.ReadInt32();$s.Position=$offset+4;return $r.ReadUInt16()}finally{$s.Dispose()}}
  $wimMachine=Machine (Join-Path $testDir 'Tools\wimlib\wimlib-imagex.exe');$wimDllMachine=Machine (Join-Path $testDir 'Tools\wimlib\libwim-15.dll');if(!(Test-Path (Join-Path $testDir 'Licenses\wimlib-1.14.5.tar.gz')) -or !(Test-Path (Join-Path $testDir 'Licenses\wimlib\COPYING.GPLv3.txt'))){throw 'WIM engine corresponding source/license is missing'}
  $machine=Machine $exe;$engine=Machine (Join-Path $testDir 'Tools\diskspd.exe');$smart=Machine (Join-Path $testDir 'Tools\smartmontools\smartctl.exe');if($smart -ne 0x8664){throw 'Unexpected SMART collector architecture'};if(!(Test-Path (Join-Path $testDir 'Licenses\smartmontools-7.5.tar.gz'))){throw 'SMART corresponding source is missing'};$expected=if($Runtime -eq 'win-arm64'){0xaa64}else{0x8664}
  if($machine -ne $expected -or $engine -ne $expected -or $wimMachine -ne $expected -or $wimDllMachine -ne $expected){throw 'Wrong native architecture in installed package'}
  $p=Start-Process (Join-Path $testDir 'unins000.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait -PassThru
  if($p.ExitCode -ne 0 -or (Test-Path $exe)){throw 'Uninstall failed'}
  @{Runtime=$Runtime;Installer='Passed';InstalledRender='Passed';AppMachine=('0x{0:x4}' -f $machine);EngineMachine=('0x{0:x4}' -f $engine);SmartCollectorMachine=('0x{0:x4}' -f $smart);SmartSourceIncluded=$true;WimEngineMachine=('0x{0:x4}' -f $wimMachine);WimLibraryMachine=('0x{0:x4}' -f $wimDllMachine);WimSourceAndLicensesIncluded=$true;VersionedReleaseGuideIncluded=$true;Uninstall='Passed';Utc=[DateTime]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content "$output\installer-check.json"
  Get-Content "$output\installer-check.json"
}finally{
  if(Test-Path (Join-Path $testDir 'unins000.exe')){Start-Process (Join-Path $testDir 'unins000.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait | Out-Null}
  if(Test-Path $testDir){Remove-Item $testDir -Recurse -Force}
}
