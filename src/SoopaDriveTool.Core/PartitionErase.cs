using System.Diagnostics;
using System.Security.Cryptography;
namespace SoopaDriveTool.Core;

public record PartitionEraseResult(long Bytes,int Passes,string Sha256);

// Only the selected extent is ever read or written. The caller retains and
// verifies the native disk handle and partition table around this operation.
public static class PartitionErase
{
    public static void ValidateRange(long diskLength,long offset,long length,int sectorSize)
    {
        if(sectorSize is not (512 or 4096)||diskLength<=0||diskLength%sectorSize!=0||offset<sectorSize||length<=0||offset%sectorSize!=0||length%sectorSize!=0||length>diskLength||offset>diskLength-length)
            throw new IOException("The partition extent is not a bounded, sector-aligned data range.");
    }
    public static async Task<PartitionEraseResult> EraseAsync(Stream disk,long offset,long length,int sectorSize,WipeMode mode,IProgress<DriveProgress>? progress=null,CancellationToken ct=default)
    {
        if(!disk.CanSeek||!disk.CanRead||!disk.CanWrite)throw new IOException("Partition erasure requires a seekable, readable, writable disk stream.");
        if(!Enum.IsDefined(mode))throw new IOException("Unknown partition erase method.");
        ValidateRange(disk.Length,offset,length,sectorSize);ct.ThrowIfCancellationRequested();
        int passes=mode==WipeMode.ThreePass?3:1;byte[] buffer=new byte[1024*1024];string finalHash="";var clock=Stopwatch.StartNew();long lastUpdate=-200;
        for(int pass=0;pass<passes;pass++)
        {
            string pattern=mode==WipeMode.ThreePass?(pass==0?"random":pass==1?"ones":"zeros"):mode==WipeMode.Random?"random":"zeros";
            using var writtenHash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);disk.Position=offset;long done=0;
            while(done<length)
            {
                ct.ThrowIfCancellationRequested();int count=(int)Math.Min(buffer.Length,length-done);
                if(pattern=="random")RandomNumberGenerator.Fill(buffer.AsSpan(0,count));else buffer.AsSpan(0,count).Fill(pattern=="ones"?(byte)255:(byte)0);
                writtenHash.AppendData(buffer,0,count);await disk.WriteAsync(buffer.AsMemory(0,count),ct);done+=count;Report("Erasing partition",done,false);
            }
            ct.ThrowIfCancellationRequested();disk.Flush();string expected=Convert.ToHexString(writtenHash.GetHashAndReset()).ToLowerInvariant();
            using var readHash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);disk.Position=offset;done=0;
            while(done<length)
            {
                ct.ThrowIfCancellationRequested();int count=(int)Math.Min(buffer.Length,length-done);await disk.ReadExactlyAsync(buffer.AsMemory(0,count),ct);readHash.AppendData(buffer,0,count);done+=count;Report("Verifying erased partition",done,true);
            }
            ct.ThrowIfCancellationRequested();finalHash=Convert.ToHexString(readHash.GetHashAndReset()).ToLowerInvariant();
            if(finalHash!=expected)throw new IOException("Partition erase read-back verification failed. The selected partition may contain incomplete data.");
            void Report(string phase,long completed,bool verifying)
            {
                if(completed!=length&&clock.ElapsedMilliseconds-lastUpdate<200)return;lastUpdate=clock.ElapsedMilliseconds;
                double percent=100.0*(pass+(verifying?.5:0)+.5*completed/length)/passes;
                progress?.Report(new(phase,Math.Min(99.9,percent),$"Pass {pass+1}/{passes} · {pattern} · {Data.Size(completed)} / {Data.Size(length)} · selected partition only",completed,length));
            }
        }
        ct.ThrowIfCancellationRequested();progress?.Report(new("Partition contents verified",100,$"{passes} pass(es) verified. The partition entry and other partitions were retained.",length,length));
        return new(length,passes,finalHash);
    }
}
