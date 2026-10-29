using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using SoopaDriveTool.Core;
using SoopaDriveTool.Windows;
partial class Program
{
    sealed record WindowsFixtureRequest(string Repository,string Scratch,string Password,string ExpectedSourceSerial,string Result);
    static async Task CaptureWindowsFixture(string request)
    {
        if(Environment.MachineName!="SDTLAB")throw new Exception("Owned spare only.");
        var settings=JsonSerializer.Deserialize<WindowsFixtureRequest>(await File.ReadAllTextAsync(request),Data.Json)??throw new IOException("Missing fixture request.");
        var disk=(await StorageService.InventoryAsync()).Single(d=>d.IsBoot);
        if(disk.Serial!=settings.ExpectedSourceSerial)throw new IOException("Unexpected fixture source disk.");
        var progress=new ImmediateDriveProgress(p=>Console.WriteLine($"{DateTime.UtcNow:O} {p.Phase}: {p.Percent:0.0}% {p.Message}"));
        string id=await ProtectedImageService.CaptureWindowsAsync(disk,disk.Partitions.Single(p=>p.IsBoot),settings.Repository,settings.Password,"Full",settings.Scratch,progress);
        await Data.SaveAsync(settings.Result,new{Snapshot=id,CompletedUtc=DateTime.UtcNow,SourceDisk=disk.Number,SourceSerial=disk.Serial,Repository=settings.Repository});
        Console.WriteLine("VSS WINDOWS RECOVERY IMAGE CAPTURED AND VERIFIED");
    }
    sealed class ImmediateDriveProgress(Action<DriveProgress> action):IProgress<DriveProgress>{public void Report(DriveProgress p)=>action(p);}
    static async Task PreparePeFixture(string folder)
    {
        if(Environment.MachineName!="SDTLAB")throw new Exception("Owned spare only.");Directory.CreateDirectory(folder);string export=@"\\Mac\SDTDevelopment\work\v10\pe-repository.raw";if(File.Exists(export))throw new IOException("PE repository export exists; inspect before replacing.");var paths=new[]{Path.Combine(folder,"pe-source.vhd"),Path.Combine(folder,"pe-repository.vhd")};var disks=new List<DiskInfo>();
        try
        {
            for(int i=0;i<2;i++){string file=paths[i];if(File.Exists(file))throw new IOException("Owned fixture exists; inspect before replacing.");string script=Path.Combine(folder,"create-pe-"+i+".txt");await File.WriteAllTextAsync(script,$"create vdisk file=\"{file}\" maximum={(i==0?2048:8192)} type=expandable\nselect vdisk file=\"{file}\"\nattach vdisk\nexit\n");string output=await StorageService.PowerShellAsync($"diskpart.exe /s '{script}';(Get-DiskImage -ImagePath '{file}'|Get-Disk).Number");int n=int.Parse(output.Split('\n').Last().Trim());var disk=(await StorageService.InventoryAsync()).Single(d=>d.Number==n);if(disk.Bus!="File Backed Virtual"||disk.Protected)throw new IOException("Not an owned virtual fixture.");await StorageService.ExecuteDetailedAsync(DiskOperation.For(disk,OperationKind.InitializeGpt));disk=(await StorageService.InventoryAsync()).Single(d=>d.Number==n);await StorageService.ExecuteDetailedAsync(DiskOperation.For(disk,OperationKind.CreatePartition) with{Bytes=(i==0?256L:7168L)*1024*1024,Value=i==0?"PEFixture":"SoopaPERepo"});disks.Add((await StorageService.InventoryAsync()).Single(d=>d.Number==n));}
            var source=disks[0];var sourcePart=source.Partitions.Single(p=>!p.Protected);string sourceRoot=sourcePart.Letter+@":\";string marker=Guid.NewGuid().ToString();await File.WriteAllTextAsync(Path.Combine(sourceRoot,"proof.txt"),"Recovered by native SoopaDriveTool WinPE: "+marker);await File.WriteAllTextAsync(Path.Combine(sourceRoot,"proof.txt")+":Stream","Native recovery stream");byte[] binary=RandomNumberGenerator.GetBytes(1024*1024);await File.WriteAllBytesAsync(Path.Combine(sourceRoot,"binary.bin"),binary);await StorageService.ExecuteDetailedAsync(DiskOperation.For(source,OperationKind.Offline));source=(await StorageService.InventoryAsync()).Single(d=>d.Number==source.Number);
            string repository=Path.Combine(disks[1].Partitions.Single(p=>!p.Protected).Letter+@":\","repo");const string password="native recovery fixture passphrase";string id=await ProtectedImageService.CaptureRawAsync(source,repository,password,"Full");string expected;using(var input=DiskAccess.Open(source,false))expected=Convert.ToHexString(await SHA256.HashDataAsync(input)).ToLowerInvariant();await Data.SaveAsync(@"\\Mac\SDTDevelopment\work\v10\pe-fixture-private.json",new{Snapshot=id,Passphrase=password,SourceSha256=expected,DiskBytes=source.Size,Marker=marker,BinarySha256=Convert.ToHexString(SHA256.HashData(binary)).ToLowerInvariant(),RepositoryRelative="repo",RepositoryLabel="SoopaPERepo"});
            var repoDisk=(await StorageService.InventoryAsync()).Single(d=>d.Number==disks[1].Number);await StorageService.ExecuteDetailedAsync(DiskOperation.For(repoDisk,OperationKind.Offline));repoDisk=(await StorageService.InventoryAsync()).Single(d=>d.Number==repoDisk.Number);using(var input=DiskAccess.Open(repoDisk,false))await using(var output=new FileStream(export,FileMode.CreateNew,FileAccess.Write,FileShare.None,4*1024*1024,FileOptions.Asynchronous)){byte[] buffer=new byte[4*1024*1024];long done=0;while(done<repoDisk.Size){int n=(int)Math.Min(buffer.Length,repoDisk.Size-done);await input.ReadExactlyAsync(buffer.AsMemory(0,n));if(buffer.AsSpan(0,n).IndexOfAnyExcept((byte)0)<0)output.Position+=n;else await output.WriteAsync(buffer.AsMemory(0,n));done+=n;}output.SetLength(repoDisk.Size);output.Flush(true);}Console.WriteLine("PE encrypted fixture and sparse raw repository exported; target may now be restored in the separate recovery VM.");
        }
        finally{foreach(string file in paths)if(File.Exists(file)){try{using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(30));await StorageService.PowerShellAsync($"Dismount-DiskImage -ImagePath '{file}'",stop.Token);}catch{}}}
    }
}
