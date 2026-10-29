using System.Buffers.Binary;
using System.Text;
namespace SoopaDriveTool.Core;
public static partial class DiskRecovery
{
    public static async Task<List<RecoverableFile>> CarveAsync(Stream disk,long start,long length,int maxFiles=10000,IProgress<DriveProgress>? progress=null,CancellationToken ct=default)
    {
        if(start<0||length<1||start>disk.Length-length)throw new IOException("Invalid carving range.");byte[] buffer=new byte[1024*1024+8];long offset=start,end=start+length;var files=new List<RecoverableFile>();
        while(offset<end&&files.Count<maxFiles)
        {
            ct.ThrowIfCancellationRequested();int n=(int)Math.Min(buffer.Length,end-offset);disk.Position=offset;await disk.ReadExactlyAsync(buffer.AsMemory(0,n),ct);int step=Math.Min(1024*1024,n);
            for(int i=0;i<step&&i<n-8;i++)
            {
                long here=offset+i,size=0;string extension="";var b=buffer.AsSpan(i);
                if(b[..8].SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})){size=PngLength(disk,here,end,ct);extension="png";}
                else if(b[0]==0xff&&b[1]==0xd8&&b[2]==0xff){size=FindEnd(disk,here+3,Math.Min(end,here+128L*1024*1024),[255,217],ct);if(size>0)size=size-here;extension="jpg";}
                else if(b[..5].SequenceEqual("%PDF-"u8)){size=FindEnd(disk,here+5,Math.Min(end,here+256L*1024*1024),Encoding.ASCII.GetBytes("%%EOF"),ct);if(size>0)size=size-here;extension="pdf";}
                if(size>0){files.Add(new($"carved_{here:x}.{extension}",size,"Contiguous signature carving",extension=="png"?"PNG chunk bounds and CRCs validated":"Signature/end marker found; fragmented or overwritten files may be incomplete",[new(here,size)]));if(files.Count>=maxFiles)break;}
            }
            offset+=step;progress?.Report(new("Carving file signatures",100.0*(offset-start)/length,$"{files.Count} candidates · {Data.Size(offset-start)} / {Data.Size(length)}",offset-start,length));
        }return files;
    }
    static long PngLength(Stream disk,long start,long end,CancellationToken ct)
    {
        long cursor=start+8;bool ihdr=false;int chunks=0;
        try{while(cursor<=end-12&&cursor-start<256L*1024*1024&&chunks++<100000){ct.ThrowIfCancellationRequested();byte[] h=DiskTables.ReadAt(disk,cursor,8);uint n=BinaryPrimitives.ReadUInt32BigEndian(h);if(n>16*1024*1024||cursor>end-12-n)return 0;string type=Encoding.ASCII.GetString(h,4,4);if(!ihdr){if(type!="IHDR"||n!=13)return 0;ihdr=true;}var data=DiskTables.ReadAt(disk,cursor+4,checked((int)n+8));uint crc=BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(data.Length-4));if(PartitionInspector.Crc32(data.AsSpan(0,data.Length-4))!=crc)return 0;cursor+=12+n;if(type=="IEND"&&n==0)return cursor-start;}}catch(IOException){}return 0;
    }
    static long FindEnd(Stream disk,long start,long end,byte[] marker,CancellationToken ct)
    {
        byte[] b=new byte[65536+marker.Length];long cursor=start;while(cursor<end){ct.ThrowIfCancellationRequested();int n=(int)Math.Min(b.Length,end-cursor);disk.Position=cursor;disk.ReadExactly(b.AsSpan(0,n));int index=b.AsSpan(0,n).IndexOf(marker);if(index>=0)return cursor+index+marker.Length;if(n<=marker.Length)break;cursor+=n-marker.Length+1;}return 0;
    }
    public static Task<List<RecoverableFile>> ScanFatDeletedAsync(Stream disk,long volume,long length,IProgress<DriveProgress>? progress=null,CancellationToken ct=default)
    {
        byte[] boot=DiskTables.ReadAt(disk,volume,512);if(Encoding.ASCII.GetString(boot,82,8)!="FAT32   ")throw new IOException("Select a FAT32 volume.");int sector=BitConverter.ToUInt16(boot,11),cluster=checked(sector*boot[13]),reserved=BitConverter.ToUInt16(boot,14),fats=boot[16];long fatBytes=(long)BitConverter.ToUInt32(boot,36)*sector,data=volume+(reserved+(long)fats*BitConverter.ToUInt32(boot,36))*sector;
        if(sector is not (512 or 4096)||cluster<sector||cluster>1024*1024||fats is not (1 or 2)||data<volume||data>=volume+length)throw new IOException("Invalid FAT32 geometry.");uint root=BitConverter.ToUInt32(boot,44);long clusters=(volume+length-data)/cluster;if(clusters>fatBytes/4-2)throw new IOException("FAT32 allocation table is too small for its data area.");var visited=new HashSet<uint>();var dirs=new Queue<uint>();dirs.Enqueue(root);var files=new List<RecoverableFile>();int scanned=0;
        while(dirs.Count>0&&scanned<1000000)
        {
            uint current=dirs.Dequeue();while(current>=2&&current<0x0ffffff8&&current<clusters+2&&visited.Add(current))
            {
                ct.ThrowIfCancellationRequested();long location=data+(current-2L)*cluster;byte[] contents=DiskTables.ReadAt(disk,location,cluster);
                for(int i=0;i<=contents.Length-32;i+=32)
                {
                    var e=contents.AsSpan(i,32);if(e[0]==0)break;if(e[11]==15||(e[11]&8)!=0)continue;uint first=((uint)BitConverter.ToUInt16(e.Slice(20,2))<<16)|BitConverter.ToUInt16(e.Slice(26,2));bool directory=(e[11]&16)!=0;
                    if(e[0]!=0xe5){if(directory&&e[0]!=(byte)'.'&&first>=2)dirs.Enqueue(first);continue;}
                    if(directory)continue;uint size=BitConverter.ToUInt32(e[28..]);long offset=data+(first-2L)*cluster;if(first<2||size==0||offset<data||offset>volume+length-size)continue;
                    byte[] shortname=e[..11].ToArray();shortname[0]=(byte)'_';string name=Encoding.ASCII.GetString(shortname,0,8).TrimEnd()+"."+Encoding.ASCII.GetString(shortname,8,3).TrimEnd();string condition="Contiguous reconstruction; deleted FAT chains and the first filename character are lost. Inspect content.";
                    bool allocated=false;for(long c=first;c<first+(size+cluster-1)/cluster;c++){byte[] entry=DiskTables.ReadAt(disk,volume+reserved*(long)sector+c*4,4);if((BitConverter.ToUInt32(entry)&0x0fffffff)!=0){allocated=true;break;}}if(allocated)condition="One or more clusters are now allocated; content may be overwritten.";
                    files.Add(new($"fat_{location+i:x}_{SafeName(name)}",size,"FAT32 deleted short directory entry",condition,[new(offset,size)]));
                }
                scanned++;byte[] next=DiskTables.ReadAt(disk,volume+reserved*(long)sector+current*4,4);current=BitConverter.ToUInt32(next)&0x0fffffff;progress?.Report(new("Scanning FAT32 directories",null,$"{scanned} directory clusters · {files.Count} deleted candidates"));
            }
        }return Task.FromResult(files);
    }
}
