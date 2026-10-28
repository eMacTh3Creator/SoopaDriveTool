param([ValidateSet('win-arm64','win-x64')][string]$Runtime='win-x64',[string]$Version='0.1.0',[string]$Compiler)
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
$output=Join-Path $root "artifacts\$Runtime";$publish=Join-Path $output 'publish';New-Item -ItemType Directory -Force $output | Out-Null
if(Test-Path $publish){Remove-Item $publish -Recurse -Force}
Push-Location $root
try{
 & dotnet publish src\SoopaDriveTool.Windows -c Release -r $Runtime --self-contained true -p:Version=$Version -p:PublishSingleFile=false -o $publish
 if($LASTEXITCODE -ne 0){throw 'Publish failed'}
 if(!(Test-Path "$publish\Tools\diskspd.exe")){throw 'Native benchmark engine missing from publish output'}
 Copy-Item "$root\README.md","$root\LICENSE","$root\THIRD-PARTY-NOTICES.txt" $publish
 Copy-Item "$root\docs\DotNet-LICENSE.txt","$root\docs\DotNet-THIRD-PARTY-NOTICES.txt","$root\docs\WPF-LICENSE.txt","$root\docs\WPF-THIRD-PARTY-NOTICES.txt" $publish
 if(!$Compiler){$candidates=@("$tools\InnoSetup\ISCC.exe",'C:\Program Files (x86)\Inno Setup 6\ISCC.exe','C:\Program Files (x86)\Inno Setup 7\ISCC.exe','C:\Program Files\Inno Setup 7\ISCC.exe');$Compiler=$candidates | Where-Object {Test-Path $_} | Select-Object -First 1}
 if(!$Compiler){throw 'Install Inno Setup 6.7+ or pass -Compiler with the ISCC.exe path.'}
 & $Compiler /Qp "/DAppVersion=$Version" "/DRuntime=$Runtime" "/DPublishDir=$publish" "/DOutDir=$output" "$PSScriptRoot\installer.iss"
 if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
 $arch=$Runtime.Replace('win-','');$portable=Join-Path $output "SoopaDriveTool-$Version-$arch-Portable.zip"
 if(Test-Path $portable){Remove-Item $portable}
 Compress-Archive "$publish\*" $portable -CompressionLevel Optimal
 Get-ChildItem $output -File | Where-Object {$_.Extension -in '.exe','.zip'} | Sort-Object Name | ForEach-Object {"$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)"} | Set-Content "$output\SHA256SUMS.txt"
 Write-Host "Built $Runtime packages: $output"
}finally{Pop-Location}
