using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public static class ImageBrowseService
{
    [StructLayout(LayoutKind.Sequential)]struct StorageType{public uint DeviceId;public Guid VendorId;}
    [StructLayout(LayoutKind.Explicit,Size=56)]struct CreateParameters
    {
        [FieldOffset(0)]public uint Version;
        [FieldOffset(8)]public Guid UniqueId;
        [FieldOffset(24)]public ulong MaximumSize;
        [FieldOffset(32)]public uint BlockSize;
        [FieldOffset(36)]public uint SectorSize;
        [FieldOffset(40)]public IntPtr Parent;
        [FieldOffset(48)]public IntPtr Source;
    }
    [DllImport("virtdisk.dll",CharSet=CharSet.Unicode)]static extern uint CreateVirtualDisk(ref StorageType type,string path,uint access,IntPtr security,uint flags,uint provider,ref CreateParameters parameters,IntPtr overlapped,out Microsoft.Win32.SafeHandles.SafeFileHandle handle);
    static void CreateBrowsingDisk(string path,long bytes)
    {
        var type=new StorageType{DeviceId=2,VendorId=Guid.Parse("ec984aec-a0f9-47e9-901f-71415a66345b")};
        var parameters=new CreateParameters{Version=1,UniqueId=Guid.NewGuid(),MaximumSize=(ulong)bytes,SectorSize=512};
        uint error=CreateVirtualDisk(ref type,Path.GetFullPath(path),0x3f0000,IntPtr.Zero,0,0,ref parameters,IntPtr.Zero,out var handle);
        if(error!=0)throw new System.ComponentModel.Win32Exception((int)error,"Cannot create the temporary browsing VHD.");handle.Dispose();
    }
    public static async Task MountReadOnlyAsync(string repository,string? password,string id,string vhd,IProgress<DriveProgress>? progress=null,CancellationToken ct=default)
    {
        if(!StorageService.IsAdmin)throw new UnauthorizedAccessException("Administrator access is required to attach an image.");BackupEngine.RejectExistingAncestorLinks(vhd);if(File.Exists(vhd))throw new IOException("Choose a new VHD filename.");using var vault=await VaultBackup.OpenAsync(repository,password,ct:ct);var meta=await ProtectedImageService.MetadataAsync(vault,id,ct);if(meta.Kind!="Raw"||meta.SectorSize!=512||meta.Length<=0||meta.Length>2040L*1024*1024*1024)throw new IOException("Read-only VHD browsing supports raw images with 512-byte sectors up to 2040 GiB. Export other images for direct recovery inspection.");
        bool complete=false,attached=false;Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(vhd))!);string encoded=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(vhd)));
        try
        {
            ct.ThrowIfCancellationRequested();CreateBrowsingDisk(vhd,meta.Length);
            string number=await StorageService.PowerShellAsync("$p=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+encoded+"'));(Mount-DiskImage -ImagePath $p -Access ReadWrite -PassThru|Get-Disk).Number",ct);attached=true;
            var disk=(await StorageService.InventoryAsync(ct)).Single(d=>d.Number==int.Parse(number.Trim()));if(disk.Bus!="File Backed Virtual"||disk.Protected||disk.Size<meta.Length||disk.Size>meta.Length+1048576||disk.Partitions.Count!=0)throw new IOException("The new browsing VHD does not match the requested empty virtual device.");
            await StorageService.ExecuteDetailedAsync(DiskOperation.For(disk,OperationKind.Offline));disk=(await StorageService.InventoryAsync(ct)).Single(d=>d.Number==disk.Number);
            using(var raw=DiskAccess.Open(disk,true))
            {
                using var sparse=new SparseWriter(raw);await vault.ExportFileAsync(id,"disk.sdi",sparse,ct,progress);raw.Flush();if(raw.Position!=meta.Length)throw new IOException("Raw image length differs from metadata.");raw.Position=0;using var hash=System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);byte[] buffer=new byte[4*1024*1024];long done=0;
                while(done<meta.Length){int n=(int)Math.Min(buffer.Length,meta.Length-done);await raw.ReadExactlyAsync(buffer.AsMemory(0,n),ct);hash.AppendData(buffer,0,n);done+=n;progress?.Report(new("Verifying browsing copy",100.0*done/meta.Length,Data.Size(done)+" / "+Data.Size(meta.Length),done,meta.Length));}
                var manifest=await vault.LoadAsync(id,ct);if(!Convert.ToHexString(hash.GetHashAndReset()).Equals(manifest.Entries.Single(e=>e.Path=="disk.sdi").Resources.Single().Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("Browsing copy failed its full read-back hash.");
                if(meta.Table!=null){var table=DiskTables.Read(raw,meta.SectorSize);var fresh=table.Style=="GPT"?table with{Id=Guid.NewGuid(),Partitions=table.Partitions.Select(p=>p with{Id=Guid.NewGuid()}).ToList()}:table with{Signature=BitConverter.ToUInt32(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4))};DiskTables.Write(raw,fresh);raw.Flush();}
            }
            await DetachAsync(vhd);attached=false;await StorageService.PowerShellAsync("$p=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+encoded+"'));$d=Mount-DiskImage -ImagePath $p -Access ReadOnly -PassThru|Get-Disk;if(!$d.IsReadOnly){throw 'The browsing image did not attach read-only'};if($d.IsOffline){Set-Disk -Number $d.Number -IsOffline $false}",ct);attached=true;complete=true;progress?.Report(new("Image attached read-only",100,"Open File Explorer to copy selected files. The temporary copy has fresh disk identities; the protected archive is unchanged. Detach the VHD before deleting it."));
        }
        finally{if(!complete){if(attached){try{await DetachAsync(vhd);attached=false;}catch{}}if(!attached&&File.Exists(vhd))File.Delete(vhd);}}
    }
    public static async Task DetachAsync(string vhd){BackupEngine.RejectExistingAncestorLinks(vhd);string encoded=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(vhd)));await StorageService.PowerShellAsync("$p=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+encoded+"'));Dismount-DiskImage -ImagePath $p -ErrorAction Stop");}
    public static async Task ExtractWimPathAsync(string wim,string relative,string destination,IProgress<DriveProgress>? progress=null,CancellationToken ct=default)
    {
        BackupEngine.ValidateRelative(relative.Replace('\\','/'));BackupEngine.RejectExistingAncestorLinks(wim);BackupEngine.RejectExistingAncestorLinks(destination);if(Directory.Exists(destination)&&Directory.EnumerateFileSystemEntries(destination).Any())throw new IOException("Extract to an empty folder.");Directory.CreateDirectory(destination);string engine=Path.Combine(AppContext.BaseDirectory,"Tools","wimlib","wimlib-imagex.exe");await ProtectedImageService.Tool(engine,["verify",wim],"Verifying WIM before extraction",progress,ct);await ProtectedImageService.Tool(engine,["extract",wim,"1",relative,"--dest-dir="+destination,"--preserve-dir-structure","--no-acls"],"Extracting selected image path",progress,ct);
    }
    internal static byte[] Footer(long length)
    {
        byte[] b=new byte[512];System.Text.Encoding.ASCII.GetBytes("conectix").CopyTo(b,0);void U32(int p,uint n)=>BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(p),n);void U64(int p,ulong n)=>BinaryPrimitives.WriteUInt64BigEndian(b.AsSpan(p),n);U32(8,2);U32(12,0x10000);U64(16,ulong.MaxValue);U32(24,(uint)Math.Max(0,(DateTime.UtcNow-new DateTime(2000,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds));System.Text.Encoding.ASCII.GetBytes("SOOP").CopyTo(b,28);U32(32,0x10000);System.Text.Encoding.ASCII.GetBytes("Wi2k").CopyTo(b,36);U64(40,(ulong)length);U64(48,(ulong)length);
        long total=Math.Min(length/512,65535L*16*255),cylinderTimesHeads;int heads,sectors;if(total>=65535L*16*63){sectors=255;heads=16;cylinderTimesHeads=total/sectors;}else{sectors=17;cylinderTimesHeads=total/sectors;heads=(int)((cylinderTimesHeads+1023)/1024);heads=Math.Max(4,heads);if(cylinderTimesHeads>=heads*1024||heads>16){sectors=31;heads=16;cylinderTimesHeads=total/sectors;}if(cylinderTimesHeads>=heads*1024){sectors=63;heads=16;cylinderTimesHeads=total/sectors;}}BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(56),(ushort)(cylinderTimesHeads/heads));b[58]=(byte)heads;b[59]=(byte)sectors;U32(60,2);Guid.NewGuid().TryWriteBytes(b.AsSpan(68,16),true,out _);uint checksum=0;foreach(byte n in b)checksum+=n;U32(64,~checksum);return b;
    }
    sealed class SparseWriter(Stream file):Stream
    {
        public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;public override long Length=>file.Length;public override long Position{get=>file.Position;set=>throw new NotSupportedException();}public override void Write(byte[] b,int o,int n){if(b.AsSpan(o,n).IndexOfAnyExcept((byte)0)<0)file.Position+=n;else file.Write(b,o,n);}public override ValueTask WriteAsync(ReadOnlyMemory<byte> b,CancellationToken ct=default){ct.ThrowIfCancellationRequested();if(b.Span.IndexOfAnyExcept((byte)0)<0){file.Position+=b.Length;return ValueTask.CompletedTask;}return file.WriteAsync(b,ct);}public override void Flush()=>file.Flush();public override int Read(byte[] b,int o,int n)=>throw new NotSupportedException();public override long Seek(long o,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long n)=>throw new NotSupportedException();
    }
}
