using System.Runtime.InteropServices;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public static class RecoveryMediaService
{
    public static string DefaultAdk=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),@"Windows Kits\10\Assessment and Deployment Kit");
    static string Q(string s){if(s.IndexOfAny(['%','!','"','\r','\n'])>=0)throw new IOException("Recovery build paths cannot contain percent, exclamation, quote or newline characters.");return '"'+s+'"';}
    public static async Task BuildIsoAsync(string iso,string adk,IReadOnlyList<string> drivers,IProgress<DriveProgress>? progress=null,CancellationToken ct=default)
    {
        if(!StorageService.IsAdmin)throw new UnauthorizedAccessException("Administrator access is required to mount and prepare WinPE.");if(RuntimeInformation.ProcessArchitecture!=RuntimeInformation.OSArchitecture)throw new IOException("Use the native ARM64 edition on ARM Windows to build firmware-compatible recovery media.");BackupEngine.RejectExistingAncestorLinks(iso);if(File.Exists(iso))throw new IOException("Choose a new ISO filename.");string arch=RuntimeInformation.ProcessArchitecture==Architecture.Arm64?"arm64":"amd64";string pe=Path.Combine(adk,"Windows Preinstallation Environment"),environment=Path.Combine(adk,@"Deployment Tools\DandISetEnv.bat");if(!File.Exists(environment)||!File.Exists(Path.Combine(pe,"copype.cmd")))throw new IOException("Install Microsoft Windows ADK Deployment Tools and the matching WinPE add-on first.");
        string host=Path.Combine(AppContext.BaseDirectory,"Tools","SoopaRecovery.exe");if(!File.Exists(host))throw new IOException("The native recovery host is missing. Reinstall the complete app package.");
        string folder=Path.Combine(Data.Home,"recovery-build-"+Guid.NewGuid().ToString("N"));string mount=Path.Combine(folder,"mount");bool mounted=false;
        try
        {
            progress?.Report(new("Copying Windows PE",0,"Preparing architecture-matched Microsoft boot files from the installed ADK."));Directory.CreateDirectory(Path.GetDirectoryName(iso)!);
            await Batch("call "+Q(environment)+" && call "+Q(Path.Combine(pe,"copype.cmd"))+" "+arch+" "+Q(folder),ct);string wim=Path.Combine(folder,@"media\sources\boot.wim");
            await Dism(["/Mount-Image","/ImageFile:"+wim,"/Index:1","/MountDir:"+mount],"Mounting recovery image",ct);mounted=true;
            string app=Path.Combine(mount,"Soopa");Directory.CreateDirectory(app);File.Copy(host,Path.Combine(app,"SoopaRecovery.exe"));File.Copy(Path.Combine(AppContext.BaseDirectory,"Tools","SoopaNative.exe"),Path.Combine(app,"SoopaNative.exe"));var logo=Path.Combine(AppContext.BaseDirectory,"Assets","app.ico");if(File.Exists(logo))File.Copy(logo,Path.Combine(app,"app.ico"));
            foreach(string driver in drivers){BackupEngine.RejectExistingAncestorLinks(driver);if(!Directory.Exists(driver))throw new IOException("Driver folder is missing: "+driver);await Dism(["/Image:"+mount,"/Add-Driver","/Driver:"+driver,"/Recurse"],"Injecting storage/network drivers",ct);}
            await File.WriteAllTextAsync(Path.Combine(mount,@"Windows\System32\startnet.cmd"),"@echo off\r\nwpeinit\r\nX:\\Soopa\\SoopaRecovery.exe\r\n",ct);
            await File.WriteAllTextAsync(Path.Combine(app,"README.txt"),"SoopaDriveTool recovery: architecture "+arch+". Boot files are supplied by your Microsoft ADK installation. Use the desktop app to prepare and verify protected disk/Windows images. Keep recovery passphrases separately. Recover to a different disk. ATA erase requires a direct unfrozen ATA/SATA device.\r\n",ct);
            progress?.Report(new("Committing recovery environment",65,"Saving the native recovery app and driver additions into the boot image."));await Dism(["/Unmount-Image","/MountDir:"+mount,"/Commit"],"Committing boot image",ct);mounted=false;
            await Batch("call "+Q(environment)+" && call "+Q(Path.Combine(pe,"MakeWinPEMedia.cmd"))+" /ISO "+Q(folder)+" "+Q(iso)+" /bootex",ct);if(!File.Exists(iso)||new FileInfo(iso).Length<64L*1024*1024)throw new IOException("The ISO was not produced correctly.");
            progress?.Report(new("Recovery ISO built",100,"Microsoft boot files, native SoopaDriveTool host and selected drivers are packaged. Boot-test this ISO with your storage before relying on it."));
        }
        finally
        {
            if(mounted){try{await Dism(["/Unmount-Image","/MountDir:"+mount,"/Discard"],"Discarding interrupted build",CancellationToken.None);}catch{}}
            if(!mounted&&Directory.Exists(folder))Directory.Delete(folder,true);
        }
        async Task Dism(string[] args,string phase,CancellationToken token){progress?.Report(new(phase,null,phase+"…"));await ProtectedImageService.Tool(Path.Combine(Environment.SystemDirectory,"dism.exe"),new[]{"/English"}.Concat(args).ToArray(),phase,progress,token);}
        async Task Batch(string script,CancellationToken token)
        {
            Directory.CreateDirectory(Data.Home);string file=Path.Combine(Data.Home,"pe-command-"+Guid.NewGuid().ToString("N")+".cmd");Q(file);
            try
            {
                await File.WriteAllTextAsync(file,"@echo off\r\nchcp 65001 >nul\r\n"+script+"\r\nexit /b %errorlevel%\r\n",new System.Text.UTF8Encoding(false),token);
                // cmd uses its own quoting rules; ArgumentList would C-escape
                // embedded quotes as backslash-quote, which cmd treats literally.
                var start=new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"cmd.exe")){Arguments="/d /s /c \"\""+file+"\"\"",UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8};
                using var process=System.Diagnostics.Process.Start(start)??throw new IOException("Cannot start the Windows PE builder.");var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();try{await process.WaitForExitAsync(token);}catch{try{process.Kill(true);}catch{}throw;}string text=await output,errors=await error;if(process.ExitCode!=0)throw new IOException("Windows PE build failed: "+text+errors);
            }
            finally{if(File.Exists(file))File.Delete(file);}
        }
    }
    public static async Task WriteUsbAsync(string iso,DiskInfo target,IProgress<DriveProgress>? progress=null)
    {
        if(!StorageService.IsAdmin||target.Protected||target.ReadOnly||target.Size>256L*1024*1024*1024||target.Bus!="USB")throw new IOException("Choose a writable USB data disk of at most 256 GiB.");BackupEngine.RejectExistingAncestorLinks(iso);if(!File.Exists(iso))throw new IOException("Choose a built recovery ISO.");await DiskAccess.RequireSeparatePathAsync(iso,target.Number);var current=(await StorageService.InventoryAsync()).Single(d=>d.Number==target.Number);QueuePlanner.AssertUnchanged(target,current);
        string path=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(iso)));var expected=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(target,Data.Json)));
        await StorageService.PowerShellAsync("$iso=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+path+"'));$expected=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+expected+"'))|ConvertFrom-Json;"+UsbScript,progress:progress);
    }
    const string UsbScript="""
    $ErrorActionPreference='Stop';$d=Get-Disk -Number $expected.Number
    if($d.IsBoot -or $d.IsSystem -or $d.IsReadOnly -or [string]$d.UniqueId -cne $expected.UniqueId -or [string]$d.SerialNumber -cne $expected.Serial -or [long]$d.Size -ne $expected.Size -or [string]$d.BusType -ne 'USB'){throw 'USB destination changed or is protected'}
    $mounted=$false
    try {
      $image=Mount-DiskImage -ImagePath $iso -PassThru;$mounted=$true;$v=$image|Get-Volume
      if(!$v.DriveLetter){throw 'Recovery ISO did not mount'}
      $root=[string]$v.DriveLetter+':\'
      if(!(Test-Path ($root+'sources\boot.wim')) -or !(Test-Path ($root+'EFI\Boot'))){throw 'Not a Windows recovery ISO'}
      if((Get-Item ($root+'sources\boot.wim')).Length -ge 4GB){throw 'boot.wim exceeds FAT32 limit; build without oversized driver folders'}
      if($d.IsOffline){Set-Disk -Number $d.Number -IsOffline $false}
      if($d.PartitionStyle -ne 'RAW'){Clear-Disk -Number $d.Number -RemoveData -RemoveOEM -Confirm:$false}
      Initialize-Disk -Number $d.Number -PartitionStyle GPT|Out-Null
      $size=[long][math]::Min($d.Size-32MB,30GB);$p=New-Partition -DiskNumber $d.Number -Size $size -AssignDriveLetter -GptType '{C12A7328-F81F-11D2-BA4B-00A0C93EC93B}'
      $p|Format-Volume -FileSystem FAT32 -NewFileSystemLabel SoopaRecover -Confirm:$false|Out-Null
      $dest=[string]$p.DriveLetter+':\';$copy=& robocopy.exe $root $dest /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
      if($LASTEXITCODE -gt 7){throw ('Recovery USB copy failed: '+($copy -join ' '))}
      foreach($f in Get-ChildItem $root -File -Recurse){$relative=$f.FullName.Substring($root.Length);$to=Join-Path $dest $relative;if(!(Test-Path -LiteralPath $to) -or (Get-FileHash -LiteralPath $f.FullName).Hash -ne (Get-FileHash -LiteralPath $to).Hash){throw ('Recovery USB verification failed: '+$relative)}}
    } finally {if($mounted){Dismount-DiskImage -ImagePath $iso|Out-Null}}
    'Recovery USB files copied and every file hash verified. Independently boot-test the USB on the intended machine.'
    """;
}
