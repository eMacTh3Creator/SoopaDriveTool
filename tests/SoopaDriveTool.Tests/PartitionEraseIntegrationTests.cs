using System.IO;
using System.Text.Json;
using SoopaDriveTool.Core;
using SoopaDriveTool.Windows;
partial class Program
{
    // Opt-in only. Every disk write is confined to a fresh VHD identified by
    // its exact image path in the spare VM; no existing disk is modified.
    static async Task PartitionEraseIntegrationChecks()
    {
        if(!(Environment.MachineName=="SDTLAB"||Environment.GetEnvironmentVariable("GITHUB_ACTIONS")=="true"&&Environment.GetEnvironmentVariable("RUNNER_OS")=="Windows")||!StorageService.IsAdmin)throw new IOException("Partition erase integration requires the spare administrator SDTLAB VM.");
        string folder=Path.Combine(Path.GetTempPath(),"SoopaPartitionErase-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        string image=Path.Combine(folder,"owned-erase-test.vhd"),script=Path.Combine(folder,"create-owned.txt");
        int number=-1;bool detached=false;
        try
        {
            await File.WriteAllTextAsync(script,$"create vdisk file=\"{image}\" maximum=128 type=expandable\r\nselect vdisk file=\"{image}\"\r\nattach vdisk\r\nconvert gpt\r\ncreate partition primary size=32\r\nformat fs=ntfs quick label=ERASETARGET\r\nassign\r\ncreate partition primary size=32\r\nformat fs=ntfs quick label=RETAINTEST\r\nassign\r\nexit\r\n");
            string q(string value)=>value.Replace("'","''");
            string created=await StorageService.PowerShellAsync($"diskpart.exe /s '{q(script)}' | Out-Null; if($LASTEXITCODE -ne 0){{throw 'Owned VHD creation failed'}}; $d=Get-DiskImage -ImagePath '{q(image)}' | Get-Disk; if(!$d -or $d.BusType -ne 'File Backed Virtual' -or $d.IsBoot -or $d.IsSystem -or $d.Size -ne 134217728){{throw 'Owned VHD identity failed'}}; [int]$d.Number");
            number=int.Parse(created.Trim());var disk=(await StorageService.InventoryAsync()).Single(d=>d.Number==number);
            Check(disk.Bus=="File Backed Virtual"&&disk.Size==128L*1024*1024&&!disk.Protected&&disk.InventoryWarning.Length==0,"Partition erase fixture is an independent owned 128 MiB VHD");
            var partition=disk.Partitions.Single(p=>p.Label=="ERASETARGET");var other=disk.Partitions.Single(p=>p.Label=="RETAINTEST");
            Check(partition.Size==32L*1024*1024&&other.Size==partition.Size&&partition.Encryption=="None"&&!partition.Protected,"Owned VHD exposes two unencrypted data partitions");
            if(other.Letter.Length!=1)throw new IOException("Owned neighbor partition has no mounted drive letter.");
            byte[] retainedPayload=System.Security.Cryptography.RandomNumberGenerator.GetBytes(65536);
            const string retainedName="soopa-retained-payload.bin";await File.WriteAllBytesAsync(Path.Combine(other.Letter+@":\",retainedName),retainedPayload);
            await StorageService.ExecuteAsync(DiskOperation.For(disk,OperationKind.Offline));disk=(await StorageService.InventoryAsync()).Single(d=>d.Number==number);
            partition=disk.Partitions.Single(p=>p.Number==partition.Number);byte[] before;
            using(var read=DiskAccess.Open(disk,false))before=DiskTables.ReadAt(read,0,checked((int)read.Length));
            var operation=DiskOperation.For(disk,OperationKind.WipePartition,partition) with{WipeMode=WipeMode.Zero};
            var activity=new RunActivity{Steps=[new StepActivity{Description=operation.Description}]};
            await QueueRunner.RunAsync([operation],new Dictionary<int,DiskInfo>{{disk.Number,disk}},activity);
            var step=activity.Steps.Single();string result=step.Message;
            var afterDisk=(await StorageService.InventoryAsync()).Single(d=>d.Number==number);byte[] after;
            using(var read=DiskAccess.Open(afterDisk,false))after=DiskTables.ReadAt(read,0,checked((int)read.Length));
            int offset=checked((int)partition.Offset),length=checked((int)partition.Size);
            Check(after.AsSpan(offset,length).IndexOfAnyExcept((byte)0)<0,"Released backend zeroes every selected-partition byte");
            Check(before.AsSpan(0,offset).SequenceEqual(after.AsSpan(0,offset))&&before.AsSpan(offset+length).SequenceEqual(after.AsSpan(offset+length)),"Physical erase leaves every byte outside the selected partition unchanged");
            using(var oldTable=new MemoryStream(before))using(var newTable=new MemoryStream(after))Check(JsonSerializer.Serialize(DiskTables.Read(oldTable),Data.Json)==JsonSerializer.Serialize(DiskTables.Read(newTable),Data.Json),"Physical partition erase retains the complete GPT table");
            QueuePlanner.VerifyTransition(disk,afterDisk,operation);Check(afterDisk.Offline&&result.Contains("full read-back")&&activity.State=="Completed"&&step.State=="Completed"&&step.Percent==100,"Queued partition erase verifies retained geometry and completes while disk remains offline");
            Console.WriteLine("PARTITION_ERASE "+JsonSerializer.Serialize(new{Disk=number,Partition=partition.Number,Bytes=partition.Size,OutsideExtentUnchanged=true,PartitionTableRetained=true,ReadBackVerified=true}));

            await StorageService.ExecuteAsync(DiskOperation.For(afterDisk,OperationKind.Online));
            var online=(await StorageService.InventoryAsync()).Single(d=>d.Number==number);var erased=online.Partitions.Single(p=>p.Offset==partition.Offset&&p.Size==partition.Size);
            Console.WriteLine("PARTITION_REUSE_STATE "+JsonSerializer.Serialize(new{Disk=number,Partition=erased.Number,erased.Letter,erased.FileSystem,erased.Encryption,online.InventoryWarning}));
            Check(erased.Encryption=="None","Erased partition exposes a confirmed unencrypted state for normal formatting");
            var format=DiskOperation.For(online,OperationKind.FormatPartition,erased) with{FileSystem="NTFS",Value="REUSED"};var formatActivity=new RunActivity{Steps=[new StepActivity{Description=format.Description}]};
            await QueueRunner.RunAsync([format],new Dictionary<int,DiskInfo>{{online.Number,online}},formatActivity);
            var reusable=(await StorageService.InventoryAsync()).Single(d=>d.Number==number);var formatted=reusable.Partitions.Single(p=>p.Offset==partition.Offset&&p.Size==partition.Size);
            Check(formatted.FileSystem=="NTFS"&&formatted.Label=="REUSED"&&formatActivity.State=="Completed","Erased partition can be formatted through the normal queued workflow");
            var retained=reusable.Partitions.Single(p=>p.Offset==other.Offset&&p.Size==other.Size);
            Check(retained.Letter.Length==1&&File.ReadAllBytes(Path.Combine(retained.Letter+@":\",retainedName)).SequenceEqual(retainedPayload),"Neighbor file content survives selected-partition erase and reformat");
            Console.WriteLine("PARTITION_REUSE "+JsonSerializer.Serialize(new{Disk=number,Partition=formatted.Number,Formatted=true,NeighborPayloadRetained=true}));
        }
        finally
        {
            if(File.Exists(image))
            {
                string escaped=image.Replace("'","''");
                await StorageService.PowerShellAsync($"$owned=Get-DiskImage -ImagePath '{escaped}' -ErrorAction Stop; if($owned.Attached){{Dismount-DiskImage -ImagePath '{escaped}' -ErrorAction Stop}}; if((Get-DiskImage -ImagePath '{escaped}' -ErrorAction Stop).Attached){{throw 'Owned VHD remains attached; retain test files'}}");
                detached=true;
            }
            else detached=true;
            if(detached&&Directory.Exists(folder))Directory.Delete(folder,true);
        }
        Check(detached&&!Directory.Exists(folder),"Owned partition erase VHD is detached and removed");
    }
}
