using System.Text;
using System.Text.Json;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public static class AdvancedPartitionService
{
    public static bool Handles(OperationKind kind)=>kind is OperationKind.MovePartition or OperationKind.ConvertTable or OperationKind.SetPartitionKind or OperationKind.SplitPartition or OperationKind.MergePartition or OperationKind.ChangeFileSystem or OperationKind.ChangeClusterSize or OperationKind.RedistributeSpace;
    public static async Task<string> ExecuteAsync(DiskOperation op,DiskInfo disk,IProgress<DriveProgress>? progress)
    {
        if(!StorageService.IsAdmin||disk.Protected)throw new IOException("Select an inactive data disk.");
        if(op.Kind==OperationKind.RedistributeSpace)return await RedistributeAsync(op,disk,progress);
        if(op.Kind is OperationKind.MovePartition or OperationKind.ConvertTable or OperationKind.SetPartitionKind)
        {
            await DiskAccess.RequireSeparatePathAsync(op.Workspace,disk.Number);if(!disk.Offline)throw new IOException("Take this disk offline before partition-table or location changes.");
            await using var raw=DiskAccess.Open(disk,true);DiskTable table=DiskTables.Read(raw,disk.SectorSize);int tableIndex=op.PartitionNumber==0?0:table.Partitions.Single(p=>p.Offset==disk.Partitions.Single(x=>x.Number==op.PartitionNumber).Offset).Index;string folder=Path.Combine(op.Workspace,"SoopaDriveTool-Recovery-"+op.Id.ToString("N"));BackupEngine.RejectExistingAncestorLinks(folder);Directory.CreateDirectory(folder);
            string tableBackup=Path.Combine(folder,"original-layout.json");if(!File.Exists(tableBackup))await Data.SaveAsync(tableBackup,table);
            if(op.Kind==OperationKind.MovePartition)
            {
                string journal=Path.Combine(folder,"move.json");if(File.Exists(journal)){var saved=JsonSerializer.Deserialize<MoveCheckpoint>(await File.ReadAllTextAsync(journal),Data.Json)??throw new IOException("Invalid move journal.");table=saved.OriginalTable??throw new IOException("Missing original move layout.");}
                await ResumableMove.MoveAsync(raw,table,tableIndex,op.Offset,disk.Identity,journal,progress);await Refresh();return "Partition moved; complete data hash, filesystem boot metadata and redundant partition table verified. Recovery journal: "+folder;
            }
            if(table.Partitions.Any(p=>p.Type!=DiskTables.Basic&&p.Type!=DiskTables.Reserved))throw new IOException("Data-table conversion requires basic data partitions. Use a Windows migration to rebuild a bootable system layout.");
            DiskTable next=op.Kind==OperationKind.ConvertTable?DiskTables.Convert(table,op.Value):DiskTables.SetLogical(table,tableIndex,op.Value=="Logical");await Data.SaveAsync(Path.Combine(folder,"replacement-layout.json"),next);
            BackupMetadata(raw,table,folder);DiskTables.Write(raw,next);raw.Flush();var after=DiskTables.Read(raw,disk.SectorSize);if(after.Style!=next.Style||after.Partitions.Count!=next.Partitions.Count||next.Partitions.Any(p=>!after.Partitions.Any(a=>a.Offset==p.Offset&&a.Length==p.Length)))throw new IOException("Partition-table conversion did not pass read-back verification. Original metadata is retained at "+folder);
            await Refresh();return "Partition table updated and read-back verified; data sectors retained. Original table/sector backup: "+folder;
        }
        var partition=disk.Partitions.Single(p=>p.Number==op.PartitionNumber);if(partition.Protected||partition.Encryption!="None"||partition.Letter.Length!=1||disk.Offline)throw new IOException("Choose a mounted, unencrypted data volume.");
        if(op.Kind==OperationKind.SplitPartition)
        {
            if(partition.FileSystem!="NTFS")throw new IOException("Split currently requires NTFS shrink support.");var limits=JsonSerializer.Deserialize<Dictionary<string,long>>(await StorageService.LimitsAsync(disk,partition),Data.Json)!;if(op.Bytes<limits["SizeMin"]||op.Bytes>=partition.Size-64L*1024*1024)throw new IOException("The first partition size must fit Windows shrink limits and leave at least 64 MiB for the second.");
            await StorageService.PowerShellAsync($"Resize-Partition -DiskNumber {disk.Number} -PartitionNumber {partition.Number} -Size {op.Bytes}",progress:progress);var now=(await StorageService.InventoryAsync()).Single(d=>d.Number==disk.Number);var create=DiskOperation.For(now,OperationKind.CreatePartition) with{Bytes=(partition.Size-op.Bytes)/(1024*1024)*(1024*1024),Value=op.Value,FileSystem="NTFS"};await StorageService.ExecuteDetailedAsync(create,progress);return "NTFS partition split; original files retained in the first partition and a new NTFS volume created in the released extent.";
        }
        await DiskAccess.RequireSeparatePathAsync(op.Workspace,disk.Number);var stage=Path.Combine(op.Workspace,"SoopaPartition-"+op.Id.ToString("N"));Directory.CreateDirectory(stage);string wim=Path.Combine(stage,"contents.wim"),engine=Path.Combine(AppContext.BaseDirectory,"Tools","wimlib","wimlib-imagex.exe");
        var source=partition;
        if(op.Kind==OperationKind.MergePartition)
        {
            source=disk.Partitions.Single(p=>p.Number==op.TargetPartitionNumber);if(source.Protected||source.Encryption!="None"||source.FileSystem!="NTFS"||partition.FileSystem!="NTFS"||source.Letter.Length!=1||source.Offset!=partition.Offset+partition.Size)throw new IOException("Merge requires two adjacent, mounted unencrypted NTFS data partitions. Select the left partition as the destination.");
        }
        if(op.Kind!=OperationKind.MergePartition)
        {
            string fs=op.Kind==OperationKind.ChangeClusterSize?source.FileSystem:op.FileSystem;long logical=0;var meta=new WindowsVaultMetadata(source.Letter+@":\");
            foreach(string path in EnumerateWithoutFollowingLinks(source.Letter+@":\"))
            {
                var attributes=File.GetAttributes(path);if((attributes&FileAttributes.ReparsePoint)!=0){if(fs!="NTFS")throw new IOException("Links cannot be represented by the destination filesystem. The source was not reformatted.");continue;}if((attributes&FileAttributes.Directory)!=0)continue;
                var info=new FileInfo(path);logical=checked(logical+info.Length);
                if(fs=="FAT32"&&info.Length>=4L*1024*1024*1024)throw new IOException("A source file exceeds FAT32's 4 GiB limit. The source was not reformatted.");
                if(fs!="NTFS"&&((info.Attributes&(FileAttributes.Encrypted|FileAttributes.ReparsePoint))!=0||meta.Streams(path).Any()||meta.Read(path,false,source.Letter+@":\").HardlinkId!=null))throw new IOException("The source has NTFS-only encrypted files, links or alternate streams. Preserve it as NTFS or remove those features before converting.");
                if(op.Kind==OperationKind.ChangeClusterSize&&op.AllocationUnitSize>4096&&(info.Attributes&FileAttributes.Compressed)!=0)throw new IOException("NTFS compression requires allocation units of at most 4096 bytes. Decompress the source files or choose 4096 bytes.");
            }
            if(logical>source.Size-32L*1024*1024)throw new IOException("Expanded file contents will not fit this partition after conversion.");

        }
        if(new DriveInfo(Path.GetPathRoot(stage)!).AvailableFreeSpace<source.Size-source.Free+256L*1024*1024)throw new IOException("Insufficient checkpoint space for a verified copy of all source files.");
        await Data.SaveAsync(Path.Combine(stage,"plan.json"),new{Operation=op,Disk=disk,Source=source,State="Capturing"});
        await ProtectedImageService.Tool(engine,["capture",source.Letter+@":\",wim,"SoopaDriveTool preserved content","--compress=XPRESS","--strict-acls","--norpfix","--check"],"Saving recoverable partition contents",progress);
        await ProtectedImageService.Tool(engine,["verify",wim],"Verifying recovery checkpoint",progress);
        var before=(await StorageService.InventoryAsync()).Single(d=>d.Number==disk.Number);QueuePlanner.AssertUnchanged(disk,before);await Data.SaveAsync(Path.Combine(stage,"checkpoint.json"),new{State="Verified backup; destructive stage may begin",RecoveryWim=wim,DiskIdentity=disk.Identity,Source=source});
        string destination=partition.Letter+@":\";
        if(op.Kind==OperationKind.MergePartition)
        {
            await StorageService.PowerShellAsync($"Remove-Partition -DiskNumber {disk.Number} -PartitionNumber {source.Number} -Confirm:$false; Resize-Partition -DiskNumber {disk.Number} -PartitionNumber {partition.Number} -Size {partition.Size+source.Size}",progress:progress);destination=Path.Combine(destination,"Merged-"+op.Id.ToString("N"));Directory.CreateDirectory(destination);
        }
        else
        {
            if(op.Kind==OperationKind.ChangeFileSystem&&op.FileSystem=="NTFS"&&partition.FileSystem=="FAT32")
            {
                await ProtectedImageService.Tool(Path.Combine(Environment.SystemDirectory,"convert.exe"),[partition.Letter+":","/FS:NTFS","/X","/NoSecurity"],"Converting FAT32 to NTFS",progress);return "FAT32 converted to NTFS using Windows conversion, with a verified recovery archive at "+stage;
            }
            if(op.FileSystem=="FAT32")
            {
                var info=await ProcessTool.RunAsync(engine,["dir",wim,"1","--detailed"]);if(info.ExitCode!=0)throw new IOException("Could not validate FAT32 source constraints.");
                // Inspect the live tree before formatting; FAT32 cannot hold a 4 GiB file.
                if(Directory.EnumerateFiles(source.Letter+@":\","*",SearchOption.AllDirectories).Any(f=>new FileInfo(f).Length>=4L*1024*1024*1024))throw new IOException("At least one file exceeds FAT32's 4 GiB file-size limit.");
            }
            int cluster=op.AllocationUnitSize;if(cluster!=0&&(cluster<512||cluster>2*1024*1024||(cluster&(cluster-1))!=0))throw new IOException("Choose a supported power-of-two allocation unit size.");
            string extra=cluster==0?"":$" -AllocationUnitSize {cluster}";string fs=op.Kind==OperationKind.ChangeClusterSize?partition.FileSystem:op.FileSystem;if(fs is not ("NTFS" or "exFAT" or "FAT32"))throw new IOException("Unsupported destination filesystem.");
            var label=Convert.ToBase64String(Encoding.UTF8.GetBytes(partition.Label));await StorageService.PowerShellAsync($"$label=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{label}')); Format-Volume -DriveLetter '{partition.Letter}' -FileSystem '{fs}' -NewFileSystemLabel $label -Confirm:$false{extra} | Out-Null",progress:progress);
        }
        bool ntfs=op.Kind==OperationKind.MergePartition||op.Kind==OperationKind.ChangeClusterSize&&partition.FileSystem=="NTFS"||op.Kind==OperationKind.ChangeFileSystem&&op.FileSystem=="NTFS";
        await ProtectedImageService.Tool(engine,["apply",wim,"1",destination,"--check",ntfs?"--strict-acls":"--no-acls","--norpfix"],"Restoring preserved contents",progress);
        if(ntfs)await StorageService.PowerShellAsync($"$r=Repair-Volume -DriveLetter '{partition.Letter}' -Scan;if($r -and [string]$r -ne 'NoErrorsFound'){{throw ('Filesystem scan: '+[string]$r)}}");
        await Data.SaveAsync(Path.Combine(stage,"completed.json"),new{State="Completed",Utc=DateTime.UtcNow});return "Content migration completed and filesystem checked. The verified recovery archive is retained at "+stage;
    }
    static IEnumerable<string> EnumerateWithoutFollowingLinks(string root)
    {foreach(var path in Directory.EnumerateFileSystemEntries(root)){yield return path;var attributes=File.GetAttributes(path);if((attributes&FileAttributes.Directory)!=0&&(attributes&FileAttributes.ReparsePoint)==0)foreach(var child in EnumerateWithoutFollowingLinks(path))yield return child;}}
    static async Task<string> RedistributeAsync(DiskOperation op,DiskInfo disk,IProgress<DriveProgress>? progress)
    {
        var left=disk.Partitions.Single(p=>p.Number==op.PartitionNumber);var right=disk.Partitions.Single(p=>p.Number==op.TargetPartitionNumber);long transfer=op.Bytes;
        if(disk.Offline||left.FileSystem!="NTFS"||right.FileSystem!="NTFS"||left.Protected||right.Protected||left.Encryption!="None"||right.Encryption!="None"||right.Offset!=left.Offset+left.Size||transfer<16L*1024*1024||transfer%(1024*1024)!=0)throw new IOException("Space redistribution requires adjacent mounted, unencrypted NTFS data partitions and a whole-MiB transfer of at least 16 MiB.");
        await DiskAccess.RequireSeparatePathAsync(op.Workspace,disk.Number);var limits=JsonSerializer.Deserialize<Dictionary<string,long>>(await StorageService.LimitsAsync(disk,right),Data.Json)!;if(right.Size-transfer<limits["SizeMin"])throw new IOException("The right partition cannot shrink by the requested amount.");string plan=Path.Combine(op.Workspace,"redistribute-"+op.Id+".json");await Data.SaveAsync(plan,new{Operation=op,Original=disk,Stage="Reviewed plan; shrink, move, grow"});
        progress?.Report(new("Shrinking donor partition",0,"Windows is moving allocated NTFS clusters into the retained donor extent."));await StorageService.ExecuteDetailedAsync(DiskOperation.For(disk,OperationKind.ResizePartition,right) with{Bytes=right.Size-transfer},progress);
        var current=(await StorageService.InventoryAsync()).Single(d=>d.Number==disk.Number);await StorageService.ExecuteDetailedAsync(DiskOperation.For(current,OperationKind.Offline),progress);current=(await StorageService.InventoryAsync()).Single(d=>d.Number==disk.Number);var donor=current.Partitions.Single(p=>p.Offset==right.Offset);
        await ExecuteAsync(DiskOperation.For(current,OperationKind.MovePartition,donor) with{Id=op.Id,Offset=right.Offset+transfer,Workspace=op.Workspace},current,progress);current=(await StorageService.InventoryAsync()).Single(d=>d.Number==disk.Number);await StorageService.ExecuteDetailedAsync(DiskOperation.For(current,OperationKind.Online),progress);current=(await StorageService.InventoryAsync()).Single(d=>d.Number==disk.Number);var destination=current.Partitions.Single(p=>p.Offset==left.Offset);await StorageService.ExecuteDetailedAsync(DiskOperation.For(current,OperationKind.ResizePartition,destination) with{Bytes=left.Size+transfer},progress);
        var final=(await StorageService.InventoryAsync()).Single(d=>d.Number==disk.Number);if(!final.Partitions.Any(p=>p.Offset==left.Offset&&p.Size==left.Size+transfer)||!final.Partitions.Any(p=>p.Offset==right.Offset+transfer&&p.Size==right.Size-transfer))throw new IOException("Redistributed layout verification failed; inspect the retained move journal.");await Data.SaveAsync(plan,new{Operation=op,Original=disk,Final=final,Stage="Completed and verified"});return "NTFS space redistributed through a verified shrink, resumable overlap-safe move, and grow. Recovery checkpoints retained at "+op.Workspace;
    }
    static void BackupMetadata(Stream raw,DiskTable table,string folder){foreach(long offset in new[]{0L,Math.Max(0,raw.Length-1024*1024)}){int count=(int)Math.Min(1024*1024,raw.Length-offset);string path=Path.Combine(folder,"sectors-"+offset+".bin");if(!File.Exists(path))File.WriteAllBytes(path,DiskTables.ReadAt(raw,offset,count));}foreach(var p in table.Partitions.Where(p=>p.Logical)){string path=Path.Combine(folder,"ebr-"+p.EbrOffset+".bin");if(!File.Exists(path))File.WriteAllBytes(path,DiskTables.ReadAt(raw,p.EbrOffset,table.SectorSize));}}
    static Task<string> Refresh()=>StorageService.PowerShellAsync("Update-HostStorageCache");
}
