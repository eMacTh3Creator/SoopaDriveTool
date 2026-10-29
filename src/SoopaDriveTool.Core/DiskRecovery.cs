using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
namespace SoopaDriveTool.Core;
public record PartitionCandidate(long Offset,long Length,string FileSystem,string Evidence);
public record RecoveredExtent(long Offset,long Length,bool Zero=false);
public record RecoverableFile(string Name,long Length,string Method,string Condition,List<RecoveredExtent> Extents,byte[]? Resident=null);
public record SurfaceBlock(long Offset,long Length,double Milliseconds,string Status,string? Error=null);
public static partial class DiskRecovery
{
    public static async Task<List<PartitionCandidate>> ScanPartitionsAsync(Stream disk,int sector=512,IProgress<DriveProgress>? progress=null,CancellationToken ct=default)
    {
        if(sector is not (512 or 4096))throw new IOException("Unsupported sector size.");var rows=new List<PartitionCandidate>();byte[] buffer=new byte[4*1024*1024];long offset=0;
        while(offset<disk.Length)
        {
            ct.ThrowIfCancellationRequested();int n=(int)Math.Min(buffer.Length,disk.Length-offset);disk.Position=offset;await disk.ReadExactlyAsync(buffer.AsMemory(0,n),ct);
            for(int i=0;i<=n-sector;i+=sector)
            {
                var b=buffer.AsSpan(i,sector);if(b[510]!=0x55||b[511]!=0xaa)continue;long start=offset+i,length=0;string fs="";string oem=Encoding.ASCII.GetString(b.Slice(3,8));
                if(oem=="NTFS    "&&BinaryPrimitives.ReadUInt16LittleEndian(b[11..])==sector&&b[13]>0){ulong count=BinaryPrimitives.ReadUInt64LittleEndian(b[40..]);if(count<=(ulong)(disk.Length-start)/ (uint)sector){length=(long)count*sector;fs="NTFS";}}
                else if(oem=="EXFAT   "&&b[108] is >=9 and <=12&&(1<<b[108])==sector){ulong count=BinaryPrimitives.ReadUInt64LittleEndian(b[72..]),origin=BinaryPrimitives.ReadUInt64LittleEndian(b[64..]);if(origin==(ulong)start/(uint)sector&&count<=(ulong)(disk.Length-start)/(uint)sector){length=(long)count*sector;fs="exFAT";}}
                else if(Encoding.ASCII.GetString(b.Slice(82,8))=="FAT32   "&&BinaryPrimitives.ReadUInt16LittleEndian(b[11..])==sector&&b[13]>0){length=(long)BinaryPrimitives.ReadUInt32LittleEndian(b[32..])*sector;fs="FAT32";}
                if(length<16L*1024*1024||start==0||start>disk.Length-length)continue;
                bool backup=false;if(fs=="NTFS"){var end=DiskTables.ReadAt(disk,start+length-sector,sector);backup=end[510]==0x55&&end[511]==0xaa&&end.AsSpan(3,8).SequenceEqual(b.Slice(3,8))&&end.AsSpan(40,8).SequenceEqual(b.Slice(40,8));if(!backup)continue;}
                rows.Add(new(start,length,fs,backup?"Matching primary and backup NTFS boot sectors; bounds checked":"Filesystem boot signature and geometry; inspect before restoring"));
            }
            offset+=n;progress?.Report(new("Discovering lost partitions",100.0*offset/disk.Length,$"{rows.Count} candidates · scanned {Data.Size(offset)} / {Data.Size(disk.Length)}",offset,disk.Length));
        }
        return rows.DistinctBy(x=>(x.Offset,x.Length)).OrderBy(x=>x.Offset).ToList();
    }
    public static DiskTable RestoreCandidate(DiskTable table,PartitionCandidate candidate)
    {
        if(candidate.FileSystem is not ("NTFS" or "exFAT" or "FAT32")||table.Partitions.Any(p=>p.Offset<candidate.Offset+candidate.Length&&p.Offset+p.Length>candidate.Offset))throw new IOException("The candidate overlaps an existing partition or has an unsupported filesystem.");
        int index=Enumerable.Range(1,table.Style=="GPT"?128:4).FirstOrDefault(i=>table.Partitions.All(p=>p.Index!=i));if(index==0)throw new IOException("No free partition-table slot.");
        var parts=table.Partitions.Append(new TablePartition(index,candidate.Offset,candidate.Length,DiskTables.Basic,table.Style=="GPT"?Guid.NewGuid():Guid.Empty,"Recovered",MbrType:candidate.FileSystem=="FAT32"?(byte)0x0c:(byte)7)).ToList();DiskTables.ValidatePartitions(parts,table.Length,table.SectorSize);return table with{Partitions=parts};
    }
    public static async Task<List<SurfaceBlock>> SurfaceScanAsync(Stream disk,long start,long length,int block=4*1024*1024,IProgress<DriveProgress>? progress=null,CancellationToken ct=default)
    {
        if(start<0||length<0||start>disk.Length-length||block<512||block>16*1024*1024)throw new IOException("Invalid scan range.");var rows=new List<SurfaceBlock>();byte[] buffer=new byte[block];long done=0;var timer=System.Diagnostics.Stopwatch.StartNew();
        while(done<length)
        {
            ct.ThrowIfCancellationRequested();int n=(int)Math.Min(block,length-done);string status="Readable";string? error=null;var watch=System.Diagnostics.Stopwatch.StartNew();try{disk.Position=start+done;await disk.ReadExactlyAsync(buffer.AsMemory(0,n),ct);}catch(IOException e){status="Read error";error=e.Message;}
            watch.Stop();if(status=="Readable"&&watch.ElapsedMilliseconds>500)status="Slow";rows.Add(new(start+done,n,watch.Elapsed.TotalMilliseconds,status,error));done+=n;
            progress?.Report(new("Reading surface",100.0*done/length,$"{rows.Count(x=>x.Status=="Read error")} unreadable regions · {Data.Size(done)} / {Data.Size(length)}",done,length,done/Math.Max(.001,timer.Elapsed.TotalSeconds)));
        }return rows;
    }
    public static async Task WriteSectorAsync(Stream disk,long offset,byte[] replacement,string expectedHash,string backup,int sector,CancellationToken ct=default)
    {
        if(offset%sector!=0||replacement.Length==0||replacement.Length%sector!=0||replacement.Length>65536)throw new IOException("Sector writes must be aligned and no larger than 64 KiB.");var before=DiskTables.ReadAt(disk,offset,replacement.Length);if(!Convert.ToHexString(SHA256.HashData(before)).Equals(expectedHash,StringComparison.OrdinalIgnoreCase))throw new IOException("Sector contents changed after preview. No write was made.");
        BackupEngine.RejectExistingAncestorLinks(backup);if(File.Exists(backup))throw new IOException("Choose a new sector backup file.");Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(backup))!);
        await using(var f=new FileStream(backup,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){await f.WriteAsync(before,ct);f.Flush(true);}
        ct.ThrowIfCancellationRequested();DiskTables.WriteAt(disk,offset,replacement);if(disk is FileStream fs)fs.Flush(true);else disk.Flush();if(!DiskTables.ReadAt(disk,offset,replacement.Length).SequenceEqual(replacement))throw new IOException("Sector write failed read-back verification. Original bytes are saved in "+backup);
    }
    public static async Task ExportAsync(Stream source,RecoverableFile file,string destination,CancellationToken ct=default)
    {
        BackupEngine.RejectExistingAncestorLinks(destination);if(File.Exists(destination))throw new IOException("Recovery never overwrites an existing file.");string temp=destination+".partial";Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        try{await using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){if(file.Resident!=null)await output.WriteAsync(file.Resident,ct);else{byte[] b=new byte[1024*1024];long done=0;foreach(var run in file.Extents){long nrun=Math.Min(run.Length,file.Length-done);if(nrun<=0)break;long copied=0;while(copied<nrun){ct.ThrowIfCancellationRequested();int n=(int)Math.Min(b.Length,nrun-copied);if(run.Zero)Array.Clear(b,0,n);else{source.Position=run.Offset+copied;await source.ReadExactlyAsync(b.AsMemory(0,n),ct);}await output.WriteAsync(b.AsMemory(0,n),ct);copied+=n;done+=n;}}if(done!=file.Length)throw new IOException("Incomplete recovery extents.");}output.Flush(true);}if(new FileInfo(temp).Length!=file.Length)throw new IOException("Recovered file length mismatch.");File.Move(temp,destination);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static async Task<List<RecoverableFile>> ScanNtfsDeletedAsync(Stream disk,long volume,long length,int maxRecords=1000000,IProgress<DriveProgress>? progress=null,CancellationToken ct=default)
    {
        byte[] boot=DiskTables.ReadAt(disk,volume,512);if(Encoding.ASCII.GetString(boot,3,8)!="NTFS    ")throw new IOException("Select an NTFS volume.");int sector=BitConverter.ToUInt16(boot,11),cluster=checked(sector*boot[13]);int scale=(sbyte)boot[64],record=scale<0?1<<-scale:checked(scale*cluster);if(sector is not (512 or 4096)||cluster<sector||cluster>2*1024*1024||record<512||record>65536)throw new IOException("Unsupported NTFS geometry.");
        long mft=checked(volume+(long)BitConverter.ToUInt64(boot,48)*cluster);byte[] first=DiskTables.ReadAt(disk,mft,record);FixRecord(first,sector);var mftData=Attributes(first).FirstOrDefault(a=>BitConverter.ToUInt32(a,0)==0x80&&a[9]==0)??throw new IOException("NTFS MFT data is unavailable.");if(mftData[8]==0)throw new IOException("Unexpected resident MFT.");long mftSize=checked((long)BitConverter.ToUInt64(mftData,48));var mftRuns=Runs(mftData,cluster,volume,length);int count=(int)Math.Min(maxRecords,mftSize/record);var rows=new List<RecoverableFile>();
        for(int i=16;i<count;i++)
        {
            ct.ThrowIfCancellationRequested();byte[] bytes=ReadMapped(disk,mftRuns,(long)i*record,record);if(Encoding.ASCII.GetString(bytes,0,4)!="FILE"||(BitConverter.ToUInt16(bytes,22)&3)!=0||BitConverter.ToUInt64(bytes,32)!=0)continue;
            try
            {
                FixRecord(bytes,sector);var attrs=Attributes(bytes).ToList();var names=attrs.Where(a=>BitConverter.ToUInt32(a,0)==0x30&&a[8]==0).Select(Resident).Where(n=>n.Length>=66&&n[65]!=2&&66+n[64]*2<=n.Length).ToList();if(names.Count==0)continue;string name=Encoding.Unicode.GetString(names[0],66,names[0][64]*2);name=SafeName(name);var data=attrs.SingleOrDefault(a=>BitConverter.ToUInt32(a,0)==0x80&&a[9]==0);if(data==null)continue;
                if((BitConverter.ToUInt16(data,12)&0xc001)!=0)continue; // compressed, sparse or EFS needs its own decoder
                if(data[8]==0){byte[] value=Resident(data);rows.Add(new($"{i}_{name}",value.Length,"NTFS deleted MFT record","Resident bytes retained in MFT",[],value));}
                else{long size=checked((long)BitConverter.ToUInt64(data,48));if(size<0||size>length)continue;var runs=Runs(data,cluster,volume,length);if(runs.Sum(r=>r.Length)<size)continue;rows.Add(new($"{i}_{name}",size,"NTFS deleted MFT record","Cluster content may have been reused; inspect exported data",runs));}
            }
            catch(Exception e)when(e is IOException or OverflowException or ArgumentException or InvalidOperationException){/* damaged records remain excluded */}
            if(i%2048==0)progress?.Report(new("Reading deleted NTFS records",100.0*i/count,$"{rows.Count} recoverable candidates · MFT record {i} / {count}"));
        }return rows;
    }
    static IEnumerable<byte[]> Attributes(byte[] record){int used=BitConverter.ToInt32(record,24),offset=BitConverter.ToUInt16(record,20);if(used<offset||used>record.Length)throw new IOException("Invalid MFT record bounds.");while(offset<=used-16){uint type=BitConverter.ToUInt32(record,offset);if(type==uint.MaxValue)yield break;int n=BitConverter.ToInt32(record,offset+4);if(n<24||offset>used-n)throw new IOException("Invalid MFT attribute.");yield return record.AsSpan(offset,n).ToArray();offset+=n;}}
    static byte[] Resident(byte[] a){int n=BitConverter.ToInt32(a,16),offset=BitConverter.ToUInt16(a,20);if(n<0||offset<24||offset>a.Length-n)throw new IOException("Invalid resident data.");return a.AsSpan(offset,n).ToArray();}
    static void FixRecord(byte[] b,int sector){if(Encoding.ASCII.GetString(b,0,4)!="FILE")throw new IOException("Invalid MFT record signature.");int offset=BitConverter.ToUInt16(b,4),count=BitConverter.ToUInt16(b,6);if(count!=b.Length/sector+1||offset<8||offset>b.Length-2*count)throw new IOException("Invalid update sequence.");for(int i=1;i<count;i++){int end=i*sector-2;if(b[end]!=b[offset]||b[end+1]!=b[offset+1])throw new IOException("Torn NTFS record.");b[end]=b[offset+i*2];b[end+1]=b[offset+i*2+1];}}
    static List<RecoveredExtent> Runs(byte[] a,int cluster,long volume,long length){int cursor=BitConverter.ToUInt16(a,32);long lcn=0;var runs=new List<RecoveredExtent>();while(cursor<a.Length&&a[cursor]!=0){byte h=a[cursor++];int size=h&15,off=h>>4;if(size<1||size>8||off>8||cursor>a.Length-size-off)throw new IOException("Invalid NTFS mapping pairs.");ulong n=0;for(int j=0;j<size;j++)n|=(ulong)a[cursor+j]<<(8*j);cursor+=size;if(n==0||n>(ulong)(length/cluster))throw new IOException("Invalid NTFS run length.");long delta=0;if(off>0){for(int j=0;j<off;j++)delta|=(long)a[cursor+j]<<(8*j);if(off<8&&(a[cursor+off-1]&128)!=0)delta|=-1L<<(off*8);}cursor+=off;lcn=checked(lcn+delta);long bytes=checked((long)n*cluster),start=checked(lcn*cluster);if(off>0&&(start<0||start>length-bytes))throw new IOException("NTFS extent escapes its volume.");runs.Add(new(volume+start,bytes,off==0));if(runs.Count>100000)throw new IOException("Too many NTFS runs.");}return runs;}
    static byte[] ReadMapped(Stream disk,List<RecoveredExtent> runs,long offset,int n){var b=new byte[n];int done=0;long skipped=0;foreach(var run in runs){if(offset>=skipped+run.Length){skipped+=run.Length;continue;}long relative=Math.Max(0,offset-skipped);int take=(int)Math.Min(n-done,run.Length-relative);if(!run.Zero){disk.Position=run.Offset+relative;disk.ReadExactly(b.AsSpan(done,take));}done+=take;offset+=take;skipped+=run.Length;if(done==n)return b;}throw new IOException("MFT extent map is incomplete.");}
    public static string SafeName(string name){var chars=name.Select(c=>char.IsControl(c)||"\\/:*?\"<>|".Contains(c)?'_':c).ToArray();var safe=new string(chars).Trim().Trim('.');return string.IsNullOrEmpty(safe)?"recovered.bin":safe;}
}
