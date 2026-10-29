using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public static class ImageBrowseService
{
    public static async Task MountReadOnlyAsync(string repository,string? password,string id,string vhd,IProgress<DriveProgress>? progress=null,CancellationToken ct=default)
    {
        if(!StorageService.IsAdmin)throw new UnauthorizedAccessException("Administrator access is required to attach an image.");BackupEngine.RejectExistingAncestorLinks(vhd);if(File.Exists(vhd))throw new IOException("Choose a new VHD filename.");using var vault=await VaultBackup.OpenAsync(repository,password,ct:ct);var meta=await ProtectedImageService.MetadataAsync(vault,id,ct);if(meta.Kind!="Raw"||meta.SectorSize!=512||meta.Length<=0||meta.Length>2040L*1024*1024*1024)throw new IOException("Read-only VHD browsing supports raw images with 512-byte sectors up to 2040 GiB. Export other images for direct recovery inspection.");
        bool complete=false;try
        {
            await using(var file=new FileStream(vhd,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,4*1024*1024,FileOptions.Asynchronous)){if(!DiskAccess.DeviceIoControl(file.SafeFileHandle,0x900c4,null,0,null,0,out _,IntPtr.Zero))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());using var sparse=new SparseWriter(file);await vault.ExportFileAsync(id,"disk.sdi",sparse,ct,progress);if(file.Position!=meta.Length)throw new IOException("Raw image length differs from metadata.");await file.WriteAsync(Footer(meta.Length),ct);file.Flush(true);}complete=true;
            string encoded=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(vhd)));await StorageService.PowerShellAsync("$p=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+encoded+"'));Mount-DiskImage -ImagePath $p -Access ReadOnly -PassThru|Out-Null");progress?.Report(new("Image attached read-only",100,"Open File Explorer to copy selected files. Detach this VHD before deleting its exported file."));
        }
        finally{if(!complete&&File.Exists(vhd))File.Delete(vhd);}
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
    sealed class SparseWriter(FileStream file):Stream
    {
        public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;public override long Length=>file.Length;public override long Position{get=>file.Position;set=>throw new NotSupportedException();}public override void Write(byte[] b,int o,int n){if(b.AsSpan(o,n).IndexOfAnyExcept((byte)0)<0)file.Position+=n;else file.Write(b,o,n);}public override ValueTask WriteAsync(ReadOnlyMemory<byte> b,CancellationToken ct=default){ct.ThrowIfCancellationRequested();if(b.Span.IndexOfAnyExcept((byte)0)<0){file.Position+=b.Length;return ValueTask.CompletedTask;}return file.WriteAsync(b,ct);}public override void Flush()=>file.Flush(true);public override int Read(byte[] b,int o,int n)=>throw new NotSupportedException();public override long Seek(long o,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long n)=>throw new NotSupportedException();
    }
}
