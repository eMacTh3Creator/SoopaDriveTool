param([ValidateSet('win-arm64','win-x64')][string]$Runtime='win-x64')
$ErrorActionPreference='Stop';$root=Split-Path $PSScriptRoot -Parent
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
if(!(Test-Path $vswhere)){throw 'Visual Studio C++ Build Tools and the Windows SDK are required for the native storage/recovery engine.'}
$vs=& $vswhere -latest -products '*' -property installationPath
if(!$vs){throw 'Visual Studio C++ installation was not found'}
$vc=Join-Path $vs 'Common7\Tools\VsDevCmd.bat';$arch=if($Runtime -eq 'win-arm64'){'arm64'}else{'amd64'};$hostArch=if($env:PROCESSOR_ARCHITECTURE -eq 'ARM64'){'arm64'}else{'amd64'}
$out=Join-Path $root ".tools\native\$Runtime";New-Item -ItemType Directory -Force $out|Out-Null
$command='call "'+$vc+'" -no_logo -host_arch='+$hostArch+' -arch='+$arch+' && cl.exe /nologo /std:c++20 /EHsc /W4 /WX /O2 /MT /DUNICODE /D_UNICODE "'+$root+'\native\SoopaNative.cpp" /Fo"'+$out+'\SoopaNative.obj" /Fe"'+$out+'\SoopaNative.exe" /link /INCREMENTAL:NO /DYNAMICBASE /NXCOMPAT /GUARD:CF vssapi.lib ole32.lib oleaut32.lib advapi32.lib uuid.lib'
& cmd.exe /d /s /c $command
if($LASTEXITCODE -ne 0){throw 'Native storage/recovery engine compilation failed'}
$deps=(Get-Content "$PSScriptRoot\dependencies.json" -Raw|ConvertFrom-Json).native
$depRoot=Join-Path $root '.tools\native-deps';New-Item -ItemType Directory -Force $depRoot|Out-Null
foreach($name in @('json','json_license','brotli')){
 $entry=$deps.$name;$path=Join-Path $depRoot $entry.file
 if(!(Test-Path $path)){Invoke-WebRequest $entry.url -OutFile $path -UseBasicParsing}
 if((Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256){throw ('Native dependency hash mismatch: '+$name)}
}
$brotli=Join-Path $depRoot ('brotli-'+$deps.brotli.version)
if(!(Test-Path "$brotli\c\dec\decode.c")){& tar.exe -xzf (Join-Path $depRoot $deps.brotli.file) -C $depRoot;if($LASTEXITCODE -ne 0){throw 'Brotli extraction failed'}}
$decoder=@(Get-ChildItem "$brotli\c\common\*.c" -Force;Get-ChildItem "$brotli\c\dec\*.c" -Force)
$objects=@();foreach($file in $decoder){$objects+=('"'+(Join-Path $out ($file.BaseName+'.obj'))+'"')}
$compileC='call "'+$vc+'" -no_logo -host_arch='+$hostArch+' -arch='+$arch+' && cd /d "'+$out+'" && cl.exe /nologo /c /TC /W0 /O2 /MT /I"'+$brotli+'\c\include" '+(($decoder|ForEach-Object {'"'+$_.FullName+'"'}) -join ' ')
Write-Host ('Compiling '+$decoder.Count+' decoder sources')
& cmd.exe /d /s /c $compileC
if($LASTEXITCODE -ne 0){throw 'Native decompressor build failed'}
$command='call "'+$vc+'" -no_logo -host_arch='+$hostArch+' -arch='+$arch+' && cd /d "'+$root+'\native" && rc.exe /nologo /fo "'+$out+'\Recovery.res" recovery.rc && cl.exe /nologo /std:c++20 /EHsc /W4 /WX /O2 /MT /DUNICODE /D_UNICODE /external:W0 /external:I"'+$depRoot+'" /external:I"'+$brotli+'\c\include" "'+$root+'\native\Recovery.cpp" /Fo"'+$out+'\Recovery.obj" /Fe"'+$out+'\SoopaRecovery.exe" /link /SUBSYSTEM:WINDOWS /INCREMENTAL:NO /DYNAMICBASE /NXCOMPAT /GUARD:CF '+($objects -join ' ')+' "'+$out+'\Recovery.res" bcrypt.lib crypt32.lib advapi32.lib shell32.lib shlwapi.lib comctl32.lib comdlg32.lib user32.lib gdi32.lib ole32.lib uuid.lib'
& cmd.exe /d /s /c $command
if($LASTEXITCODE -ne 0){throw 'Native recovery host compilation failed'}
Copy-Item "$depRoot\json-LICENSE.MIT","$brotli\LICENSE" $out -Force
