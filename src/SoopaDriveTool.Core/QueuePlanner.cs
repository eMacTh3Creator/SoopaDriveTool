namespace SoopaDriveTool.Core;
public static class QueuePlanner
{
    public const long MiB=1024*1024;
    public static bool Opaque(DiskOperation o)=>o.Kind is OperationKind.RestoreImage or OperationKind.CloneDisk or OperationKind.MovePartition or OperationKind.ConvertTable or OperationKind.SetPartitionKind or OperationKind.SplitPartition or OperationKind.MergePartition or OperationKind.ChangeFileSystem or OperationKind.ChangeClusterSize or OperationKind.RedistributeSpace or OperationKind.ConvertDynamic;
    public static List<DiskInfo> Project(IEnumerable<DiskInfo> initial,IReadOnlyList<DiskOperation> operations)
    {
        var result=initial.ToDictionary(d=>d.Number);var closed=new HashSet<int>();
        for(int index=0;index<operations.Count;index++)
        {
            var op=operations[index];
            if(closed.Contains(op.DiskNumber)||op.TargetDiskNumber>=0&&closed.Contains(op.TargetDiskNumber))throw new InvalidOperationException("Cloning/restoring must be the final operation using its disks. Apply before planning further changes.");
            if(!result.TryGetValue(op.DiskNumber,out var d))throw new InvalidOperationException("A queued disk is missing.");
            var target=result.GetValueOrDefault(op.TargetDiskNumber);
            var bound=op with{Identity=d.Identity,Layout=d.Layout,TargetIdentity=target?.Identity??"",TargetLayout=target?.Layout??""};
            OperationPolicy.Validate(bound,d,target);
            result[d.Number]=ApplyProjection(d,op,index);
            if(Opaque(op)){closed.Add(d.Number);if(target!=null)closed.Add(target.Number);}
        }
        return result.Values.OrderBy(d=>d.Number).ToList();
    }
    public static long Allocate(DiskInfo d,long size)
    {
        long cursor=d.Style=="GPT"&&d.Partitions.Count==0?16*MiB:MiB;long limit=d.Size-(d.Style=="GPT"?MiB:0);
        foreach(var p in d.Partitions.OrderBy(p=>p.Offset))
        {cursor=Align(cursor);if(p.Offset-cursor>=size)return cursor;cursor=Math.Max(cursor,checked(p.Offset+p.Size));}
        cursor=Align(cursor);if(cursor>limit-size)throw new InvalidOperationException("No contiguous free region fits this partition in the planned layout.");return cursor;
    }
    public static long Align(long n)=>checked((n+MiB-1)/MiB*MiB);
    static PartitionInfo Reserved(DiskInfo d){long start=(2+(128*128+d.SectorSize-1)/d.SectorSize)*d.SectorSize;return new(){Number=1,Offset=start,Size=16*MiB-start,Type="Reserved",Encryption="None"};}
    public static int VirtualNumber(int operationIndex)=>-operationIndex-1;
    static DiskInfo ApplyProjection(DiskInfo d,DiskOperation o,int index)
    {
        var parts=d.Partitions.ToList();var p=parts.SingleOrDefault(p=>p.Number==o.PartitionNumber);
        switch(o.Kind)
        {
            case OperationKind.InitializeGpt:return d with{Style="GPT",Partitions=[Reserved(d)]};
            case OperationKind.InitializeMbr:return d with{Style="MBR",Partitions=[]};
            case OperationKind.QuickWipeDisk:case OperationKind.WipeDisk:return d with{Style="RAW",Partitions=[]};
            case OperationKind.Offline:return d with{Offline=true};
            case OperationKind.Online:return d with{Offline=false};
            case OperationKind.ReadOnly:return d with{ReadOnly=true};
            case OperationKind.ReadWrite:return d with{ReadOnly=false};
            case OperationKind.CreatePartition:
                var offset=Allocate(d,o.Bytes);if(d.Style=="GPT"&&parts.Count==0)parts.Add(Reserved(d));if(o.Offset!=0&&o.Offset!=offset)throw new InvalidOperationException("Planned allocation changed.");
                parts.Add(new(){Number=VirtualNumber(index),Offset=offset,Size=o.Bytes,Label=o.Value,FileSystem=o.FileSystem,Free=o.Bytes,Type="Basic",Encryption="None"});break;
            case OperationKind.DeletePartition:parts.Remove(p!);break;
            case OperationKind.SetLabel:Replace(p! with{Label=o.Value});break;
            case OperationKind.SetLetter:
                if(parts.Any(p=>p.Number!=o.PartitionNumber&&p.Letter==o.Value))throw new InvalidOperationException("The planned drive letter is already used on this disk.");
                Replace(p! with{Letter=o.Value});break;
            case OperationKind.RemoveLetter:Replace(p! with{Letter=""});break;
            case OperationKind.FormatPartition:Replace(p! with{Label=o.Value,FileSystem=o.FileSystem,Free=p.Size});break;
            case OperationKind.ResizePartition:
                if(o.Bytes>p!.Size&&parts.Any(x=>x.Number!=p.Number&&x.Offset>=p.Offset&&x.Offset<p.Offset+o.Bytes)||p.Offset+o.Bytes>d.Size-MiB)throw new InvalidOperationException("Resize overlaps the next partition or the disk boundary.");
                Replace(p with{Size=o.Bytes});break;
        }
        return d with{Partitions=parts.OrderBy(p=>p.Offset).ToList()};
        void Replace(PartitionInfo replacement){parts.Remove(p!);parts.Add(replacement);}
    }
    public static void AssertUnchanged(DiskInfo expected,DiskInfo actual)
    {if(actual.InventoryWarning.Length>0||expected.InventoryWarning.Length>0||expected.StableIdentity!=actual.StableIdentity||expected.NativeIdentity.Length>0&&expected.NativeIdentity!=actual.NativeIdentity||expected.Layout!=actual.Layout||expected.Protected!=actual.Protected)throw new InvalidOperationException("Disk identity/layout changed outside the verified chain. The remaining steps were stopped.");}
    public static PartitionInfo? VerifyTransition(DiskInfo before,DiskInfo after,DiskOperation op)
    {
        if(op.Kind==OperationKind.ConvertDynamic)
        {
            if(before.NativeIdentity.Length!=64||before.NativeIdentity!=after.NativeIdentity||before.Size!=after.Size||before.SectorSize!=after.SectorSize||after.Protected||op.Value=="Dynamic"&&after.Style!="Dynamic"||op.Value=="Basic"&&!after.Offline)throw new InvalidOperationException("Dynamic conversion device identity or resulting state could not be verified.");return null;
        }
        if(before.Identity!=after.Identity||after.Protected&&!(op.Kind==OperationKind.CloneDisk&&op.CloneMode==CloneMode.WindowsLiveUefi&&before.Protected))throw new InvalidOperationException("Disk identity or protection changed during the operation.");
        bool Same(PartitionInfo a,PartitionInfo b)=>a.Number==b.Number&&a.Offset==b.Offset&&a.Size==b.Size&&a.Type==b.Type&&(before.Offline||after.Offline||a.Letter==b.Letter&&a.Label==b.Label&&a.FileSystem==b.FileSystem);
        var partition=before.Partitions.SingleOrDefault(p=>p.Number==op.PartitionNumber);PartitionInfo? created=null;
        var unaffected=before.Partitions.Where(p=>p.Number!=op.PartitionNumber||op.PartitionNumber==0).ToList();
        if(op.Kind is OperationKind.QuickWipeDisk or OperationKind.WipeDisk or OperationKind.InitializeGpt or OperationKind.InitializeMbr or OperationKind.RestoreImage or OperationKind.CloneDisk or OperationKind.ConvertTable or OperationKind.SetPartitionKind or OperationKind.MergePartition or OperationKind.RedistributeSpace or OperationKind.ConvertDynamic)unaffected=[];
        foreach(var p in unaffected)if(!after.Partitions.Any(a=>Same(a,p)))throw new InvalidOperationException("An unrelated partition changed; the remaining queue was stopped.");
        switch(op.Kind)
        {
            case OperationKind.MovePartition:if(!after.Partitions.Any(p=>p.Offset==op.Offset&&p.Size==partition!.Size))throw new InvalidOperationException("Moved partition location was not verified.");break;
            case OperationKind.ConvertTable:if(after.Style!=op.Value)throw new InvalidOperationException("Converted partition table was not verified.");break;
            case OperationKind.SplitPartition:if(!after.Partitions.Any(p=>p.Offset==partition!.Offset&&p.Size==op.Bytes)||after.Partitions.Count!=before.Partitions.Count+1)throw new InvalidOperationException("Partition split was not verified.");break;
            case OperationKind.ChangeFileSystem:if(!after.Partitions.Any(p=>p.Offset==partition!.Offset&&p.FileSystem==op.FileSystem))throw new InvalidOperationException("Filesystem conversion was not verified.");break;
            case OperationKind.MergePartition:var merged=before.Partitions.Single(p=>p.Number==op.TargetPartitionNumber);if(!after.Partitions.Any(p=>p.Offset==partition!.Offset&&p.Size==partition.Size+merged.Size)||after.Partitions.Any(p=>p.Offset==merged.Offset))throw new InvalidOperationException("Partition merge was not verified.");break;
            case OperationKind.CreatePartition:
                created=after.Partitions.SingleOrDefault(p=>!before.Partitions.Any(b=>b.Number==p.Number)&&p.Offset==op.Offset&&p.Size==op.Bytes&&p.FileSystem==op.FileSystem&&p.Label==op.Value)??throw new InvalidOperationException("Created partition did not match the reviewed plan.");
                if(after.Partitions.Count!=before.Partitions.Count+1&&!(before.Style=="GPT"&&before.Partitions.Count==0&&after.Partitions.Count==2&&after.Partitions.Any(p=>p.Protected&&p.Offset+p.Size==16*MiB)))throw new InvalidOperationException("Unexpected partition count after creation.");break;
            case OperationKind.DeletePartition:if(after.Partitions.Any(p=>p.Number==op.PartitionNumber)||after.Partitions.Count!=before.Partitions.Count-1)throw new InvalidOperationException("Partition deletion was not verified.");break;
            case OperationKind.SetLabel:if(!after.Partitions.Any(p=>p.Number==op.PartitionNumber&&p.Label==op.Value&&p.Offset==partition!.Offset&&p.Size==partition.Size))throw new InvalidOperationException("Volume rename was not verified.");break;
            case OperationKind.SetLetter:if(!after.Partitions.Any(p=>p.Number==op.PartitionNumber&&p.Letter==op.Value))throw new InvalidOperationException("Drive-letter change was not verified.");break;
            case OperationKind.RemoveLetter:if(!after.Partitions.Any(p=>p.Number==op.PartitionNumber&&p.Letter==""))throw new InvalidOperationException("Drive-letter removal was not verified.");break;
            case OperationKind.ResizePartition:if(!after.Partitions.Any(p=>p.Number==op.PartitionNumber&&p.Size==op.Bytes&&p.Offset==partition!.Offset))throw new InvalidOperationException("Partition resize was not verified.");break;
            case OperationKind.FormatPartition:if(!after.Partitions.Any(p=>p.Number==op.PartitionNumber&&p.FileSystem==op.FileSystem&&p.Label==op.Value))throw new InvalidOperationException("Formatting was not verified.");break;
            case OperationKind.InitializeGpt:if(after.Style!="GPT"||after.Partitions.Count!=1||!after.Partitions.Single().Protected)throw new InvalidOperationException("GPT/MSR initialization was not verified.");break;
            case OperationKind.InitializeMbr:if(after.Style!="MBR"||after.Partitions.Count!=0)throw new InvalidOperationException("MBR initialization was not verified.");break;
            case OperationKind.QuickWipeDisk:case OperationKind.WipeDisk:if(after.Style!="RAW"||after.Partitions.Count!=0)throw new InvalidOperationException("Cleared disk metadata was not verified.");break;
            case OperationKind.Offline:if(!after.Offline)throw new InvalidOperationException("Disk did not go offline.");break;
            case OperationKind.Online:if(after.Offline)throw new InvalidOperationException("Disk did not come online.");break;
            case OperationKind.ReadOnly:if(!after.ReadOnly)throw new InvalidOperationException("Read-only state was not verified.");break;
            case OperationKind.ReadWrite:if(after.ReadOnly)throw new InvalidOperationException("Writable state was not verified.");break;
        }
        return created;
    }
}
