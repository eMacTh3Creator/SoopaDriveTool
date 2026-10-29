using System.Globalization;
using System.Windows.Controls;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public partial class MainWindow
{
    void AdvancedPartitionActions(ComboBox disk,ComboBox partition)
    {
        var actions=new WrapPanel();
        void Add(string label,OperationKind kind)=>actions.Children.Add(Btn(label,()=>PrepareBatch(()=>
        {
            if(disk.SelectedItem is not DiskInfo d)throw new IOException("Select a data disk.");var p=partition.SelectedItem as PartitionInfo;if(kind is not (OperationKind.ConvertTable or OperationKind.ConvertDynamic)&&p==null)throw new IOException("Select a data partition.");
            var op=DiskOperation.For(d,kind,kind is OperationKind.ConvertTable or OperationKind.ConvertDynamic?null:p);
            string? folder=PickFolder("Choose recovery checkpoint storage on another physical disk");if(folder==null)return;op=op with{Workspace=folder};
            if(kind==OperationKind.ConvertDynamic){if(!StorageService.IsAdmin)throw new IOException("Restart as administrator and refresh before preparing a dynamic-disk conversion.");var style=Prompt("Basic or dynamic disk","Single-disk simple NTFS volumes can be preserved as basic GPT. Multi-disk dynamic volumes require migration to another disk. Dynamic-to-basic keeps the disk offline and requires a restart before mounting.",d.Style=="Dynamic"?"Basic":"Dynamic");if(style==null)return;op=op with{Value=style};}
            if(kind==OperationKind.MovePartition){var n=Prompt("Move partition","New starting offset in MiB. Free adjacent space may overlap this partition; the engine copies in the safe direction.",(p!.Offset/(1024*1024)).ToString(CultureInfo.InvariantCulture));if(n==null)return;if(!long.TryParse(n,out long value))throw new IOException("Enter a whole MiB offset.");op=op with{Offset=checked(value*1024*1024)};}
            if(kind==OperationKind.ConvertTable){var style=Prompt("Convert partition table","Data partitions keep their sectors. Bootable Windows disks require Windows migration instead.",d.Style=="GPT"?"MBR":"GPT");if(style==null)return;op=op with{Value=style};}
            if(kind==OperationKind.SetPartitionKind){var type=Prompt("Primary or logical","For MBR data disks. Logical conversion needs a free sector before the partition.","Logical");if(type==null)return;op=op with{Value=type};}
            if(kind==OperationKind.SplitPartition){var size=Prompt("Split NTFS partition","Size of the first partition in MiB. The existing files stay in the first partition.",(p!.Size/(2*1024*1024)).ToString());if(size==null)return;if(!long.TryParse(size,out long n))throw new IOException("Enter a whole MiB size.");op=op with{Bytes=checked(n*1024*1024),Value="Split"};}
            if(kind==OperationKind.RedistributeSpace){var right=d.Partitions.SingleOrDefault(x=>x.Offset==p!.Offset+p.Size&&!x.Protected)??throw new IOException("Select the left of two adjacent data volumes.");var amount=Prompt("Redistribute space","MiB to transfer from the right partition to the left. The donor is shrunk, moved with recovery checkpoints, then the destination grows.","128");if(amount==null)return;if(!long.TryParse(amount,out long n))throw new IOException("Enter a whole MiB amount.");op=op with{Bytes=checked(n*1024*1024),TargetPartitionNumber=right.Number};}
            if(kind==OperationKind.MergePartition){var right=d.Partitions.OrderBy(x=>x.Offset).FirstOrDefault(x=>x.Offset==p!.Offset+p.Size&&!x.Protected)??throw new IOException("No adjacent data partition exists to the right.");op=op with{TargetPartitionNumber=right.Number};}
            if(kind==OperationKind.ChangeFileSystem){var fs=Prompt("Convert filesystem","Enter NTFS, exFAT or FAT32. A verified recovery archive is made first. Filesystems other than NTFS cannot retain NTFS permissions, alternate streams, hardlinks or EFS.","NTFS");if(fs==null)return;op=op with{FileSystem=fs};}
            if(kind==OperationKind.ChangeClusterSize){var size=Prompt("Allocation unit size","Bytes per allocation unit (512, 1024, 2048, 4096, 8192, ...). Files are staged and verified before reformatting.","4096");if(size==null)return;if(!int.TryParse(size,out int n))throw new IOException("Enter a byte count.");op=op with{AllocationUnitSize=n};}
            if(kind is OperationKind.MovePartition or OperationKind.ConvertTable or OperationKind.SetPartitionKind)
            {if(!d.Offline){AddWithoutNavigate(DiskOperation.For(d,OperationKind.Offline));d=QueuePlanner.Project(disks,queue).Single(x=>x.Number==d.Number);}op=op with{Identity=d.Identity,Layout=d.Layout};}
            Queue(op,d);
        })));
        Add("Move partition…",OperationKind.MovePartition);Add("Convert GPT / MBR…",OperationKind.ConvertTable);Add("Primary / logical…",OperationKind.SetPartitionKind);Add("Split…",OperationKind.SplitPartition);Add("Merge right partition…",OperationKind.MergePartition);Add("Convert filesystem…",OperationKind.ChangeFileSystem);Add("Change allocation units…",OperationKind.ChangeClusterSize);Add("Redistribute space…",OperationKind.RedistributeSpace);Add("Basic / dynamic…",OperationKind.ConvertDynamic);
        body.Children.Add(Card(Stack(T("Advanced partition tools",19,Ink,true),T("Moves save a resumable checkpoint before each overlapping write. Content conversions retain a verified recovery archive on a separate disk. Each workflow is reviewed in the action chain before it starts.",12,Muted),actions)));
    }
}
