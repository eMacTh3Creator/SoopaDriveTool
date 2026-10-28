using System.Buffers.Binary;
using System.Text;
namespace SoopaDriveTool.Core;
public record RecoveredPartition(string Scheme,int Index,long Offset,long Length,string Type,string Name);
public static class PartitionInspector
{
    public static List<RecoveredPartition> Inspect(Stream image,int sectorSize=512)
    {
        if(sectorSize is not (512 or 4096)||image.Length<sectorSize*2)throw new IOException("Invalid image/sector size.");
        var mbr=new byte[512];image.Position=0;image.ReadExactly(mbr);
        if(mbr[510]!=0x55||mbr[511]!=0xaa)throw new IOException("No valid MBR signature. This is inspection only; no data was changed.");
        var result=new List<RecoveredPartition>();
        for(int i=0;i<4;i++)
        {
            var e=mbr.AsSpan(446+i*16,16);var type=e[4];if(type==0)continue;
            var start=(long)BinaryPrimitives.ReadUInt32LittleEndian(e[8..])*sectorSize;var length=(long)BinaryPrimitives.ReadUInt32LittleEndian(e[12..])*sectorSize;
            if(type==0xee){return ReadGpt(image,sectorSize);}
            if(start<sectorSize||length<=0||start>image.Length-length)throw new IOException("MBR entry exceeds the image.");
            result.Add(new("MBR",i+1,start,length,$"0x{type:X2}","Primary partition"));
        }
        return result;
    }
    static List<RecoveredPartition> ReadGpt(Stream image,int sector)
    {
        var h=new byte[sector];image.Position=sector;image.ReadExactly(h);
        if(Encoding.ASCII.GetString(h,0,8)!="EFI PART")throw new IOException("Invalid primary GPT header. Backup-header repair is not available in this release.");
        var size=BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(12));if(size<92||size>sector)throw new IOException("Invalid GPT header size.");
        var crc=BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(16));Array.Clear(h,16,4);if(Crc32(h.AsSpan(0,(int)size))!=crc)throw new IOException("GPT header checksum failed.");
        var lba=BinaryPrimitives.ReadUInt64LittleEndian(h.AsSpan(72));var count=BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(80));var width=BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(84));var tableCrc=BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(88));
        if(width<128||width>4096||count>16384||(ulong)count*width>16*1024*1024||lba>(ulong)image.Length/(ulong)sector)throw new IOException("Unsafe GPT table bounds.");
        var start=checked((long)lba*sector);var len=checked((int)(count*width));if(start<2L*sector||start>image.Length-len)throw new IOException("GPT table exceeds image.");
        var table=new byte[len];image.Position=start;image.ReadExactly(table);if(Crc32(table)!=tableCrc)throw new IOException("GPT partition-array checksum failed.");
        var currentLba=BinaryPrimitives.ReadUInt64LittleEndian(h.AsSpan(24));var backupLba=BinaryPrimitives.ReadUInt64LittleEndian(h.AsSpan(32));var firstUsable=BinaryPrimitives.ReadUInt64LittleEndian(h.AsSpan(40));var lastUsable=BinaryPrimitives.ReadUInt64LittleEndian(h.AsSpan(48));
        if(currentLba!=1||backupLba!=(ulong)image.Length/(ulong)sector-1||firstUsable>lastUsable||lastUsable>=backupLba||firstUsable<lba+((ulong)len+(ulong)sector-1)/(ulong)sector)throw new IOException("Invalid GPT usable bounds.");
        var result=new List<RecoveredPartition>();
        for(int i=0;i<count;i++)
        {
            var e=table.AsSpan(i*(int)width,(int)width);var type=new Guid(e[..16]);if(type==Guid.Empty)continue;
            var first=BinaryPrimitives.ReadUInt64LittleEndian(e[32..]);var last=BinaryPrimitives.ReadUInt64LittleEndian(e[40..]);
            if(first<firstUsable||last>lastUsable||last<first||last>=(ulong)image.Length/(ulong)sector)throw new IOException("GPT entry exceeds image.");
            result.Add(new("GPT",i+1,checked((long)first*sector),checked((long)(last-first+1)*sector),type.ToString(),Encoding.Unicode.GetString(e.Slice(56,72)).TrimEnd('\0')));
        }
        var ordered=result.OrderBy(p=>p.Offset).ToArray();for(int i=1;i<ordered.Length;i++)if(ordered[i-1].Offset+ordered[i-1].Length>ordered[i].Offset)throw new IOException("Overlapping GPT partitions.");
        return result;
    }
    public static uint Crc32(ReadOnlySpan<byte> data){uint c=0xffffffff;foreach(var b in data){c^=b;for(int i=0;i<8;i++)c=(c>>1)^((c&1)!=0?0xedb88320u:0);}return ~c;}
}
