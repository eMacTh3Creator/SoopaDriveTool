using System.Buffers.Binary;
using System.IO;
using System.Text;
using SoopaDriveTool.Core;
using SoopaDriveTool.Windows;
partial class Program
{
    static void OfflineEncryptionEvidenceChecks()
    {
        var (online,bytes)=EvidenceNtfs();DiskInfo Offline(DiskInfo disk)=>disk with{Offline=true,Partitions=disk.Partitions.Select(p=>p with{Encryption="Unknown",Letter="",FileSystem=""}).ToList()};
        OfflineEncryptionEvidence NewCache()=>new(_=>new MemoryStream(bytes,false));
        void Observe(OfflineEncryptionEvidence cache,DiskInfo disk)=>cache.Apply(disk,cache.BeginInventory());
        DiskInfo Read(OfflineEncryptionEvidence cache,DiskInfo disk)=>cache.Apply(disk,cache.BeginInventory());
        var cold=NewCache();Check(Read(cold,Offline(online)).Partitions[0].Encryption=="Unknown","A cold offline partition never gains an unencrypted classification from its signature alone");
        var cache=NewCache();Observe(cache,online);Check(Read(cache,Offline(online)).Partitions[0].Encryption=="None","Offline encryption retains prior online evidence only when both NTFS boot sectors match");
        bytes[(int)online.Partitions[0].Offset+100]^=1;Check(Read(cache,Offline(online)).Partitions[0].Encryption=="Unknown","Changed primary boot metadata invalidates offline encryption evidence");bytes[(int)online.Partitions[0].Offset+100]^=1;
        Check(Read(cache,Offline(online)).Partitions[0].Encryption=="Unknown","Restoring a changed header does not silently rearm invalidated evidence");
        Observe(cache,online);int backup=(int)(online.Partitions[0].Offset+online.Partitions[0].Size-online.SectorSize);bytes[backup+101]^=1;
        Check(Read(cache,Offline(online)).Partitions[0].Encryption=="Unknown","Changed backup boot metadata invalidates offline encryption evidence");bytes[backup+101]^=1;
        foreach(var changed in new[]{online with{NativeIdentity=new string('b',64)},online with{UniqueId="replacement"},online with{Serial="different"},online with{Size=online.Size+512},online with{SectorSize=4096},online with{Number=9},online with{IsSystem=true},online with{InventoryWarning="Incomplete"},online with{Partitions=[online.Partitions[0] with{Number=2}]},online with{Partitions=[online.Partitions[0] with{Offset=online.Partitions[0].Offset+512}]},online with{Partitions=[online.Partitions[0] with{Size=online.Partitions[0].Size-512}]}})
        {var isolated=NewCache();Observe(isolated,online);Check(Read(isolated,Offline(changed)).Partitions[0].Encryption=="Unknown","Changed native identity, geometry, protection, or partition binding cannot reuse encryption evidence");}
        foreach(string status in new[]{"Unknown","BitLocker"})
        {
            var invalidated=NewCache();Observe(invalidated,online);Observe(invalidated,online with{Partitions=[online.Partitions[0] with{Encryption=status}]});
            Check(Read(invalidated,Offline(online)).Partitions[0].Encryption=="Unknown","Online "+status+" invalidates prior unencrypted evidence");
        }
        var knownEncrypted=online with{Offline=true,Partitions=[online.Partitions[0] with{Encryption="BitLocker"}]};Observe(cache,online);
        Check(Read(cache,knownEncrypted).Partitions[0].Encryption=="BitLocker","Offline evidence never overrides a reported BitLocker state");
        var stale=NewCache();long old=stale.BeginInventory(),fresh=stale.BeginInventory();stale.Apply(online with{Partitions=[online.Partitions[0] with{Encryption="Unknown"}]},fresh);stale.Apply(online,old);
        Check(Read(stale,Offline(online)).Partitions[0].Encryption=="Unknown","An older inventory generation cannot restore evidence after a newer unknown result");
        bool replaceDuringRead=false;OfflineEncryptionEvidence? racing=null;
        racing=new(_=>{if(replaceDuringRead){replaceDuringRead=false;var active=racing!;active.Apply(online with{Partitions=[online.Partitions[0] with{Encryption="BitLocker"}]},active.BeginInventory());}return new MemoryStream(bytes,false);});
        replaceDuringRead=true;Observe(racing,online);Check(Read(racing,Offline(online)).Partitions[0].Encryption=="Unknown","An in-flight online filesystem read cannot overwrite newer BitLocker evidence");
        bool broken=false;var controller=new OfflineEncryptionEvidence(_=>broken?throw new IOException("Controller unavailable"):new MemoryStream(bytes,false));Observe(controller,online);broken=true;
        Check(Read(controller,Offline(online)).Partitions[0].Encryption=="Unknown","A native controller failure keeps offline encryption unknown");broken=false;
        Check(Read(controller,Offline(online)).Partitions[0].Encryption=="Unknown","Controller recovery needs a new confirmed online observation");
        var fve=EvidenceNtfs();Encoding.ASCII.GetBytes("-FVE-FS-").CopyTo(fve.Bytes,(int)fve.Disk.Partitions[0].Offset+3);var fveCache=new OfflineEncryptionEvidence(_=>new MemoryStream(fve.Bytes,false));Observe(fveCache,fve.Disk);
        Check(Read(fveCache,Offline(fve.Disk)).Partitions[0].Encryption=="Unknown","Native BitLocker boot signatures cannot provide plaintext filesystem evidence");
        var unsupported=NewCache();Observe(unsupported,online with{Partitions=[online.Partitions[0] with{FileSystem="ReFS"}]});Check(Read(unsupported,Offline(online)).Partitions[0].Encryption=="Unknown","Unsupported filesystem metadata is not guessed as unencrypted");
        var (fourK,fourKBytes)=EvidenceNtfs(4096);var fourKCache=new OfflineEncryptionEvidence(_=>new MemoryStream(fourKBytes,false));Observe(fourKCache,fourK);
        Check(Read(fourKCache,Offline(fourK)).Partitions[0].Encryption=="None","Aligned 4Kn NTFS boot and backup evidence is supported");
        var (fat,fatBytes)=EvidenceFat32();var fatCache=new OfflineEncryptionEvidence(_=>new MemoryStream(fatBytes,false));Observe(fatCache,fat);
        Check(Read(fatCache,Offline(fat)).Partitions[0].Encryption=="None","FAT32 evidence uses the declared backup BPB instead of user data at the partition end");
        fatBytes[(int)fat.Partitions[0].Offset+6*512+100]^=1;Check(Read(fatCache,Offline(fat)).Partitions[0].Encryption=="Unknown","Changed FAT32 backup BPB invalidates evidence");
        var (exfat,exfatBytes)=EvidenceExFat();var exfatCache=new OfflineEncryptionEvidence(_=>new MemoryStream(exfatBytes,false));Observe(exfatCache,exfat);
        Check(Read(exfatCache,Offline(exfat)).Partitions[0].Encryption=="None","exFAT evidence verifies both complete checksummed boot regions");
        exfatBytes[(int)exfat.Partitions[0].Offset+23*512]^=1;Check(Read(exfatCache,Offline(exfat)).Partitions[0].Encryption=="Unknown","Invalid exFAT backup checksum cannot preserve an unencrypted state");
    }
    static (DiskInfo Disk,byte[] Bytes) EvidenceNtfs(int sector=512)
    {
        const int offset=4096;int length=sector*16;byte[] bytes=new byte[offset+length+4096],boot=new byte[sector];Encoding.ASCII.GetBytes("NTFS    ").CopyTo(boot,3);BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(11),(ushort)sector);boot[13]=1;BinaryPrimitives.WriteUInt64LittleEndian(boot.AsSpan(40),16);BinaryPrimitives.WriteUInt64LittleEndian(boot.AsSpan(48),4);BinaryPrimitives.WriteUInt64LittleEndian(boot.AsSpan(56),8);boot[64]=sector==4096?(byte)244:(byte)246;boot[510]=0x55;boot[511]=0xaa;boot.CopyTo(bytes,offset);boot.CopyTo(bytes,offset+length-sector);return (EvidenceDisk(offset,length,bytes.Length,sector,"NTFS"),bytes);
    }
    static (DiskInfo Disk,byte[] Bytes) EvidenceFat32()
    {
        const int offset=4096,length=64*1024*1024;byte[] bytes=new byte[offset+length+4096],boot=new byte[512];Encoding.ASCII.GetBytes("FAT32   ").CopyTo(boot,82);BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(11),512);boot[13]=1;BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(14),32);boot[16]=2;BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(32),length/512);BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(36),1024);BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(44),2);BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(50),6);boot[510]=0x55;boot[511]=0xaa;boot.CopyTo(bytes,offset);boot.CopyTo(bytes,offset+6*512);return (EvidenceDisk(offset,length,bytes.Length,512,"FAT32"),bytes);
    }
    static (DiskInfo Disk,byte[] Bytes) EvidenceExFat()
    {
        const int offset=4096,length=32*512;byte[] bytes=new byte[offset+length+4096],region=new byte[12*512];Encoding.ASCII.GetBytes("EXFAT   ").CopyTo(region,3);BinaryPrimitives.WriteUInt64LittleEndian(region.AsSpan(64),offset/512);BinaryPrimitives.WriteUInt64LittleEndian(region.AsSpan(72),length/512);BinaryPrimitives.WriteUInt32LittleEndian(region.AsSpan(80),24);BinaryPrimitives.WriteUInt32LittleEndian(region.AsSpan(84),1);BinaryPrimitives.WriteUInt32LittleEndian(region.AsSpan(88),25);BinaryPrimitives.WriteUInt32LittleEndian(region.AsSpan(92),4);BinaryPrimitives.WriteUInt32LittleEndian(region.AsSpan(96),2);region[108]=9;region[109]=0;region[110]=1;region[510]=0x55;region[511]=0xaa;uint checksum=0;for(int i=0;i<11*512;i++){if(i is 106 or 107 or 112)continue;checksum=(checksum>>1)|((checksum&1)<<31);checksum=unchecked(checksum+region[i]);}for(int i=11*512;i<12*512;i+=4)BinaryPrimitives.WriteUInt32LittleEndian(region.AsSpan(i),checksum);region.CopyTo(bytes,offset);region.CopyTo(bytes,offset+12*512);return (EvidenceDisk(offset,length,bytes.Length,512,"exFAT"),bytes);
    }
    static DiskInfo EvidenceDisk(int offset,int length,int total,int sector,string fileSystem)=>new(){Number=2,UniqueId="evidence",Serial="sample",NativeIdentity=new string('a',64),Size=total,SectorSize=sector,Style="GPT",Partitions=[new(){Number=1,Offset=offset,Size=length,FileSystem=fileSystem,Encryption="None",Type="Basic",Letter="E"}]};
}
