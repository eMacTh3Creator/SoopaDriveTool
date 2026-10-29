using SoopaDriveTool.Core;

partial class Program
{
    static void DiskRefreshChecks()
    {
        var disk=new DiskInfo{Number=2,Name="External drive",UniqueId="USB-ONE",Serial="one",NativeIdentity=new string('a',64),Size=256L*1024*1024,SectorSize=512,Style="GPT",Bus="USB",Partitions=[new(){Number=1,Offset=1024*1024,Size=128L*1024*1024,Letter="E",Label="Data",FileSystem="NTFS",Encryption="None"}]};
        var fresh=disk with{Health="Healthy",Partitions=[disk.Partitions[0] with{Free=64L*1024*1024}]};
        var other=disk with{Number=3,UniqueId="USB-TWO",Serial="two",NativeIdentity=new string('b',64)};
        Check(DiskRefreshPolicy.QueueProblem([fresh,other],[disk])==null,"Unrelated hotplug and free-space updates preserve a reviewed queue");
        Check(ReferenceEquals(DiskRefreshPolicy.RetainSelection([other,fresh],disk),fresh),"Refresh retains the same disk identity and layout, using current metadata");
        Check(DiskRefreshPolicy.QueueProblem([other],[disk])?.Contains("Disk 2",StringComparison.Ordinal)==true,"Missing queued disk produces a disk-specific warning");
        Check(DiskRefreshPolicy.RetainSelection([other],disk)==null,"A missing selection never falls back to a different disk");
        var replacement=other with{Number=disk.Number};
        Check(DiskRefreshPolicy.QueueProblem([replacement],[disk])!=null&&DiskRefreshPolicy.RetainSelection([replacement],disk)==null,"Reusing a disk number cannot retain a replaced drive or its queue");
        var nativeReplacement=disk with{NativeIdentity=new string('c',64)};
        Check(DiskRefreshPolicy.QueueProblem([nativeReplacement],[disk])!=null&&DiskRefreshPolicy.RetainSelection([nativeReplacement],disk)==null,"Native device identity changes invalidate the selection and queue");
        var changedLayout=disk with{Partitions=[disk.Partitions[0] with{Label="Changed"}]};
        Check(DiskRefreshPolicy.QueueProblem([changedLayout],[disk])!=null&&DiskRefreshPolicy.RetainSelection([changedLayout],disk)==null,"External partition changes invalidate the selection and queue");
        foreach(var changed in new[]{disk with{ReadOnly=true},disk with{Offline=true},disk with{IsSystem=true},disk with{Partitions=[disk.Partitions[0] with{IsBoot=true}]}})
            Check(DiskRefreshPolicy.QueueProblem([changed],[disk])!=null&&DiskRefreshPolicy.RetainSelection([changed],disk)==null,"Protection or online state changes invalidate the selection and queue");
        var incomplete=disk with{InventoryWarning="The controller did not return partition details."};
        Check(DiskRefreshPolicy.QueueProblem([incomplete],[disk])!=null&&DiskRefreshPolicy.RetainSelection([incomplete],disk)==null,"New inventory warnings invalidate a reviewed queue and selection");
        Check(DiskRefreshPolicy.QueueProblem([incomplete],[incomplete])!=null,"An unchanged incomplete inventory cannot validate a queue");
        Check(DiskRefreshPolicy.QueueProblem([disk],[incomplete])!=null&&DiskRefreshPolicy.RetainSelection([disk],incomplete)==null,"Restored inventory reliability requires another review");
        Check(ReferenceEquals(DiskRefreshPolicy.RetainSelection([incomplete],incomplete),incomplete),"An unchanged warning may retain a read-only selection");
        Check(DiskRefreshPolicy.QueueProblem([disk,disk],[disk])!=null&&DiskRefreshPolicy.RetainSelection([disk,disk],disk)==null,"Ambiguous duplicate disk numbers do not validate or select a drive");
        Check(DiskRefreshPolicy.QueueProblem([incomplete],[])==null&&DiskRefreshPolicy.RetainSelection([disk],null)==null,"An empty queue and unset selection remain empty");
    }
}
