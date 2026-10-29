using System.Security.Cryptography;
using System.Text.Json;
namespace SoopaDriveTool.Core;
public record SectorPatch(long Offset,byte[] Before,byte[] After);
public record MoveCheckpoint(int Version,string Binding,long DiskBytes,int SectorSize,int Partition,long Source,long Target,long Length,string Hash,long Copied,string State,List<SectorPatch> Patches,bool Pending=false,int PendingBytes=0,string PendingHash="",DiskTable? OriginalTable=null);
// The journal must live on another physical disk. A pending source chunk is
// durably saved before any overlapping write, so a crash can replay it safely.
public static class ResumableMove
{
    public const int BufferBytes=4*1024*1024;
    public static async Task MoveAsync(Stream disk,DiskTable table,int partition,long target,string binding,string journal,IProgress<DriveProgress>? progress=null,CancellationToken ct=default,Action<string>? fault=null)
    {
        if(!disk.CanRead||!disk.CanWrite||!disk.CanSeek)throw new IOException("A retained writable disk handle is required.");
        journal=Path.GetFullPath(journal);BackupEngine.RejectExistingAncestorLinks(journal);Directory.CreateDirectory(Path.GetDirectoryName(journal)!);
        var destination=DiskTables.Move(table,partition,target);var p=table.Partitions.Single(p=>p.Index==partition);if(p.Offset==target)throw new IOException("Choose a different partition offset.");
        MoveCheckpoint state;
        if(File.Exists(journal))
        {
            if(new FileInfo(journal).Length>16*1024*1024)throw new IOException("Oversized move journal.");state=JsonSerializer.Deserialize<MoveCheckpoint>(await File.ReadAllTextAsync(journal,ct),Data.Json)??throw new IOException("Invalid journal.");
            if(state.Version!=1||state.Binding!=binding||state.DiskBytes!=disk.Length||state.SectorSize!=table.SectorSize||state.Partition!=partition||state.Source!=p.Offset||state.Target!=target||state.Length!=p.Length||state.Copied<0||state.Copied>p.Length||state.State is not ("Copying" or "Verify" or "Commit" or "Completed"))throw new IOException("The move journal does not match this disk and plan.");
        }
        else
        {
            string hash=await HashAsync(disk,p.Offset,p.Length,progress,"Hashing source before move",ct);
            var bootPatches=BootPatches(disk,target,p.Length,table.SectorSize,p.Offset);
            state=new(1,binding,disk.Length,table.SectorSize,partition,p.Offset,target,p.Length,hash,0,"Copying",bootPatches,OriginalTable:table);await Save(journal,state);fault?.Invoke("created");
        }
        if(state.State=="Completed")return;
        byte[] buffer=new byte[BufferBytes];string spool=journal+".chunk";
        while(state.State=="Copying"&&state.Copied<state.Length)
        {
            ct.ThrowIfCancellationRequested();int n=(int)Math.Min(buffer.Length,state.Length-state.Copied);long relative=state.Target>state.Source?state.Length-state.Copied-n:state.Copied;
            if(state.Pending)
            {
                BackupEngine.RejectExistingAncestorLinks(spool);if(state.PendingBytes!=n||new FileInfo(spool).Length!=n)throw new IOException("The saved pending move chunk is missing or has the wrong length. Keep this journal for recovery.");
                await using var pending=File.OpenRead(spool);await pending.ReadExactlyAsync(buffer.AsMemory(0,n),ct);if(Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0,n)))!=state.PendingHash)throw new IOException("The pending move chunk failed its checksum. No further disk writes were made.");
            }
            else
            {
                disk.Position=state.Source+relative;await disk.ReadExactlyAsync(buffer.AsMemory(0,n),ct);
                await using(var pending=new FileStream(spool,FileMode.Create,FileAccess.Write,FileShare.None,65536,FileOptions.WriteThrough)){await pending.WriteAsync(buffer.AsMemory(0,n),ct);pending.Flush(true);}
                state=state with{Pending=true,PendingBytes=n,PendingHash=Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0,n)))};await Save(journal,state);fault?.Invoke("pending");
            }
            // Cancellation is deliberately deferred until the chunk and checkpoint
            // are committed; replay never rereads a possibly overwritten source.
            disk.Position=state.Target+relative;await disk.WriteAsync(buffer.AsMemory(0,n));Flush(disk);fault?.Invoke("written");
            state=state with{Copied=state.Copied+n,Pending=false,PendingBytes=0,PendingHash=""};await Save(journal,state);fault?.Invoke("checkpoint");File.Delete(spool);
            progress?.Report(new("Moving partition data",70.0*state.Copied/state.Length,$"{Data.Size(state.Copied)} / {Data.Size(state.Length)} · recoverable checkpoint saved",state.Copied,state.Length));
        }
        if(state.State=="Copying"){state=state with{State="Verify"};await Save(journal,state);}
        if(state.State=="Verify")
        {
            if(await HashAsync(disk,state.Target,state.Length,progress,"Verifying moved partition",ct)!=state.Hash)throw new IOException("Moved data failed its full SHA-256 verification. The table was not changed; keep the recovery journal.");
            var recorder=new PatchStream(disk);DiskTables.Write(recorder,destination);var patches=recorder.Patches;
            patches.AddRange(state.Patches);
            state=state with{State="Commit",Patches=patches};await Save(journal,state);fault?.Invoke("prepared");
        }
        if(state.State=="Commit")
        {
            progress?.Report(new("Committing verified layout",95,"Updating redundant table and filesystem boot metadata from the durable recovery plan."));
            foreach(var patch in state.Patches)
            {
                if(patch.Before.Length!=patch.After.Length||patch.Before.Length>4*1024*1024||patch.Offset<0||patch.Offset>disk.Length-patch.After.Length)throw new IOException("Invalid journal patch.");
                var actual=DiskTables.ReadAt(disk,patch.Offset,patch.Before.Length);
                if(!actual.SequenceEqual(patch.Before)&&!actual.SequenceEqual(patch.After))throw new IOException("A table/boot sector changed outside the saved move. Stop and inspect the journal.");
                DiskTables.WriteAt(disk,patch.Offset,patch.After);Flush(disk);fault?.Invoke("metadata");
            }
            var actualTable=DiskTables.Read(disk,table.SectorSize);if(!actualTable.Partitions.Any(x=>x.Offset==target&&x.Length==p.Length))throw new IOException("The committed partition layout could not be verified.");
            state=state with{State="Completed"};await Save(journal,state);File.Delete(spool);progress?.Report(new("Move verified",100,"Full data hash, boot metadata and final partition table verified."));
        }
    }
    static List<SectorPatch> BootPatches(Stream disk,long target,long length,int sector,long readBase)
    {
        var result=new List<SectorPatch>();byte[] boot=DiskTables.ReadAt(disk,readBase,sector);string oem=System.Text.Encoding.ASCII.GetString(boot,3,8);
        void Hidden(long relative){byte[] old=DiskTables.ReadAt(disk,readBase+relative,sector),next=(byte[])old.Clone();if(target/sector>uint.MaxValue)throw new IOException("Filesystem hidden-sector field cannot represent the destination.");System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(next.AsSpan(28),(uint)(target/sector));result.Add(new(target+relative,old,next));}
        if(oem=="NTFS    "){Hidden(0);Hidden(length-sector);}
        else if(System.Text.Encoding.ASCII.GetString(boot,82,8)=="FAT32   "){Hidden(0);int backup=BitConverter.ToUInt16(boot,50);if(backup>0&&backup<length/sector)Hidden((long)backup*sector);}
        else if(oem=="EXFAT   ")
        {
            foreach(int region in new[]{0,12})
            {
                byte[] old=DiskTables.ReadAt(disk,readBase+(long)region*sector,12*sector),next=(byte[])old.Clone();System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(next.AsSpan(64),(ulong)(target/sector));uint sum=0;
                for(int i=0;i<11*sector;i++){if(i is 106 or 107 or 112)continue;sum=((sum&1)!=0?0x80000000u:0)+(sum>>1)+next[i];}
                for(int i=11*sector;i<12*sector;i+=4)System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(next.AsSpan(i),sum);result.Add(new(target+(long)region*sector,old,next));
            }
        }
        else throw new IOException("Move requires a recognized NTFS, FAT32 or exFAT boot sector. Data is copied but the original table remains; keep the journal.");return result;
    }
    static async Task<string> HashAsync(Stream disk,long start,long length,IProgress<DriveProgress>? progress,string phase,CancellationToken ct)
    {using var h=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);byte[] b=new byte[BufferBytes];long done=0;disk.Position=start;while(done<length){int n=(int)Math.Min(b.Length,length-done);await disk.ReadExactlyAsync(b.AsMemory(0,n),ct);h.AppendData(b,0,n);done+=n;progress?.Report(new(phase,phase.StartsWith("Verifying")?70+24.0*done/length:0,Data.Size(done)+" / "+Data.Size(length),done,length));}return Convert.ToHexString(h.GetHashAndReset());}
    static async Task Save(string path,MoveCheckpoint state){string tmp=path+".tmp";await using(var f=new FileStream(tmp,FileMode.Create,FileAccess.Write,FileShare.None,65536,FileOptions.WriteThrough)){await JsonSerializer.SerializeAsync(f,state,Data.Json);f.Flush(true);}File.Move(tmp,path,true);}
    static void Flush(Stream s){if(s is FileStream f)f.Flush(true);else s.Flush();}
    sealed class PatchStream(Stream source):Stream
    {
        public List<SectorPatch> Patches{get;}=[];long position;
        public override bool CanRead=>true;public override bool CanWrite=>true;public override bool CanSeek=>true;public override long Length=>source.Length;public override long Position{get=>position;set=>position=value;}
        public override int Read(byte[] b,int o,int n){source.Position=position;int got=source.Read(b,o,n);foreach(var p in Patches){long lo=Math.Max(position,p.Offset),hi=Math.Min(position+got,p.Offset+p.After.Length);if(hi>lo)p.After.AsSpan((int)(lo-p.Offset),(int)(hi-lo)).CopyTo(b.AsSpan(o+(int)(lo-position)));}position+=got;return got;}
        public override void Write(byte[] b,int o,int n){byte[] before=new byte[n];long here=position;ReadExactly(before);Patches.Add(new(here,before,b.AsSpan(o,n).ToArray()));position=here+n;}
        public override long Seek(long o,SeekOrigin so)=>Position=so==SeekOrigin.Begin?o:so==SeekOrigin.Current?position+o:Length+o;public override void SetLength(long n)=>throw new NotSupportedException();public override void Flush(){}
    }
}
