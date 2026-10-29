using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public record ImageMetadata(int Version,long Length,int SectorSize,string Sha256,DateTime CreatedUtc);
public static class RawDiskEngine
{
    static DiskAccess.BoundDiskStream Open(DiskInfo d,bool write)=>DiskAccess.Open(d,write);
    static async Task ValidateAgain(DiskOperation op)
    {var all=await StorageService.InventoryAsync();OperationPolicy.Validate(op,all.Single(d=>d.Number==op.DiskNumber),all.SingleOrDefault(d=>d.Number==op.TargetDiskNumber));}
    static async Task SafeImagePath(string path,int sourceDisk)
    {
        BackupEngine.RejectExistingAncestorLinks(path);var root=Path.GetPathRoot(Path.GetFullPath(path))!;var drive=new DriveInfo(root);
        if(drive.DriveType is not (DriveType.Fixed or DriveType.Removable))throw new IOException("Use a local disk for images.");
        var number=await StorageService.PowerShellAsync($"(Get-Partition -DriveLetter '{char.ToUpperInvariant(root[0])}' -ErrorAction Stop).DiskNumber");
        if(int.Parse(number)==sourceDisk)throw new IOException("An image cannot be stored on its source/target disk.");
    }
    public static async Task<string> ExecuteAsync(DiskOperation op,DiskInfo disk,DiskInfo? target,IProgress<DriveProgress>? progress)
    {
        if(op.Kind==OperationKind.BackupImage)
        {
            await SafeImagePath(op.Value,disk.Number);if(File.Exists(op.Value)||File.Exists(op.Value+".json"))throw new IOException("Choose a new image filename.");
            if(new DriveInfo(Path.GetPathRoot(op.Value)!).AvailableFreeSpace<disk.Size+64L*1024*1024)throw new IOException("Insufficient free space for a full sector image.");
            var temp=op.Value+".partial";await ValidateAgain(op);
            try
            {
                string hash;await using(var input=Open(disk,false))await using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,1024*1024,true))hash=await Copy(input,output,disk.Size,progress,0,60);
                await using(var verify=File.OpenRead(temp))if(await Hash(verify,disk.Size,progress,60,40)!=hash)throw new IOException("Image verification failed.");
                File.Move(temp,op.Value);await Data.SaveAsync(op.Value+".json",new ImageMetadata(1,disk.Size,disk.SectorSize,hash,DateTime.UtcNow));return "Full disk image created and SHA-256 verified. Keep the .json sidecar with the image.";
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        if(op.Kind==OperationKind.RestoreImage)
        {
            await SafeImagePath(op.Value,disk.Number);BackupEngine.RejectExistingAncestorLinks(op.Value+".json");var meta=JsonSerializer.Deserialize<ImageMetadata>(await File.ReadAllTextAsync(op.Value+".json"),Data.Json)??throw new IOException("Missing image metadata.");
            if(meta.Version!=1||meta.Length!=disk.Size||meta.SectorSize!=disk.SectorSize)throw new IOException("Image and target size/sector size must match exactly.");
            await using var input=new FileStream(op.Value,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,true);
            if(input.Length!=meta.Length||await Hash(input,meta.Length,progress,0,25)!=meta.Sha256)throw new IOException("Image checksum failed. Target was not changed.");
            input.Position=0;await ValidateAgain(op);await using var output=Open(disk,true);await Copy(input,output,meta.Length,progress,25,40);
            output.Position=0;if(await Hash(output,disk.Size,progress,65,35)!=meta.Sha256)throw new IOException("Restore read-back checksum failed. Target may contain a partial image.");return "Image restored and full target read-back verified. Disk remains offline.";
        }
        if(op.Kind==OperationKind.CloneDisk)
        {
            await ValidateAgain(op);string hash;await using var input=Open(disk,false);await using var output=Open(target!,true);hash=await Copy(input,output,disk.Size,progress,0,55);
            output.Position=0;if(await Hash(output,disk.Size,progress,55,45)!=hash)throw new IOException("Clone read-back checksum failed. Target may be incomplete.");
            if(op.CloneMode==CloneMode.ByteExactGrow&&target!.Size>disk.Size){var table=DiskTables.Read(output,disk.SectorSize);if(table.Style=="GPT"){DiskTables.Write(output,table);output.Flush();if(DiskTables.Read(output,disk.SectorSize).RecoveredFromBackup)throw new IOException("Relocated GPT verification failed.");}return "All source sectors copied and hash-verified; redundant GPT metadata relocated to the larger disk boundary. Extra capacity is unallocated. Disconnect the source before using its identical clone.";}
            return "Byte-exact clone complete and full read-back verified. Both disks remain offline. Disconnect the source before bringing its identical clone online.";
        }
        if(op.Kind==OperationKind.WipeDisk)
        {
            await ValidateAgain(op);int passes=op.WipeMode==WipeMode.ThreePass?3:1;var b=new byte[1024*1024];
            await using var output=Open(disk,true);
            for(int pass=0;pass<passes;pass++)
            {
                string pattern=op.WipeMode==WipeMode.ThreePass?(pass==0?"Random":pass==1?"Ones":"Zero"):op.WipeMode==WipeMode.Random?"Random":"Zero";
                using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var meter=new Meter(progress,$"Overwrite pass {pass+1}/{passes} ({pattern})",100.0*pass/passes,50.0/passes,disk.Size);long done=0;
                output.Position=0;
                {
                    while(done<disk.Size)
                    {
                        int n=(int)Math.Min(b.Length,disk.Size-done);if(pattern=="Random")RandomNumberGenerator.Fill(b.AsSpan(0,n));else b.AsSpan(0,n).Fill(pattern=="Ones"?(byte)255:(byte)0);
                        digest.AppendData(b,0,n);await output.WriteAsync(b.AsMemory(0,n));done+=n;meter.Report(done);
                    }
                    output.Flush();
                }
                var expected=Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
                output.Position=0;if(await Hash(output,disk.Size,progress,100.0*pass/passes+50.0/passes,50.0/passes,$"Verify pass {pass+1}/{passes}")!=expected)throw new IOException("Overwrite read-back hash failed.");
            }
            return $"{passes}-pass overwrite completed with full read-back verification after every pass. This erases user-addressable sectors; it is not firmware sanitize or guaranteed SSD remapped-cell erasure.";
        }
        throw new InvalidOperationException("Unsupported raw operation.");
    }
    sealed class Meter(IProgress<DriveProgress>? progress,string phase,double start,double span,long total)
    {
        readonly Stopwatch clock=Stopwatch.StartNew();long last;
        public void Report(long done)
        {
            if(done!=total&&clock.ElapsedMilliseconds-last<200)return;last=clock.ElapsedMilliseconds;
            double speed=done/Math.Max(.001,clock.Elapsed.TotalSeconds);var eta=speed>0?TimeSpan.FromSeconds((total-done)/speed).ToString(@"hh\:mm\:ss"):"—";
            progress?.Report(new(phase,start+span*done/total,$"{100.0*done/total:0.0}% · {Data.Size(done)} / {Data.Size(total)} · {Data.Size((long)speed)}/s · remaining {eta}",done,total,speed));
        }
    }
    static async Task<string> Copy(Stream input,Stream output,long length,IProgress<DriveProgress>? progress,double start,double span)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var b=new byte[1024*1024];long done=0;var meter=new Meter(progress,"Copying sectors",start,span,length);
        while(done<length){int n=(int)Math.Min(b.Length,length-done);await input.ReadExactlyAsync(b.AsMemory(0,n));hash.AppendData(b,0,n);await output.WriteAsync(b.AsMemory(0,n));done+=n;meter.Report(done);}output.Flush();return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    static async Task<string> Hash(Stream input,long length,IProgress<DriveProgress>? progress,double start,double span,string phase="Read-back verification")
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var b=new byte[1024*1024];long done=0;var meter=new Meter(progress,phase,start,span,length);
        while(done<length){int n=(int)Math.Min(b.Length,length-done);await input.ReadExactlyAsync(b.AsMemory(0,n));hash.AppendData(b,0,n);done+=n;meter.Report(done);}return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
