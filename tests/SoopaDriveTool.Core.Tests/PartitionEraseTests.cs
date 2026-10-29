using SoopaDriveTool.Core;
using System.Security.Cryptography;
partial class Program
{
    static async Task PartitionEraseChecks()
    {
        const int offset=4096,length=2*1024*1024,tail=8192;
        foreach(var mode in Enum.GetValues<WipeMode>())
        {
            byte[] bytes=Enumerable.Repeat((byte)0xa5,offset+length+tail).ToArray();var records=new List<DriveProgress>();using var disk=new MemoryStream(bytes,true);
            var result=await PartitionErase.EraseAsync(disk,offset,length,4096,mode,new EraseProgress(records.Add));
            Check(bytes.AsSpan(0,offset).ToArray().All(b=>b==0xa5)&&bytes.AsSpan(offset+length).ToArray().All(b=>b==0xa5),$"{mode} partition erase preserves bytes before and after the selected extent");
            Check(result.Bytes==length&&result.Passes==(mode==WipeMode.ThreePass?3:1)&&result.Sha256==Convert.ToHexString(SHA256.HashData(bytes.AsSpan(offset,length))).ToLowerInvariant(),$"{mode} partition erasure verifies the complete selected extent");
            Check(mode==WipeMode.Random?bytes.AsSpan(offset,length).ContainsAnyExcept((byte)0):!bytes.AsSpan(offset,length).ContainsAnyExcept((byte)0),$"{mode} partition erasure uses the selected final pattern");
            Check(records.Count(p=>p.Percent==100)==1&&records.Last().Percent==100&&records.Where(p=>p.Phase.StartsWith("Verifying")).Count()==result.Passes,$"{mode} reports completion only after every pass read-back succeeds");
        }
        foreach(var range in new(long Offset,long Length,int Sector)[]{(-512,512,512),(0,512,512),(513,512,512),(512,513,512),(4096,0,512),(offset+length,tail+512,512),(long.MaxValue,512,512),(4096,4096,0),(4096,4096,2048)})
            await Reject(()=>PartitionErase.EraseAsync(new MemoryStream(new byte[offset+length+tail]),range.Offset,range.Length,range.Sector,WipeMode.Zero),"Partition erase rejects invalid or escaping extent");
        await Reject(()=>PartitionErase.EraseAsync(new MemoryStream(new byte[8192],false),512,512,512,WipeMode.Zero),"Partition erase rejects a read-only stream before writes");
        using(var cancellation=new CancellationTokenSource())
        {
            byte[] bytes=Enumerable.Repeat((byte)0xa5,offset+length+tail).ToArray();var records=new List<DriveProgress>();using var disk=new MemoryStream(bytes,true);
            await Reject(()=>PartitionErase.EraseAsync(disk,offset,length,512,WipeMode.Zero,new EraseProgress(p=>{records.Add(p);if(p.Phase=="Erasing partition")cancellation.Cancel();}),cancellation.Token),"Cancelled partition erase stops before verification completion");
            Check(!records.Any(p=>p.Percent==100)&&bytes.AsSpan(0,offset).ToArray().All(b=>b==0xa5)&&bytes.AsSpan(offset+length).ToArray().All(b=>b==0xa5),"Cancelled partition erase never reports completion or touches adjacent extents");
        }
        var failed=new List<DriveProgress>();using(var disk=new CorruptEraseReadback(new byte[offset+length+tail]))
        {
            await Reject(()=>PartitionErase.EraseAsync(disk,offset,length,512,WipeMode.Zero,new EraseProgress(failed.Add)),"Corrupt partition read-back fails verification");
            Check(!failed.Any(p=>p.Percent==100),"A failed read-back cannot report a completed erase");
        }
        var partition=new PartitionInfo{Number=1,Offset=1024*1024,Size=16*1024*1024,FileSystem="NTFS",Encryption="None",Type="Basic",Letter="E"};
        var data=new DiskInfo{Number=2,UniqueId="erase-fixture",Serial="fixture",NativeIdentity=new string('a',64),Size=64*1024*1024,SectorSize=512,Style="GPT",Offline=true,Partitions=[partition,new(){Number=2,Offset=32*1024*1024,Size=16*1024*1024,FileSystem="NTFS",Encryption="None",Type="Basic"}]};
        var operation=DiskOperation.For(data,OperationKind.WipePartition,partition);OperationPolicy.Validate(operation,data);Check(operation.Destructive&&operation.Description.Contains("Erase partition contents",StringComparison.Ordinal),"Partition erasure has a destructive, partition-specific queued description");
        var preview=QueuePlanner.Project([data],[operation]).Single();Check(preview.Style=="GPT"&&preview.Partitions.Count==2&&preview.Partitions[0].Size==partition.Size&&preview.Partitions[0].FileSystem==""&&preview.Partitions[1]==data.Partitions[1],"Erase preview retains the partition entry and unrelated partition");
        RejectErasePolicy(()=>QueuePlanner.Project([data],[operation,DiskOperation.For(data,OperationKind.Online)]),"Partition erase must be the final operation using its disk");
        foreach(var changed in new[]{data with{Offline=false},data with{ReadOnly=true},data with{IsBoot=true},data with{InventoryWarning="Incomplete"},data with{Partitions=[partition with{Encryption="Unknown"}]},data with{Partitions=[partition with{Type="Recovery"}]},data with{Partitions=[partition with{Offset=513}]},data with{Partitions=[partition with{Size=long.MaxValue}]}})
            RejectErasePolicy(()=>OperationPolicy.Validate(DiskOperation.For(changed,OperationKind.WipePartition,changed.Partitions[0]),changed),"Partition erase rejects unsafe protection, encryption, availability, or bounds");
        RejectErasePolicy(()=>OperationPolicy.Validate(operation with{WipeMode=(WipeMode)999},data),"Partition erasure rejects an unknown erase method");
        RejectErasePolicy(()=>OperationPolicy.Validate(operation,data with{UniqueId="replacement"}),"Partition erasure rejects stale disk binding");
        RejectErasePolicy(()=>OperationPolicy.Validate(DiskOperation.For(data,OperationKind.WipePartition),data),"Partition erasure requires a selected partition");
        var after=data with{Partitions=[partition with{FileSystem="",Label="",Letter="",Encryption="Unknown"},data.Partitions[1]]};QueuePlanner.VerifyTransition(data,after,operation);Check(true,"Erase transition allows removed filesystem metadata while retaining offline geometry");
        RejectErasePolicy(()=>QueuePlanner.VerifyTransition(data,after with{Partitions=after.Partitions.Skip(1).ToList()},operation),"Erase transition rejects a removed partition entry");
    }
    static void RejectErasePolicy(Action action,string description){try{action();}catch{Check(true,description);return;}throw new Exception("FAIL: accepted "+description);}
    sealed class EraseProgress(Action<DriveProgress> report):IProgress<DriveProgress>{public void Report(DriveProgress value)=>report(value);}
    sealed class CorruptEraseReadback(byte[] data):MemoryStream(data,true)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default){int count=await base.ReadAsync(buffer,ct);if(count>0)buffer.Span[0]^=1;return count;}
    }
}
