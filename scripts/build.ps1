param([ValidateSet('win-arm64','win-x64')][string]$Runtime='win-x64',[string]$Version='0.2.1',[string]$Compiler)
$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$root=Split-Path $PSScriptRoot -Parent
$tools=Join-Path $root '.tools';New-Item -ItemType Directory -Force $tools | Out-Null
$dep=(Get-Content "$PSScriptRoot\dependencies.json" -Raw | ConvertFrom-Json).diskspd
$zip=Join-Path $tools 'DiskSpd.zip'
if(!(Test-Path $zip)){Invoke-WebRequest $dep.url -OutFile $zip -UseBasicParsing}
if((Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $dep.sha256){throw 'DiskSpd dependency SHA-256 mismatch'}
$expanded=Join-Path $tools 'diskspd-upstream';Expand-Archive $zip $expanded -Force
$native=if($Runtime -eq 'win-arm64'){'arm64'}else{'amd64'}
$engine=Join-Path $tools "diskspd\$Runtime";New-Item -ItemType Directory -Force $engine | Out-Null;Copy-Item "$expanded\$native\diskspd.exe" "$engine\diskspd.exe" -Force
$smart=(Get-Content "$PSScriptRoot\dependencies.json" -Raw | ConvertFrom-Json).smartmontools
$smartDir=Join-Path $tools 'smartmontools';New-Item -ItemType Directory -Force $smartDir|Out-Null
if(!(Test-Path "$smartDir\smartctl.exe")){
 $smartInstaller=Join-Path $tools 'smartmontools-setup.exe'
 if(!(Test-Path $smartInstaller)){Invoke-WebRequest $smart.url -OutFile $smartInstaller -UseBasicParsing}
 if((Get-FileHash $smartInstaller).Hash.ToLowerInvariant() -ne $smart.sha256){throw 'SMART installer checksum mismatch'}
 $seven=(Get-Command 7z.exe -ErrorAction SilentlyContinue).Source
 if(!$seven){$seven='C:\Program Files\7-Zip\7z.exe'}
 if(!(Test-Path $seven)){throw '7-Zip is required to extract the pinned SMART collector (no service installation).'}
 & $seven x -y "-o$tools\smart-extract" $smartInstaller 'bin/smartctl.exe' 'bin/drivedb.h' | Out-Null
 if($LASTEXITCODE -ne 0){throw 'SMART extraction failed'}
 Copy-Item "$tools\smart-extract\bin\smartctl.exe","$tools\smart-extract\bin\drivedb.h" $smartDir -Force
}
if((Get-FileHash "$smartDir\smartctl.exe").Hash.ToLowerInvariant() -ne $smart.smartctl_sha256 -or (Get-FileHash "$smartDir\drivedb.h").Hash.ToLowerInvariant() -ne $smart.drivedb_sha256){throw 'SMART engine/database checksum mismatch'}
$smartSource=Join-Path $tools 'smartmontools-7.5.tar.gz'
if(!(Test-Path $smartSource)){Invoke-WebRequest $smart.source_url -OutFile $smartSource -UseBasicParsing}
if((Get-FileHash $smartSource).Hash.ToLowerInvariant() -ne $smart.source_sha256){throw 'SMART corresponding-source checksum mismatch'}
$wim=(Get-Content "$PSScriptRoot\dependencies.json" -Raw|ConvertFrom-Json).wimlib;$wimArch=$wim.architectures.$Runtime
$wimZip=Join-Path $tools "wimlib-$Runtime.zip"
if(!(Test-Path $wimZip)){Invoke-WebRequest $wimArch.url -OutFile $wimZip -UseBasicParsing}
if((Get-FileHash $wimZip).Hash.ToLowerInvariant() -ne $wimArch.sha256){throw 'WIM engine archive checksum mismatch'}
$wimDir=Join-Path $tools "wimlib\$Runtime";Expand-Archive $wimZip $wimDir -Force
if((Get-FileHash "$wimDir\wimlib-imagex.exe").Hash.ToLowerInvariant() -ne $wimArch.exe_sha256 -or (Get-FileHash "$wimDir\libwim-15.dll").Hash.ToLowerInvariant() -ne $wimArch.dll_sha256){throw 'WIM executable/library checksum mismatch'}
$wimSource=Join-Path $tools 'wimlib-1.14.5.tar.gz'
if(!(Test-Path $wimSource)){Invoke-WebRequest $wim.source_url -OutFile $wimSource -UseBasicParsing}
if((Get-FileHash $wimSource).Hash.ToLowerInvariant() -ne $wim.source_sha256){throw 'WIM corresponding-source checksum mismatch'}
$output=Join-Path $root "artifacts\$Runtime";$publish=Join-Path $output 'publish';New-Item -ItemType Directory -Force $output | Out-Null
if(Test-Path $publish){Remove-Item $publish -Recurse -Force}
Push-Location $root
try{
 & dotnet publish src\SoopaDriveTool.Windows -c Release -r $Runtime --self-contained true -p:Version=$Version -p:PublishSingleFile=false -o $publish
 if($LASTEXITCODE -ne 0){throw 'Publish failed'}
 if(!(Test-Path "$publish\Tools\diskspd.exe")){throw 'Native benchmark engine missing from publish output'}
 Copy-Item "$root\LICENSE","$root\THIRD-PARTY-NOTICES.txt" $publish
 $guide=(Get-Content "$root\docs\PACKAGE-README.md" -Raw).Replace('@APP_VERSION@',$Version);[IO.File]::WriteAllText("$publish\README.md",$guide,[Text.UTF8Encoding]::new($false))
 Copy-Item "$root\docs\Smartmontools-COPYING.txt" $publish
 New-Item -ItemType Directory -Force "$publish\Licenses"|Out-Null
 Copy-Item $smartSource "$publish\Licenses\smartmontools-7.5.tar.gz"
 Copy-Item $wimSource "$publish\Licenses\wimlib-1.14.5.tar.gz"
 Copy-Item "$root\docs\wimlib" "$publish\Licenses\wimlib" -Recurse
 Copy-Item "$root\docs\DotNet-LICENSE.txt","$root\docs\DotNet-THIRD-PARTY-NOTICES.txt","$root\docs\WPF-LICENSE.txt","$root\docs\WPF-THIRD-PARTY-NOTICES.txt" $publish
 if(!$Compiler){$candidates=@("$tools\InnoSetup\ISCC.exe",'C:\Program Files (x86)\Inno Setup 6\ISCC.exe','C:\Program Files (x86)\Inno Setup 7\ISCC.exe','C:\Program Files\Inno Setup 7\ISCC.exe');$Compiler=$candidates | Where-Object {Test-Path $_} | Select-Object -First 1}
 if(!$Compiler){throw 'Install Inno Setup 6.7+ or pass -Compiler with the ISCC.exe path.'}
 & $Compiler /Qp "/DAppVersion=$Version" "/DRuntime=$Runtime" "/DPublishDir=$publish" "/DOutDir=$output" "$PSScriptRoot\installer.iss"
 if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
 $arch=$Runtime.Replace('win-','');$portable=Join-Path $output "SoopaDriveTool-$Version-$arch-Portable.zip"
 if(Test-Path $portable){Remove-Item $portable}
 Compress-Archive "$publish\*" $portable -CompressionLevel Optimal
 Get-ChildItem $output -File | Where-Object {$_.Extension -in '.exe','.zip' -and $_.Name -like "SoopaDriveTool-$Version-*"} | Sort-Object Name | ForEach-Object {"$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)"} | Set-Content "$output\SHA256SUMS.txt"
 Write-Host "Built $Runtime packages: $output"
}finally{Pop-Location}
