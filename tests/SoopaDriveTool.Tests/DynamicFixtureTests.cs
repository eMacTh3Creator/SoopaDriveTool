using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using SoopaDriveTool.Core;
using SoopaDriveTool.Windows;
partial class Program
{
    static string DynamicSerial=>Environment.GetEnvironmentVariable("SDT_DYNAMIC_FIXTURE_SERIAL")??throw new IOException("Provide the serial of the owned disposable fixture.");
    static async Task DynamicFixture(string phase)
    {
        if(Environment.MachineName!="SDTLAB")throw new IOException("This qualification is restricted to its owned spare VM.");
        async Task<DiskInfo> Current(){var d=(await StorageService.InventoryAsync()).Single(d=>d.Serial==DynamicSerial);if(d.Protected||d.Size!=4L*1024*1024*1024)throw new IOException("Unexpected disposable device.");return d;}
        var disk=await Current();string folder=@"E:\dynamic-qualification";Directory.CreateDirectory(folder);string state=Path.Combine(folder,"fixture.json");
        if(phase=="prepare")
        {
            if(disk.Style!="RAW"||disk.Partitions.Count!=0||File.Exists(state))throw new IOException("Dynamic fixture already exists; inspect it before repeating.");
            await StorageService.ExecuteDetailedAsync(DiskOperation.For(disk,OperationKind.InitializeGpt));disk=await Current();
            await StorageService.ExecuteDetailedAsync(DiskOperation.For(disk,OperationKind.CreatePartition) with{Bytes=768L*1024*1024,Value="SoopaDYN"});disk=await Current();
            string root=disk.Partitions.Single(p=>!p.Protected).Letter+@":\";byte[] bytes=RandomNumberGenerator.GetBytes(1024*1024);await File.WriteAllBytesAsync(Path.Combine(root,"proof.bin"),bytes);await File.WriteAllTextAsync(Path.Combine(root,"proof.bin")+":Proof","Dynamic conversion stream");
            await Data.SaveAsync(state,new{Sha256=Convert.ToHexString(SHA256.HashData(bytes)),DiskSerial=DynamicSerial});
            await DiagnosticsService.FreeSpaceWipeAsync(disk,disk.Partitions.Single(p=>!p.Protected),false,null,CancellationToken.None);
            Check((await File.ReadAllBytesAsync(Path.Combine(root,"proof.bin"))).SequenceEqual(bytes)&&!Directory.GetDirectories(root,".soopa-free-space-*").Any(),"Real free-space overwrite preserves allocated data and removes its temporary files");
            using var scanInput=DiskAccess.Open(disk,false,false);var scan=await DiskRecovery.SurfaceScanAsync(scanInput,0,32L*1024*1024,4*1024*1024);scanInput.Dispose();Check(scan.Count==8&&scan.All(b=>b.Status!="Read error"),"Surface scan reads real owned disk regions and reports individual timing");
            await RejectAsync(()=>DiagnosticsService.NativeEraseAsync(disk,"CryptoErase"),"Firmware erase rejects the VM SATA device before sending NVMe commands");
            var topology=await DiagnosticsService.TopologyAsync();using(var json=JsonDocument.Parse(topology))Check(json.RootElement.TryGetProperty("Volumes",out _),"Live topology reports physical disks, volumes, pools and virtual disks");
            await StorageService.ExecuteDetailedAsync(DiskOperation.For(disk,OperationKind.ConvertDynamic) with{Value="Dynamic",Workspace=folder});disk=await Current();Check(disk.Style=="Dynamic"&&disk.Partitions.Count==1,"Windows dynamic conversion and native extent inventory identify the owned simple volume");
            var part=disk.Partitions.Single();Check(await File.ReadAllTextAsync(Path.Combine(part.Letter+@":\","proof.bin")+":Proof")=="Dynamic conversion stream","Basic-to-dynamic retains the named NTFS stream");
            await StorageService.ExecuteDetailedAsync(DiskOperation.For(disk,OperationKind.ConvertDynamic) with{Value="Basic",Workspace=folder});Check((await Current()).Offline,"Dynamic-to-basic verifies its GPT and keeps the disk offline for restart");
        }
        else if(phase=="verify")
        {
            if(!File.Exists(state)||disk.Style!="GPT")throw new IOException("Converted basic fixture is missing.");
            if(disk.Offline)await StorageService.ExecuteDetailedAsync(DiskOperation.For(disk,OperationKind.Online));disk=await Current();var p=disk.Partitions.Single(p=>!p.Protected);
            if(p.Letter.Length==0){await StorageService.PowerShellAsync($"Get-Partition -DiskNumber {disk.Number} -PartitionNumber {p.Number}|Add-PartitionAccessPath -AssignDriveLetter");disk=await Current();p=disk.Partitions.Single(x=>!x.Protected);}
            using var expected=JsonDocument.Parse(await File.ReadAllTextAsync(state));string file=Path.Combine(p.Letter+@":\","proof.bin");Check(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file)))==expected.RootElement.GetProperty("Sha256").GetString()&&await File.ReadAllTextAsync(file+":Proof")=="Dynamic conversion stream","After Windows restart, basic GPT mounts and preserves original NTFS data and streams");
        }
        else throw new IOException("Unknown fixture phase.");
    }
}
