using System.Runtime.InteropServices;
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
        var staging=Path.Combine(workspace,"SoopaDriveTool-Clone-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);var image=Path.Combine(staging,"clone.wim");bool sourceMadeReadOnly=false;string? recovery=null;char? efi=null,volume=null;bool targetTouched=false;
        try
        {
            await Revalidate();
            progress?.Report(new("Protecting source",0,"Temporarily making the source disk read-only for a stable capture."));
            if(!source.ReadOnly){await StorageService.PowerShellAsync($"Set-Disk -Number {source.Number} -IsReadOnly $true");sourceMadeReadOnly=true;}
            if(windows&&File.Exists(Path.Combine(root,@"Windows\System32\Recovery\Winre.wim")))recovery=Path.Combine(root,@"Windows\System32\Recovery\Winre.wim");
            await Tool(SystemTool("dism.exe"),["/English","/Capture-Image","/ImageFile:"+image,"/CaptureDir:"+root,"/Name:SoopaDriveTool clone","/Compress:fast","/CheckIntegrity","/Verify","/EA"],"Capturing used files",0,35);
            var protect=source with{ReadOnly=true};var current=await StorageService.InventoryAsync();QueuePlanner.AssertUnchanged(protect,current.Single(d=>d.Number==source.Number));QueuePlanner.AssertUnchanged(target,current.Single(d=>d.Number==target.Number));
            progress?.Report(new("Preparing destination",35,"Creating fresh partition identifiers and the destination filesystem layout."));
            var letters=await CreateDestinationLayoutAsync(op,target,recovery!=null);
            targetTouched=true;volume=letters.Windows[0];if(letters.Efi.Length>0)efi=letters.Efi[0];
            await Tool(SystemTool("dism.exe"),["/English","/Apply-Image","/ImageFile:"+image,"/Index:1","/ApplyDir:"+volume+@":\","/CheckIntegrity","/Verify","/EA"],"Applying used files",40,45);
            if(windows)
            {
                progress?.Report(new("Configuring UEFI boot",85,"Building the destination EFI/BCD files. The current host firmware boot order is not changed."));
                await Tool(SystemTool("bcdboot.exe"),[volume+@":\Windows","/s",efi+":","/f","UEFI","/c","/v"],"Configuring UEFI boot",85,5);
                var efiLoader=RuntimeInformation.OSArchitecture==Architecture.Arm64?@"EFI\Boot\bootaa64.efi":@"EFI\Boot\bootx64.efi";
                if(!File.Exists(Path.Combine(efi+@":\",efiLoader))||!File.Exists(Path.Combine(efi+@":\",@"EFI\Microsoft\Boot\BCD")))throw new IOException("Destination boot-file checks failed.");
                if(recovery!=null&&letters.Recovery.Length>0)
                {
                    var dir=Path.Combine(letters.Recovery+@":\",@"Recovery\WindowsRE");Directory.CreateDirectory(dir);File.Copy(recovery,Path.Combine(dir,"Winre.wim"),false);
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
            if(efi.HasValue&&targetTouched)await StorageService.PowerShellAsync($"Get-Partition -DiskNumber {target.Number} | Where-Object DriveLetter -eq '{efi}' | Remove-PartitionAccessPath -AccessPath '{efi}:\\' -ErrorAction SilentlyContinue");
            if(Directory.Exists(staging))Directory.Delete(staging,true);
        }
        async Task Revalidate(){var now=await StorageService.InventoryAsync();OperationPolicy.Validate(op,now.Single(d=>d.Number==source.Number),now.Single(d=>d.Number==target.Number));}
        async Task Tool(string exe,string[] args,string phase,double start,double span){progress?.Report(new(phase,start,phase+"…"));var result=await ProcessTool.RunAsync(exe,args,phase,progress,start,span);if(result.ExitCode!=0)throw new IOException($"{Path.GetFileName(exe)} failed ({result.ExitCode}). {result.Error} {result.Output}");}
    }
    internal record LayoutLetters(string Windows,string Efi,string Recovery);
    internal static async Task<LayoutLetters> CreateDestinationLayoutAsync(DiskOperation op,DiskInfo target,bool recovery)
    {
        if(!StorageService.IsAdmin||target.Protected||target.ReadOnly||op.TargetDiskNumber!=target.Number||op.TargetIdentity!=target.Identity||op.TargetLayout!=target.Layout)throw new InvalidOperationException("Unvalidated clone layout request.");
        var current=(await StorageService.InventoryAsync()).Single(d=>d.Number==target.Number);QueuePlanner.AssertUnchanged(target,current);
        var request=Payload(new{Source=op.DiskNumber,Target=target.Number,Expected=target,Windows=op.CloneMode==CloneMode.WindowsUefi,Recovery=recovery});
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
    $reserve=if($r.Recovery){1024MB}else{0}
    $size=[long]([math]::Floor(($remaining-$reserve-2MB)/1MB)*1MB)
    if($size -lt 64MB){throw 'Destination Windows/data partition is too small'}
    $p=New-Partition -DiskNumber $d.Number -Size $size -AssignDriveLetter;$p|Format-Volume -FileSystem NTFS -NewFileSystemLabel 'Clone' -Confirm:$false|Out-Null;$windows=[string]$p.DriveLetter
    if($r.Recovery){$p=New-Partition -DiskNumber $d.Number -UseMaximumSize -GptType '{DE94BBA4-06D1-4D40-A16A-BFD50179D6AC}' -AssignDriveLetter;$p|Format-Volume -FileSystem NTFS -NewFileSystemLabel 'Recovery' -Confirm:$false|Out-Null;$recovery=[string]$p.DriveLetter}
    @{Windows=$windows;Efi=$efi;Recovery=$recovery}|ConvertTo-Json -Compress
    """;
}
