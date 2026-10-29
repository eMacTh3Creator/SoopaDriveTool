using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;

// Windows removes volume paths while a disk is offline. Reuse a known online
// encryption result only while both native filesystem boot regions still match.
// Evidence is process-local and never accepts a cold, unknown offline volume.
internal sealed class OfflineEncryptionEvidence(Func<DiskInfo,Stream> open)
{
    sealed record Proof(string FileSystem,string Primary,string Backup);
    sealed record State(long Generation,string Binding,IReadOnlyDictionary<string,Proof> Partitions);
    readonly ConcurrentDictionary<int,State> states=new();long newest;
    public long BeginInventory()=>Interlocked.Increment(ref newest);
    static string Binding(DiskInfo d)=>$"{d.Number}|{d.UniqueId}|{d.Serial}|{d.NativeIdentity}|{d.Size}|{d.SectorSize}";
    static string Key(PartitionInfo p)=>$"{p.Number}|{p.Offset}|{p.Size}";
    static bool Eligible(DiskInfo d,PartitionInfo p)=>!d.Protected&&!p.Protected&&d.InventoryWarning.Length==0&&d.NativeIdentity.Length==64&&d.Size>0&&d.SectorSize is 512 or 4096;
    public DiskInfo Apply(DiskInfo disk,long generation)
    {
        if(generation!=Volatile.Read(ref newest))return disk;
        string binding=Binding(disk);var proofs=new Dictionary<string,Proof>();var result=disk.Partitions.ToList();Stream? stream=null;
        try
        {
            if(!disk.Offline)
            {
                foreach(var part in disk.Partitions)
                {
                    if(!Eligible(disk,part)||part.Encryption!="None"||part.FileSystem is not ("NTFS" or "FAT32" or "exFAT"))continue;
                    try{stream??=open(disk);var proof=ReadProof(stream,part,disk.SectorSize,part.FileSystem);if(proof!=null)proofs[Key(part)]=proof;}catch(Exception ex)when(ProofFailure(ex)){}
                }
            }
            else if(states.TryGetValue(disk.Number,out var previous)&&previous.Binding==binding)
            {
                for(int i=0;i<result.Count;i++)
                {
                    var part=result[i];if(!Eligible(disk,part)||part.Encryption is not ("Unknown" or "None")||!previous.Partitions.TryGetValue(Key(part),out var expected))continue;
                    try
                    {
                        stream??=open(disk);var actual=ReadProof(stream,part,disk.SectorSize,expected.FileSystem);
                        if(actual!=expected)continue;
                        proofs[Key(part)]=expected;if(part.Encryption=="Unknown")result[i]=part with{Encryption="None"};
                    }
                    catch(Exception ex)when(ProofFailure(ex)){}
                }
            }
        }
        finally{stream?.Dispose();}
        var next=new State(generation,binding,proofs);if(!Commit(disk.Number,next))return disk;
        return disk with{Partitions=result};
    }
    bool Commit(int number,State next)
    {
        if(next.Generation!=Volatile.Read(ref newest))return false;
        var stored=states.AddOrUpdate(number,_=>next.Generation==Volatile.Read(ref newest)?next:new State(next.Generation,next.Binding,new Dictionary<string,Proof>()),(_,old)=>next.Generation==Volatile.Read(ref newest)&&old.Generation<=next.Generation?next:old);
        return ReferenceEquals(stored,next)&&next.Generation==Volatile.Read(ref newest);
    }
    static bool ProofFailure(Exception ex)=>ex is IOException or Win32Exception or UnauthorizedAccessException or ArgumentException or OverflowException;
    static Proof? ReadProof(Stream stream,PartitionInfo part,int sector,string expected)
    {
        PartitionErase.ValidateRange(stream.Length,part.Offset,part.Size,sector);
        byte[] primary=DiskTables.ReadAt(stream,part.Offset,sector);if(primary[510]!=0x55||primary[511]!=0xaa)return null;
        string oem=Encoding.ASCII.GetString(primary,3,8);if(oem=="-FVE-FS-"||oem.Contains("FVE",StringComparison.OrdinalIgnoreCase))return null;
        int cluster=primary[13];bool PowerOfTwo(int value)=>value>0&&(value&(value-1))==0;
        string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
        if(expected=="NTFS")
        {
            if(oem!="NTFS    "||BinaryPrimitives.ReadUInt16LittleEndian(primary.AsSpan(11))!=sector||!PowerOfTwo(cluster)||cluster>128)return null;
            ulong count=BinaryPrimitives.ReadUInt64LittleEndian(primary.AsSpan(40)),limit=(ulong)(part.Size/sector);
            if(count==0||count>limit||limit-count>1)return null;
            ulong mft=BinaryPrimitives.ReadUInt64LittleEndian(primary.AsSpan(48)),mirror=BinaryPrimitives.ReadUInt64LittleEndian(primary.AsSpan(56));
            int recordCode=(sbyte)primary[64];if(recordCode==0||recordCode< -16||recordCode>128)return null;long recordBytes=recordCode<0?1L<< -recordCode:(long)recordCode*sector*cluster;
            if(recordBytes<sector||recordBytes>65536||mft==0||mirror==0||mft>=count/(uint)cluster||mirror>=count/(uint)cluster||mft*(ulong)cluster+(ulong)(recordBytes/sector)>count||mirror*(ulong)cluster+(ulong)(recordBytes/sector)>count)return null;
            byte[] backup=DiskTables.ReadAt(stream,part.Offset+part.Size-sector,sector);if(!primary.AsSpan().SequenceEqual(backup))return null;
            return new(expected,Hash(primary),Hash(backup));
        }
        if(expected=="FAT32")
        {
            if(Encoding.ASCII.GetString(primary,82,8)!="FAT32   "||BinaryPrimitives.ReadUInt16LittleEndian(primary.AsSpan(11))!=sector||!PowerOfTwo(cluster)||cluster>128)return null;
            uint total=BinaryPrimitives.ReadUInt32LittleEndian(primary.AsSpan(32)),fat=BinaryPrimitives.ReadUInt32LittleEndian(primary.AsSpan(36));int reserved=BinaryPrimitives.ReadUInt16LittleEndian(primary.AsSpan(14)),copies=primary[16],backupIndex=BinaryPrimitives.ReadUInt16LittleEndian(primary.AsSpan(50));
            if(total==0||total>(ulong)(part.Size/sector)||fat==0||copies is not (1 or 2)||reserved<2||backupIndex<1||backupIndex>=reserved||(ulong)reserved+(ulong)copies*fat>=total)return null;
            if(BinaryPrimitives.ReadUInt16LittleEndian(primary.AsSpan(17))!=0||BinaryPrimitives.ReadUInt16LittleEndian(primary.AsSpan(22))!=0)return null;
            ulong clusterCount=((ulong)total-(uint)reserved-(ulong)copies*fat)/(uint)cluster;uint root=BinaryPrimitives.ReadUInt32LittleEndian(primary.AsSpan(44));
            if(clusterCount<65525||clusterCount>0x0ffffff5||root<2||root>clusterCount+1||(ulong)fat*(uint)sector/4<clusterCount+2)return null;
            byte[] backup=DiskTables.ReadAt(stream,part.Offset+(long)backupIndex*sector,sector);if(!primary.AsSpan().SequenceEqual(backup))return null;
            return new(expected,Hash(primary),Hash(backup));
        }
        if(expected=="exFAT")
        {
            if(oem!="EXFAT   "||primary[108] is <9 or >12||(1<<primary[108])!=sector||primary[109]>25-primary[108]||primary[110] is not (1 or 2)||part.Size<24L*sector)return null;
            ulong origin=BinaryPrimitives.ReadUInt64LittleEndian(primary.AsSpan(64)),total=BinaryPrimitives.ReadUInt64LittleEndian(primary.AsSpan(72));uint fat=BinaryPrimitives.ReadUInt32LittleEndian(primary.AsSpan(80)),fatLength=BinaryPrimitives.ReadUInt32LittleEndian(primary.AsSpan(84)),heap=BinaryPrimitives.ReadUInt32LittleEndian(primary.AsSpan(88)),clusters=BinaryPrimitives.ReadUInt32LittleEndian(primary.AsSpan(92)),root=BinaryPrimitives.ReadUInt32LittleEndian(primary.AsSpan(96));
            if((ulong)fatLength*(uint)sector/4<(ulong)clusters+2||origin!=(ulong)(part.Offset/sector)||total>(ulong)(part.Size/sector)||total<24||fat<24||fatLength==0||heap<fat+(ulong)fatLength*primary[110]||clusters==0||root<2||root>(ulong)clusters+1||heap+(ulong)clusters*(1UL<<primary[109])>total)return null;
            byte[] main=DiskTables.ReadAt(stream,part.Offset,12*sector),backup=DiskTables.ReadAt(stream,part.Offset+12L*sector,12*sector);
            if(!ExFatChecksum(main,sector)||!ExFatChecksum(backup,sector)||!main.AsSpan(3,103).SequenceEqual(backup.AsSpan(3,103))||!main.AsSpan(108,4).SequenceEqual(backup.AsSpan(108,4)))return null;
            return new(expected,Hash(main),Hash(backup));
        }
        return null;
    }
    static bool ExFatChecksum(byte[] region,int sector)
    {
        uint sum=0;for(int i=0;i<11*sector;i++){if(i is 106 or 107 or 112)continue;sum=(sum>>1)|((sum&1)<<31);sum=unchecked(sum+region[i]);}
        for(int i=11*sector;i<12*sector;i+=4)if(BinaryPrimitives.ReadUInt32LittleEndian(region.AsSpan(i))!=sum)return false;return true;
    }
}
