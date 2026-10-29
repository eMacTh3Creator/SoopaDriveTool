using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
// All raw writes use the same validated device handle for their full lifetime.
public static class DiskAccess
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool DeviceIoControl(SafeFileHandle handle,uint code,byte[]? input,int inputBytes,byte[]? output,int outputBytes,out int returned,IntPtr overlapped);
    static SafeFileHandle OpenHandle(string path,bool write){var h=CreateFile(path,write?0xc0000000u:0x80000000u,3,IntPtr.Zero,3,0,IntPtr.Zero);if(h.IsInvalid){int e=Marshal.GetLastWin32Error();h.Dispose();throw new Win32Exception(e);}return h;}
    static byte[] Query(SafeFileHandle h,uint code,byte[]? input,int max){var b=new byte[max];if(!DeviceIoControl(h,code,input,input?.Length??0,b,b.Length,out int n,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());return b.AsSpan(0,n).ToArray();}
    static long Length(SafeFileHandle h)=>BitConverter.ToInt64(Query(h,0x7405c,null,8));
    static string Fingerprint(SafeFileHandle h)
    {
        byte[] query=new byte[12];var descriptor=Query(h,0x2d1400,query,65536);if(descriptor.Length<36)throw new IOException("Invalid storage descriptor."); // StorageDeviceProperty
        // Stable descriptor contents bind vendor/product/revision/serial/bus data.
        return Convert.ToHexString(SHA256.HashData(descriptor)).ToLowerInvariant();
    }
    public static string Fingerprint(int number){using var h=OpenHandle(@"\\.\PhysicalDrive"+number,false);return Fingerprint(h);}
    static bool ContainsRunningWindows(int number)
    {
        var root=Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!;using var h=OpenHandle(@"\\.\"+root.TrimEnd('\\'),false);var extents=Query(h,0x560000,null,65536);if(extents.Length<8)throw new IOException("Windows disk protection could not be determined.");int count=BitConverter.ToInt32(extents);if(count<1||count>1024||extents.Length<8+count*24)throw new IOException("Invalid Windows volume extent data.");for(int i=0;i<count;i++)if(BitConverter.ToInt32(extents,8+i*24)==number)return true;return false;
    }
    public static BoundDiskStream Open(DiskInfo expected,bool write,bool requireOffline=true)
    {
        if(write&&expected.InventoryWarning.Length>0)throw new IOException("Windows could not read complete disk information. Disk writes are disabled; refresh first.");
        if(!StorageService.IsAdmin)throw new UnauthorizedAccessException("Administrator access is required for direct disk I/O.");if(write&&(expected.Protected||expected.ReadOnly))throw new IOException("This disk is protected or read-only.");if(requireOffline&&!expected.Offline)throw new IOException("Take this data disk offline before direct disk access.");
        var h=OpenHandle(@"\\.\PhysicalDrive"+expected.Number,write);
        try
        {
            var number=Query(h,0x2d1080,null,12);if(number.Length<12||BitConverter.ToInt32(number,4)!=expected.Number||Length(h)!=expected.Size)throw new IOException("The retained device handle does not match the selected disk geometry.");
            string actual=Fingerprint(h);if(string.IsNullOrEmpty(expected.NativeIdentity)||actual!=expected.NativeIdentity)throw new IOException("The selected hardware descriptor changed. Refresh and prepare the operation again.");
            if(write&&ContainsRunningWindows(expected.Number))throw new IOException("Writes to the running Windows disk are blocked by native extent checks.");
            return new(new FileStream(h,write?FileAccess.ReadWrite:FileAccess.Read,1,false),expected.Size,h,expected.SectorSize);
        }
        catch{h.Dispose();throw;}
    }
    public sealed class BoundDiskStream(FileStream stream,long length,SafeFileHandle handle,int sector=512):Stream
    {
        long cursor;
        public SafeFileHandle Handle=>handle;public override bool CanRead=>stream.CanRead;public override bool CanWrite=>stream.CanWrite;public override bool CanSeek=>true;public override long Length=>length;public override long Position{get=>cursor;set{if(value<0||value>length)throw new IOException("Seek exceeds the disk.");cursor=value;}}
        int Count(int requested)=>(int)Math.Min(requested,length-cursor);
        public override int Read(byte[] buffer,int offset,int count)=>Read(buffer.AsSpan(offset,count));
        public override int Read(Span<byte> buffer)
        {
            int n=Count(buffer.Length);if(n==0)return 0;long begin=cursor/sector*sector;int bytes=checked((int)(((cursor+n+sector-1)/sector*sector)-begin));stream.Position=begin;
            if(begin==cursor&&bytes==n)stream.ReadExactly(buffer[..n]);else{byte[] temporary=System.Buffers.ArrayPool<byte>.Shared.Rent(bytes);try{stream.ReadExactly(temporary.AsSpan(0,bytes));temporary.AsSpan((int)(cursor-begin),n).CopyTo(buffer);}finally{System.Buffers.ArrayPool<byte>.Shared.Return(temporary);}}cursor+=n;return n;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)
        {
            int n=Count(buffer.Length);if(n==0)return 0;long begin=cursor/sector*sector;int bytes=checked((int)(((cursor+n+sector-1)/sector*sector)-begin));stream.Position=begin;
            if(begin==cursor&&bytes==n)await stream.ReadExactlyAsync(buffer[..n],ct);else{byte[] temporary=System.Buffers.ArrayPool<byte>.Shared.Rent(bytes);try{await stream.ReadExactlyAsync(temporary.AsMemory(0,bytes),ct);temporary.AsMemory((int)(cursor-begin),n).CopyTo(buffer);}finally{System.Buffers.ArrayPool<byte>.Shared.Return(temporary);}}cursor+=n;return n;
        }
        void Writable(int count){if(cursor%sector!=0||count%sector!=0||count>length-cursor)throw new IOException("Direct disk writes must be whole sectors within the retained disk bounds.");stream.Position=cursor;}
        public override void Write(byte[] b,int o,int n)=>Write(b.AsSpan(o,n));public override void Write(ReadOnlySpan<byte> b){Writable(b.Length);stream.Write(b);cursor+=b.Length;}
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> b,CancellationToken ct=default){Writable(b.Length);await stream.WriteAsync(b,ct);cursor+=b.Length;}
        public override void Flush()=>stream.Flush(true);public override Task FlushAsync(CancellationToken ct)=>stream.FlushAsync(ct);public override long Seek(long o,SeekOrigin origin){Position=origin==SeekOrigin.Begin?o:origin==SeekOrigin.Current?cursor+o:length+o;return cursor;}public override void SetLength(long value)=>throw new NotSupportedException();protected override void Dispose(bool disposing){if(disposing)stream.Dispose();base.Dispose(disposing);}
    }
    public static async Task RequireSeparatePathAsync(string path,params int[] disks)
    {
        path=Path.GetFullPath(path);BackupEngine.RejectExistingAncestorLinks(path);if(path.StartsWith(@"\\"))throw new IOException("Use a local folder for recovery checkpoints and disk images.");var root=Path.GetPathRoot(path)!;if(root.Length<3||!char.IsAsciiLetter(root[0]))throw new IOException("A local drive-letter path is required.");int number=int.Parse(await StorageService.PowerShellAsync($"(Get-Partition -DriveLetter '{char.ToUpperInvariant(root[0])}' -ErrorAction Stop).DiskNumber"));if(disks.Contains(number))throw new IOException("The output and recovery checkpoint must be on a different physical disk.");
    }
}
