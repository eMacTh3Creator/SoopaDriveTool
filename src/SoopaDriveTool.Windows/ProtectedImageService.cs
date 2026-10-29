using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public record ProtectedImageInfo(int Version,string Kind,long Length,int SectorSize,string Architecture,DateTime CreatedUtc,DiskTable? Table,string WindowsLetter="",byte[]? WindowsMapping=null,bool Recovery=false,long UsedBytes=0);
public static class ProtectedImageService
{
    public static async Task<string> CaptureRawAsync(DiskInfo disk,string repository,string? password,string policy,IProgress<DriveProgress>? progress=null,CancellationToken ct=default)
    {
        await DiskAccess.RequireSeparatePathAsync(repository,disk.Number);var now=(await StorageService.InventoryAsync(ct)).Single(d=>d.Number==disk.Number);QueuePlanner.AssertUnchanged(disk,now);if(disk.Protected||!disk.Offline)throw new IOException("Raw image capture requires an offline data disk. Use the VSS Windows image option for the running system.");
        using var vault=await VaultBackup.OpenAsync(repository,password,true,ct:ct);DiskTable? table;using(var input=DiskAccess.Open(disk,false)){try{table=DiskTables.Read(input,disk.SectorSize);}catch(IOException){table=null;}}
        var meta=JsonSerializer.SerializeToUtf8Bytes(new ProtectedImageInfo(1,"Raw",disk.Size,disk.SectorSize,RuntimeInformation.OSArchitecture.ToString(),DateTime.UtcNow,table),Data.Json);
        var id=await vault.CreateStreamsAsync([new("disk.json",meta.Length,()=>new MemoryStream(meta)),new("disk.sdi",disk.Size,()=>DiskAccess.Open(disk,false))],disk.Display,policy,progress,ct);await vault.VerifyAsync(id,ct:ct,activity:progress);return id;
    }
    public static async Task<string> CaptureWindowsAsync(DiskInfo disk,PartitionInfo partition,string repository,string? password,string policy,string scratch,IProgress<DriveProgress>? progress=null,CancellationToken ct=default)
    {
        if(!StorageService.IsAdmin||!partition.IsBoot||!disk.IsBoot||partition.FileSystem!="NTFS"||partition.Encryption!="None")throw new IOException("Choose the current unencrypted NTFS Windows volume.");await DiskAccess.RequireSeparatePathAsync(repository,disk.Number);await DiskAccess.RequireSeparatePathAsync(scratch,disk.Number);string root=partition.Letter+@":\";string staging=Path.Combine(scratch,"SoopaImage-"+Guid.NewGuid().ToString("N"));BackupEngine.RejectExistingAncestorLinks(staging);Directory.CreateDirectory(staging);
        try
        {
            string? recovery=await WindowsRecovery.FindAsync(disk,partition,true);var volumes=new List<string>{VssSession.VolumeForPath(root)};if(recovery!=null)volumes.Add(VssSession.VolumeForPath(recovery));await using var snapshot=await VssSession.CreateAsync(volumes,progress);
            string wim=Path.Combine(staging,"windows.wim"),tool=Path.Combine(AppContext.BaseDirectory,"Tools","wimlib","wimlib-imagex.exe");
            await Tool(tool,["capture",snapshot.PathFor(root),wim,"SoopaDriveTool Windows recovery","--compress=none","--strict-acls","--norpfix","--check","--threads=4"],"Capturing VSS Windows image",progress,ct);
            await Tool(tool,["verify",wim],"Verifying Windows image",progress,ct);
            if(recovery!=null)File.Copy(snapshot.PathFor(recovery),Path.Combine(staging,"recovery.wim"));
            using var map=Microsoft.Win32.Registry.LocalMachine.OpenSubKey("SYSTEM\\MountedDevices");byte[] mapping=map?.GetValue(@"\DosDevices\"+partition.Letter+":") as byte[]??throw new IOException("Windows volume mapping is unavailable.");
            await Data.SaveAsync(Path.Combine(staging,"disk.json"),new ProtectedImageInfo(1,"WindowsWim",disk.Size,disk.SectorSize,RuntimeInformation.OSArchitecture.ToString(),DateTime.UtcNow,null,partition.Letter,mapping,recovery!=null,partition.Size-partition.Free));
            using var vault=await VaultBackup.OpenAsync(repository,password,true,ct:ct);var inputs=Directory.GetFiles(staging).Select(file=>new VaultInput(Path.GetFileName(file),new FileInfo(file).Length,()=>File.OpenRead(file))).ToList();
            string id=await vault.CreateStreamsAsync(inputs,disk.Display+" Windows VSS",policy,progress,ct);await vault.VerifyAsync(id,ct:ct,activity:progress);await snapshot.CompleteAsync();return id;
        }
        finally{if(Directory.Exists(staging))Directory.Delete(staging,true);}
    }
    public static async Task<ProtectedImageInfo> MetadataAsync(VaultBackup vault,string id,CancellationToken ct=default)
    {using var b=new MemoryStream();var manifest=await vault.LoadAsync(id,ct);if(manifest.Entries.SingleOrDefault(e=>e.Path=="disk.json")?.Length is not (>0 and <1048576))throw new IOException("Invalid protected image metadata.");await vault.ExportFileAsync(id,"disk.json",b,ct);return JsonSerializer.Deserialize<ProtectedImageInfo>(b.ToArray(),Data.Json)??throw new IOException("Invalid image metadata.");}
    public static async Task RestoreRawAsync(DiskInfo target,string repository,string? password,string id,IProgress<DriveProgress>? progress=null,CancellationToken ct=default)
    {
        await DiskAccess.RequireSeparatePathAsync(repository,target.Number);using var vault=await VaultBackup.OpenAsync(repository,password,ct:ct);var info=await MetadataAsync(vault,id,ct);if(info.Version!=1||info.Kind!="Raw"||info.Length<=0||info.Length>target.Size||info.SectorSize!=target.SectorSize)throw new IOException("Choose a raw image that fits this disk and matches its sector size.");
        var manifest=await vault.LoadAsync(id,ct);if(manifest.Entries.SingleOrDefault(e=>e.Path=="disk.sdi")?.Length!=info.Length)throw new IOException("Raw image geometry and resource length differ.");
        await vault.VerifyAsync(id,ct:ct,activity:progress);var now=(await StorageService.InventoryAsync(ct)).Single(d=>d.Number==target.Number);QueuePlanner.AssertUnchanged(target,now);await using var output=DiskAccess.Open(target,true);
        await vault.ExportFileAsync(id,"disk.sdi",output,ct,progress);output.Flush();output.Position=0;using var sink=new HashSink();await vault.ExportFileAsync(id,"disk.sdi",sink,ct);string expected=sink.Finish();string actual=await Hash(output,info.Length,progress,ct);if(expected!=actual)throw new IOException("Restored disk failed full read-back verification.");
        if(target.Size>info.Length&&info.Table?.Style=="GPT"){var table=DiskTables.Read(output,target.SectorSize);DiskTables.Write(output,table);output.Flush();DiskTables.Read(output,target.SectorSize);}await StorageService.PowerShellAsync("Update-HostStorageCache");
    }
    public static async Task ExportAsync(string repository,string? password,string id,string destination,IProgress<DriveProgress>? progress=null,CancellationToken ct=default)
    {
        using var vault=await VaultBackup.OpenAsync(repository,password,ct:ct);if(Directory.Exists(destination)&&Directory.EnumerateFileSystemEntries(destination).Any())throw new IOException("Choose an empty export folder.");await vault.RestoreAsync(id,destination,ct:ct);progress?.Report(new("Image exported",100,"Image resources verified and exported. Raw images can be attached as a read-only VHD; WIM images support selective extraction."));
    }
    static async Task<string> Hash(Stream input,long length,IProgress<DriveProgress>? p,CancellationToken ct){using var h=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);byte[] b=new byte[4*1024*1024];long done=0;while(done<length){int n=(int)Math.Min(b.Length,length-done);await input.ReadExactlyAsync(b.AsMemory(0,n),ct);h.AppendData(b,0,n);done+=n;p?.Report(new("Read-back verification",100.0*done/length,Data.Size(done)+" / "+Data.Size(length),done,length));}return Convert.ToHexString(h.GetHashAndReset());}
    internal static async Task Tool(string exe,string[] args,string phase,IProgress<DriveProgress>? progress,CancellationToken ct=default){var result=await ProcessTool.RunAsync(exe,args,phase,progress,ct:ct);if(result.ExitCode!=0)throw new IOException(phase+" failed: "+result.Output+result.Error);}
    sealed class HashSink:Stream{readonly IncrementalHash h=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);public string Finish()=>Convert.ToHexString(h.GetHashAndReset());public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}public override void Write(byte[] b,int o,int n)=>h.AppendData(b,o,n);public override void Write(ReadOnlySpan<byte>b)=>h.AppendData(b);public override void Flush(){}public override int Read(byte[] b,int o,int n)=>throw new NotSupportedException();public override long Seek(long o,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long n)=>throw new NotSupportedException();protected override void Dispose(bool disposing){if(disposing)h.Dispose();base.Dispose(disposing);}}
}
