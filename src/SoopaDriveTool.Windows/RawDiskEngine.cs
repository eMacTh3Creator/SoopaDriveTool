using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public record ImageMetadata(int Version,long Length,int SectorSize,string Sha256,DateTime CreatedUtc);
public static class RawDiskEngine
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool DeviceIoControl(SafeFileHandle handle,uint code,IntPtr input,int inputSize,byte[] output,int outputSize,out int returned,IntPtr overlap);
    static FileStream Open(DiskInfo d,bool write)
    {
        var h=CreateFile(@"\\.\PhysicalDrive"+d.Number,write?0xC0000000u:0x80000000u,3,IntPtr.Zero,3,0,IntPtr.Zero);
        if(h.IsInvalid)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var bytes=new byte[8];
        if(!DeviceIoControl(h,0x7405c,IntPtr.Zero,0,bytes,8,out _,IntPtr.Zero)||BitConverter.ToInt64(bytes)!=d.Size){h.Dispose();throw new IOException("Raw disk size could not be validated.");}
        return new FileStream(h,write?FileAccess.ReadWrite:FileAccess.Read,1024*1024,false);
    }
    static async Task ValidateAgain(DiskOperation op)
    {
        var all=await StorageService.InventoryAsync();OperationPolicy.Validate(op,all.Single(d=>d.Number==op.DiskNumber),all.SingleOrDefault(d=>d.Number==op.TargetDiskNumber));
    }
    static async Task SafeImagePath(string path,int sourceDisk)
    {
        BackupEngine.RejectExistingAncestorLinks(path);
        var root=Path.GetPathRoot(Path.GetFullPath(path))!;var drive=new DriveInfo(root);
        if(drive.DriveType is not (DriveType.Fixed or DriveType.Removable))throw new IOException("Use a local disk for images.");
        var number=await StorageService.PowerShellAsync($"(Get-Partition -DriveLetter '{char.ToUpperInvariant(root[0])}' -ErrorAction Stop).DiskNumber");
        if(int.Parse(number)==sourceDisk)throw new IOException("An image cannot be stored on its source/target disk.");
    }
    public static async Task<string> ExecuteAsync(DiskOperation op,DiskInfo disk,DiskInfo? target,IProgress<string>? progress)
    {
        if(op.Kind==OperationKind.BackupImage)
        {
            await SafeImagePath(op.Value,disk.Number);
            if(File.Exists(op.Value)||File.Exists(op.Value+".json"))throw new IOException("Choose a new image filename.");
            if(new DriveInfo(Path.GetPathRoot(op.Value)!).AvailableFreeSpace<disk.Size+64L*1024*1024)throw new IOException("Insufficient free space for a full sector image.");
            var temp=op.Value+".partial";await ValidateAgain(op);
            try
            {
                string hash;
                await using(var input=Open(disk,false))
                await using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,1024*1024,true))hash=await Copy(input,output,disk.Size,progress);
                await using(var verify=File.OpenRead(temp))if(await Hash(verify,disk.Size,null)!=hash)throw new IOException("Image verification failed.");
                File.Move(temp,op.Value);
                await Data.SaveAsync(op.Value+".json",new ImageMetadata(1,disk.Size,disk.SectorSize,hash,DateTime.UtcNow));
                return "Full disk image created and SHA-256 verified. Keep the .json sidecar with the image.";
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        if(op.Kind==OperationKind.RestoreImage)
        {
            await SafeImagePath(op.Value,disk.Number);BackupEngine.RejectExistingAncestorLinks(op.Value+".json");
            var meta=JsonSerializer.Deserialize<ImageMetadata>(await File.ReadAllTextAsync(op.Value+".json"),Data.Json)??throw new IOException("Missing image metadata.");
            if(meta.Version!=1||meta.Length!=disk.Size||meta.SectorSize!=disk.SectorSize)throw new IOException("Image and target size/sector size must match exactly.");
            await using var input=new FileStream(op.Value,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,true);
            if(input.Length!=meta.Length||await Hash(input,meta.Length,progress)!=meta.Sha256)throw new IOException("Image checksum failed. Target was not changed.");
            input.Position=0;await ValidateAgain(op);
            await using(var output=Open(disk,true))await Copy(input,output,meta.Length,progress);
            await using(var verify=Open(disk,false))if(await Hash(verify,disk.Size,progress)!=meta.Sha256)throw new IOException("Restore read-back checksum failed. Target may contain a partial image.");
            return "Image restored and full target read-back verified. Disk remains offline.";
        }
        if(op.Kind==OperationKind.CloneDisk)
        {
            await ValidateAgain(op);string hash;
            await using(var input=Open(disk,false))await using(var output=Open(target!,true))hash=await Copy(input,output,disk.Size,progress);
            await using(var verify=Open(target!,false))if(await Hash(verify,target!.Size,progress)!=hash)throw new IOException("Clone read-back checksum failed. Target may be incomplete.");
            return "Sector-exact clone complete and read-back verified. Both disks remain offline. Disconnect the source before bringing its identical clone online.";
        }
        if(op.Kind==OperationKind.WipeDisk)
        {
            await ValidateAgain(op);var zero=new byte[1024*1024];long done=0;
            await using(var output=Open(disk,true)){while(done<disk.Size){int n=(int)Math.Min(zero.Length,disk.Size-done);await output.WriteAsync(zero.AsMemory(0,n));done+=n;progress?.Report($"Overwrite {100.0*done/disk.Size:0}%");}output.Flush(true);}
            await using(var verify=Open(disk,false)){done=0;while(done<disk.Size){int n=(int)Math.Min(zero.Length,disk.Size-done);await verify.ReadExactlyAsync(zero.AsMemory(0,n));if(zero.AsSpan(0,n).IndexOfAnyExcept((byte)0)>=0)throw new IOException("Overwrite read-back failed.");done+=n;}}
            return "One-pass zero overwrite completed and read-back verified. This does not guarantee SSD/NVMe secure erasure of remapped or overprovisioned cells.";
        }
        throw new InvalidOperationException("Unsupported raw operation.");
    }
    static async Task<string> Copy(Stream input,FileStream output,long length,IProgress<string>? progress)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var b=new byte[1024*1024];long done=0;
        while(done<length){int n=(int)Math.Min(b.Length,length-done);await input.ReadExactlyAsync(b.AsMemory(0,n));hash.AppendData(b,0,n);await output.WriteAsync(b.AsMemory(0,n));done+=n;progress?.Report($"Copy {100.0*done/length:0}% · {Data.Size(done)} / {Data.Size(length)}");}
        output.Flush(true);return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    static async Task<string> Hash(Stream input,long length,IProgress<string>? progress)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var b=new byte[1024*1024];long done=0;
        while(done<length){int n=(int)Math.Min(b.Length,length-done);await input.ReadExactlyAsync(b.AsMemory(0,n));hash.AppendData(b,0,n);done+=n;progress?.Report($"Verify {100.0*done/length:0}%");}
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
