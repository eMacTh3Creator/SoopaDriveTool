param([ValidateSet('win-arm64','win-x64')][string]$Runtime='win-x64',[switch]$Integration,[switch]$BenchmarkOnly)
$ErrorActionPreference='Stop';$env:DOTNET_CLI_TELEMETRY_OPTOUT='1';$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try{
 & dotnet run --project tests\SoopaDriveTool.Core.Tests -c Release
 if($LASTEXITCODE -ne 0){throw 'Core storage/recovery tests failed'}
 & dotnet build tests\SoopaDriveTool.Tests -c Release -r $Runtime
 if($LASTEXITCODE -ne 0){throw 'Test build failed'}
 $args=@("$root\tests\SoopaDriveTool.Tests\bin\Release\net10.0-windows\$Runtime\SoopaDriveTool.Tests.dll");if($Integration){$args+='--integration'};if($BenchmarkOnly){$args+='--benchmark-only'}
 & dotnet @args
 if($LASTEXITCODE -ne 0){throw 'Tests failed'}
 & dotnet "$root\tests\SoopaDriveTool.Tests\bin\Release\net10.0-windows\$Runtime\SoopaDriveTool.Tests.dll" --native-store-only
 if($LASTEXITCODE -ne 0){throw 'Native encrypted recovery compatibility tests failed'}
 & dotnet "$root\tests\SoopaDriveTool.Tests\bin\Release\net10.0-windows\$Runtime\SoopaDriveTool.Tests.dll" --schedule-permissions-only
 if($LASTEXITCODE -ne 0){throw 'SYSTEM schedule ownership/access checks failed'}
}finally{Pop-Location}
