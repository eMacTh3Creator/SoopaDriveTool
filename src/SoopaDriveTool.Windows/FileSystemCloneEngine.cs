using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;
using System.Text;
using System.Text.Json;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public static class FileSystemCloneEngine
{
    static string SystemTool(string file)=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32",file);
    static string Payload(object value)=>Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value,Data.Json)));
    public static async Task<string> ExecuteAsync(DiskOperation op,DiskInfo source,DiskInfo target,IProgress<DriveProgress>? progress)
    {
        var p=source.Partitions.Single(p=>p.Number==op.PartitionNumber);var root=p.Letter+@":\";bool windows=op.CloneMode==CloneMode.WindowsUefi;
        if(windows)
        {
            if(!File.Exists(Path.Combine(root,@"Windows\System32\Config\SYSTEM"))||!File.Exists(Path.Combine(root,@"Windows\System32\Config\BCD-Template")))throw new IOException("The selected volume is not a complete offline Windows installation.");
            var loader=Path.Combine(root,@"Windows\System32\winload.efi");if(!File.Exists(loader))throw new IOException("UEFI Windows loader is missing.");
            using var f=File.OpenRead(loader);using var b=new BinaryReader(f);f.Position=0x3c;int offset=b.ReadInt32();f.Position=offset+4;var arch=b.ReadUInt16();var expected=RuntimeInformation.OSArchitecture==Architecture.Arm64?0xaa64:0x8664;
            if(arch!=expected)throw new IOException("Run this Windows-aware clone on a host with the same Windows architecture as the source.");
        }
        var workspace=Path.GetFullPath(string.IsNullOrWhiteSpace(op.Workspace)?Path.GetTempPath():op.Workspace);BackupEngine.RejectExistingAncestorLinks(workspace);
        if(!Directory.Exists(workspace)||workspace.StartsWith(@"\\"))throw new IOException("Choose an existing local scratch folder for the WIM staging file.");
        char drive=char.ToUpperInvariant(Path.GetPathRoot(workspace)![0]);var workspaceDisk=int.Parse(await StorageService.PowerShellAsync($"(Get-Partition -DriveLetter '{drive}').DiskNumber"));
        if(workspaceDisk==source.Number||workspaceDisk==target.Number)throw new IOException("The WIM scratch folder must be on a third disk, separate from source and destination.");
        long used=p.Size-p.Free;if(new DriveInfo(Path.GetPathRoot(workspace)!).AvailableFreeSpace<used+512L*1024*1024)throw new IOException("Not enough scratch space for the used-file WIM capture.");
        var staging=Path.Combine(workspace,"SoopaDriveTool-Clone-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);var image=Path.Combine(staging,"clone.wim");bool sourceMadeReadOnly=false;string? recovery=null;char? efi=null,volume=null,recoveryLetter=null;bool targetTouched=false;
        try
        {
            await Revalidate();
            progress?.Report(new("Protecting source",0,"Temporarily making the source disk read-only for a stable capture."));
            if(!source.ReadOnly){await StorageService.PowerShellAsync($"Set-Disk -Number {source.Number} -IsReadOnly $true");sourceMadeReadOnly=true;}
            long recoveryBytes=0;
            if(windows)
            {
                progress?.Report(new("Finding Windows recovery",0,"Checking the selected source disk's registered and hidden recovery locations."));
                recovery=await WindowsRecovery.FindAsync(source,p);
                if(recovery!=null)recoveryBytes=WindowsRecovery.ReserveBytes(new FileInfo(recovery).Length);
                if(target.Size<used+recoveryBytes+300L*1024*1024)throw new IOException("The destination cannot hold the Windows files, EFI layout and recovery image with servicing space.");
            }
            await Tool(SystemTool("dism.exe"),["/English","/Capture-Image","/ImageFile:"+image,"/CaptureDir:"+root,"/Name:SoopaDriveTool clone","/Compress:fast","/CheckIntegrity","/Verify","/EA"],"Capturing used files",0,35);
            var protect=source with{ReadOnly=true};var current=await StorageService.InventoryAsync();QueuePlanner.AssertUnchanged(protect,current.Single(d=>d.Number==source.Number));QueuePlanner.AssertUnchanged(target,current.Single(d=>d.Number==target.Number));
            progress?.Report(new("Preparing destination",35,"Creating fresh partition identifiers and the destination filesystem layout."));
            var letters=await CreateDestinationLayoutAsync(op,target,recovery!=null,recoveryBytes);
            targetTouched=true;volume=letters.Windows[0];if(letters.Efi.Length>0)efi=letters.Efi[0];if(letters.Recovery.Length>0)recoveryLetter=letters.Recovery[0];
            await Tool(SystemTool("dism.exe"),["/English","/Apply-Image","/ImageFile:"+image,"/Index:1","/ApplyDir:"+volume+@":\","/CheckIntegrity","/Verify","/EA"],"Applying used files",40,45);
            if(windows)
            {
                progress?.Report(new("Preserving Windows drive letter",85,"Remapping the cloned SYSTEM hive to the destination volume identifier."));
                await RemapOfflineWindowsVolumeAsync(source with{Partitions=[p]},target,volume!.Value);
                progress?.Report(new("Configuring UEFI boot",86,"Building the destination EFI/BCD files. The current host firmware boot order is not changed."));
                await Tool(SystemTool("bcdboot.exe"),[volume+@":\Windows","/s",efi+":","/f","UEFI","/c","/v"],"Configuring UEFI boot",86,4);
                var efiLoader=RuntimeInformation.OSArchitecture==Architecture.Arm64?@"EFI\Boot\bootaa64.efi":@"EFI\Boot\bootx64.efi";
                if(!File.Exists(Path.Combine(efi+@":\",efiLoader))||!File.Exists(Path.Combine(efi+@":\",@"EFI\Microsoft\Boot\BCD")))throw new IOException("Destination boot-file checks failed.");
                if(recovery!=null&&letters.Recovery.Length>0)
                {
                    var dir=Path.Combine(letters.Recovery+@":\",@"Recovery\WindowsRE");Directory.CreateDirectory(dir);var copy=Path.Combine(dir,"Winre.wim");File.Copy(recovery,copy,false);
                    using(var original=File.OpenRead(recovery))using(var copied=File.OpenRead(copy))
                    {if(!SHA256.HashData(original).SequenceEqual(SHA256.HashData(copied)))throw new IOException("Windows recovery image verification failed.");}
                    await Tool(SystemTool("reagentc.exe"),["/Setreimage","/Path",dir,"/Target",volume+@":\Windows"],"Registering Windows recovery",90,3);
                }
            }
            progress?.Report(new("Verifying filesystem",94,"Checking the applied filesystem. DISM verified file resources during capture and apply."));
            await StorageService.PowerShellAsync($"$scan=Repair-Volume -DriveLetter '{volume}' -Scan -ErrorAction Stop; if($scan -and [string]$scan -ne 'NoErrorsFound'){{throw ('NTFS scan result: '+[string]$scan)}}");
            var after=(await StorageService.InventoryAsync()).Single(d=>d.Number==target.Number);
            if(after.Identity!=target.Identity||after.Protected||after.Style!="GPT"||!after.Partitions.Any(p=>p.Letter==volume.ToString()&&p.FileSystem=="NTFS"))throw new IOException("Destination layout verification failed.");
            progress?.Report(new("Verified",100,windows?"Windows image, NTFS layout, EFI loader and BCD store verified. Independent boot testing is still required.":"Used-file data clone applied and verified."));
            return windows?"Offline Windows-aware clone complete: fresh GPT/EFI/MSR/Windows layout, verified WIM resources, EFI loader and BCD store. Disconnect the source and independently boot-test the destination. Hardware binding, BitLocker, activation and all firmware configurations are not guaranteed.":"Filesystem-aware NTFS clone complete. Used files were captured/applied with DISM integrity verification; unused sectors were not copied.";
        }
        finally
        {
            if(sourceMadeReadOnly)
            {var now=(await StorageService.InventoryAsync()).SingleOrDefault(d=>d.Number==source.Number);if(now?.Identity==source.Identity)await StorageService.PowerShellAsync($"Set-Disk -Number {source.Number} -IsReadOnly $false");}
            if(targetTouched)foreach(var letter in new[]{efi,recoveryLetter}.Where(l=>l.HasValue))
                await StorageService.PowerShellAsync($"Get-Partition -DiskNumber {target.Number} | Where-Object DriveLetter -eq '{letter}' | Remove-PartitionAccessPath -AccessPath '{letter}:\\' -ErrorAction Stop");
            if(Directory.Exists(staging))Directory.Delete(staging,true);
        }
        async Task Revalidate(){var now=await StorageService.InventoryAsync();OperationPolicy.Validate(op,now.Single(d=>d.Number==source.Number),now.Single(d=>d.Number==target.Number));}
        async Task Tool(string exe,string[] args,string phase,double start,double span){progress?.Report(new(phase,phase=="Capturing used files"?null:start,phase=="Capturing used files"?"Scanning source files before capture. DISM reports measured percentages after enumeration.":phase+"…"));var result=await ProcessTool.RunAsync(exe,args,phase,progress,start,span);if(result.ExitCode!=0)throw new IOException($"{Path.GetFileName(exe)} failed ({result.ExitCode}). {result.Error} {result.Output}");}
    }
    // Change only the destination's offline hive. Preserve its original Windows
    // letter rather than guessing C: or clearing every other drive mapping.
    internal static async Task RemapOfflineWindowsVolumeAsync(DiskInfo source,DiskInfo target,char destinationLetter)
    {
        var disks=await StorageService.InventoryAsync();
        var actualSource=disks.Single(d=>d.Number==source.Number);var actualTarget=disks.Single(d=>d.Number==target.Number);
        if(!StorageService.IsAdmin||source.Number==target.Number||actualSource.Protected||actualTarget.Protected||actualSource.Identity!=source.Identity||actualTarget.Identity!=target.Identity||actualTarget.ReadOnly)throw new IOException("Offline Windows mapping target is no longer safe.");
        var sourcePartition=source.Partitions.Single(p=>p.Letter.Length==1&&File.Exists(Path.Combine(p.Letter+@":\",@"Windows\System32\Config\SYSTEM")));
        // Use the explicitly selected source volume, not any Windows directory on
        // another partition. The caller supplies a source record filtered to it.
        var liveSource=actualSource.Partitions.SingleOrDefault(p=>p.Number==sourcePartition.Number);
        if(liveSource==null||liveSource.Offset!=sourcePartition.Offset||liveSource.Size!=sourcePartition.Size||liveSource.Letter!=sourcePartition.Letter)throw new IOException("The selected source volume changed before offline registry mapping.");
        var targetPartition=actualTarget.Partitions.Single(p=>p.Letter==destinationLetter.ToString()&&p.FileSystem=="NTFS");
        var hive=Path.Combine(targetPartition.Letter+@":\",@"Windows\System32\Config\SYSTEM");BackupEngine.RejectExistingAncestorLinks(hive);
        if(!File.Exists(hive))throw new IOException("The applied destination SYSTEM hive is missing.");
        using var mappings=Registry.LocalMachine.OpenSubKey("SYSTEM\\MountedDevices")??throw new IOException("Windows volume identifiers are unavailable.");
        byte[] Map(string letter)=>mappings.GetValue(@"\DosDevices\"+letter+":") as byte[]??throw new IOException("No persistent identifier for volume "+letter+".");
        var from=Map(sourcePartition.Letter);var to=Map(targetPartition.Letter);
        if(from.SequenceEqual(to)||from.Length==0||to.Length==0)throw new IOException("Source and destination volume identifiers are not distinct.");
        var name="SoopaDriveTool_Offline_"+Guid.NewGuid().ToString("N");var key=@"HKLM\"+name;bool loaded=false;
        async Task Reg(params string[] args){var result=await ProcessTool.RunAsync(SystemTool("reg.exe"),args);if(result.ExitCode!=0)throw new IOException("Offline registry operation failed: "+result.Error+result.Output);}
        try
        {
            await Reg("load",key,hive);loaded=true;
            using var offline=Registry.LocalMachine.OpenSubKey(name+"\\MountedDevices",true)??throw new IOException("The offline Windows image has no drive-letter mappings.");
            var matching=offline.GetValueNames().Where(n=>offline.GetValue(n) is byte[] data&&data.SequenceEqual(from)).ToArray();
            if(matching.Count(n=>n.StartsWith(@"\DosDevices\",StringComparison.OrdinalIgnoreCase)&&n.Length==14)!=1)throw new IOException("The original Windows drive letter cannot be identified unambiguously. Use a byte-exact clone or qualify this image manually.");
            foreach(var value in matching)offline.SetValue(value,to,RegistryValueKind.Binary);
            offline.Flush();
        }
        finally{if(loaded)await Reg("unload",key);}
    }
    internal record LayoutLetters(string Windows,string Efi,string Recovery);
    internal static async Task<LayoutLetters> CreateDestinationLayoutAsync(DiskOperation op,DiskInfo target,bool recovery,long recoveryBytes=1024L*1024*1024)
    {
        if(!StorageService.IsAdmin||target.Protected||target.ReadOnly||op.TargetDiskNumber!=target.Number||op.TargetIdentity!=target.Identity||op.TargetLayout!=target.Layout)throw new InvalidOperationException("Unvalidated clone layout request.");
        var current=(await StorageService.InventoryAsync()).Single(d=>d.Number==target.Number);QueuePlanner.AssertUnchanged(target,current);
        var request=Payload(new{Source=op.DiskNumber,Target=target.Number,Expected=target,Windows=op.CloneMode==CloneMode.WindowsUefi,Recovery=recovery,RecoveryBytes=recovery?Math.Max(1024L*1024*1024,recoveryBytes):0});
        var json=await StorageService.PowerShellAsync("$r=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+request+"'))|ConvertFrom-Json; "+PrepareScript);
        return JsonSerializer.Deserialize<LayoutLetters>(json,Data.Json)??throw new IOException("Destination layout was not returned.");
    }
    const string PrepareScript="""
    $ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue'
    $d=Get-Disk -Number $r.Target
    if($d.IsBoot -or $d.IsSystem -or $d.IsReadOnly -or [string]$d.UniqueId -cne $r.Expected.UniqueId -or [string]$d.SerialNumber -cne $r.Expected.Serial -or [long]$d.Size -ne $r.Expected.Size){throw 'Clone destination changed or is protected'}
    if($d.IsOffline){Set-Disk -Number $d.Number -IsOffline $false}
    if($d.PartitionStyle -ne 'RAW'){Clear-Disk -Number $d.Number -RemoveData -RemoveOEM -Confirm:$false}
    Initialize-Disk -Number $d.Number -PartitionStyle GPT | Out-Null
    $efi='';$recovery=''
    if($r.Windows){$p=New-Partition -DiskNumber $d.Number -Size 260MB -GptType '{C12A7328-F81F-11D2-BA4B-00A0C93EC93B}' -AssignDriveLetter;$p|Format-Volume -FileSystem FAT32 -NewFileSystemLabel 'SYSTEM' -Confirm:$false|Out-Null;$efi=[string]$p.DriveLetter}
    $remaining=(Get-Disk -Number $d.Number).LargestFreeExtent
    $reserve=[long]$r.RecoveryBytes
    $size=[long]([math]::Floor(($remaining-$reserve-2MB)/1MB)*1MB)
    if($size -lt 64MB){throw 'Destination Windows/data partition is too small'}
    $p=New-Partition -DiskNumber $d.Number -Size $size -AssignDriveLetter;$p|Format-Volume -FileSystem NTFS -NewFileSystemLabel 'Clone' -Confirm:$false|Out-Null;$windows=[string]$p.DriveLetter
    if($r.Recovery){$p=New-Partition -DiskNumber $d.Number -UseMaximumSize -GptType '{DE94BBA4-06D1-4D40-A16A-BFD50179D6AC}' -AssignDriveLetter;$p|Format-Volume -FileSystem NTFS -NewFileSystemLabel 'Recovery' -Confirm:$false|Out-Null;$recovery=[string]$p.DriveLetter;
      $script=[IO.Path]::GetTempFileName()
      try {
        @("select disk $($d.Number)","select partition $($p.PartitionNumber)",'gpt attributes=0x8000000000000001','detail partition','exit')|Set-Content -LiteralPath $script -Encoding ASCII
        $log=& "$env:windir\System32\diskpart.exe" /s $script
        $code=$LASTEXITCODE
        if($code -ne 0 -or (($log -join ' ') -notmatch '(?i)\b0x8000000000000001\b')){throw ('Recovery partition attributes could not be set: '+($log -join ' '))}
      } finally {Remove-Item -LiteralPath $script -Force}
    }
    @{Windows=$windows;Efi=$efi;Recovery=$recovery}|ConvertTo-Json -Compress
    """;
}
