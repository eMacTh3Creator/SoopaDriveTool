using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public partial class MainWindow
{
    void DiskManager()
    {
        Note("Select a partition in the map or list. Right-click for actions. Changes stay in the queue until you apply them.");
        var disk=DiskPicker();var partition=new ComboBox{DisplayMemberPath="Display"};var detail=new StackPanel();Border? map=null;
        var fields=new Grid();fields.ColumnDefinitions.Add(new());fields.ColumnDefinitions.Add(new());
        var diskField=Stack(T("Disk",12,Muted),disk);diskField.Margin=new Thickness(0,0,16,0);fields.Children.Add(diskField);var partitionField=Stack(T("Partition",12,Muted),partition);Grid.SetColumn(partitionField,1);fields.Children.Add(partitionField);
        var bar=new WrapPanel();bar.Children.Add(Btn("Create partition…",async()=>{if(disk.SelectedItem is not DiskInfo d)throw new IOException("Select a disk.");await PreparePartitionActionAsync(d,null,OperationKind.CreatePartition);}));
        Button? actions=null;actions=Btn("Partition actions ▾",()=>{if(disk.SelectedItem is not DiskInfo d||partition.SelectedItem is not PartitionInfo p)throw new IOException("Select a partition.");ShowPartitionMenu(actions!,BuildPartitionMenu(d,p));});bar.Children.Add(actions);
        Button? diskActions=null;diskActions=Btn("Disk actions ▾",()=>{if(disk.SelectedItem is not DiskInfo d)throw new IOException("Select a disk.");ShowPartitionMenu(diskActions!,BuildDiskMenu(d));});bar.Children.Add(diskActions);
        body.Children.Add(Card(Stack(fields,bar)));body.Children.Add(detail);
        disk.SelectionChanged+=(_,_)=>UpdateDisk();
        partition.SelectionChanged+=(_,_)=>
        {
            ResetApplyConfirmation();if(disk.SelectedItem is DiskInfo d&&partition.SelectedItem is PartitionInfo p){partitionSelections[PartitionSelectionKey(d)]=p;status.Text=$"Disk {d.Number} · Partition {p.Number} · {p.Display}";}
            if(map!=null)HighlightPartition(map,partition.SelectedItem as PartitionInfo);
        };
        UpdateDisk();QueuePanel();
        void UpdateDisk()
        {
            partition.ItemsSource=(disk.SelectedItem as DiskInfo)?.Partitions;
            if(disk.SelectedItem is DiskInfo d)
            {
                partitionSelections.TryGetValue(PartitionSelectionKey(d),out var previous);
                partition.SelectedItem=d.Partitions.FirstOrDefault(p=>previous!=null&&p.Number==previous.Number&&p.Offset==previous.Offset&&p.Size==previous.Size)??d.Partitions.LastOrDefault(p=>p.Number<0)??d.Partitions.FirstOrDefault(p=>!p.Protected)??d.Partitions.FirstOrDefault();
                map=DiskCard(d,p=>partition.SelectedItem=p,p=>BuildPartitionMenu(d,p),partition.SelectedItem as PartitionInfo);detail.Children.Clear();detail.Children.Add(map);
            }
            else{map=null;detail.Children.Clear();}
        }
    }
    void ShowPartitionMenu(Button owner,ContextMenu menu){owner.ContextMenu=menu;menu.PlacementTarget=owner;menu.Placement=PlacementMode.Bottom;menu.IsOpen=true;}
    string? PartitionActionProblem(DiskInfo d,PartitionInfo? p,OperationKind kind,string direction="")
    {
        if(busy)return "Wait for the current operation to finish.";
        if(queueIssue!=null)return queueIssue;
        if(DiskRefreshPolicy.RetainSelection(ProjectedDisks(),d)==null)return "This disk changed or disconnected. Select it again.";
        if(d.InventoryWarning.Length>0)return d.InventoryWarning;
        if(d.Protected)return "The running Windows disk is protected.";
        if(d.ReadOnly&&kind!=OperationKind.ReadWrite)return "This disk is read-only.";
        if(d.Style=="Dynamic"&&kind!=OperationKind.ConvertDynamic)return "Use the basic/dynamic conversion tool for this disk.";
        if(p!=null)
        {
            if(!d.Partitions.Any(x=>x.Number==p.Number&&x.Offset==p.Offset&&x.Size==p.Size))return "This partition changed. Select it again.";
            if(p.Protected)return "System, EFI, reserved, and recovery partitions are protected.";
            if(p.Encryption!="None")return "BitLocker is enabled or the encryption status is unavailable.";
            if(p.Number<0&&kind is OperationKind.ResizePartition or OperationKind.MovePartition or OperationKind.WipePartition)return "Apply partition creation before this operation.";
            if(kind is OperationKind.MovePartition or OperationKind.WipePartition&&p.FileSystem is not ("NTFS" or "exFAT" or "FAT32"))return "This action requires a supported NTFS, exFAT, or FAT32 partition with verifiable filesystem metadata.";
            if(kind==OperationKind.ResizePartition&&p.FileSystem!="NTFS")return "Shrinking and expanding require a supported NTFS volume.";
        }
        if(kind==OperationKind.CreatePartition&&d.Style is not ("GPT" or "MBR"))return "Initialize this disk before creating a partition.";
        if(d.Offline&&kind is not (OperationKind.MovePartition or OperationKind.WipePartition or OperationKind.Online or OperationKind.ReadOnly or OperationKind.ReadWrite or OperationKind.ConvertTable or OperationKind.SetPartitionKind))return "Bring this disk online first.";
        return null;
    }
    ContextMenu BuildPartitionMenu(DiskInfo disk,PartitionInfo partition)
    {
        var menu=TrackPartitionMenu(new ContextMenu());
        menu.Items.Add(new MenuItem{Header=$"Partition {partition.Number} · {partition.Display}",IsEnabled=false});menu.Items.Add(new Separator());
        void Add(string label,OperationKind kind,string direction="")
        {
            var item=new MenuItem{Header=label,Tag=kind};var problem=PartitionActionProblem(disk,partition,kind,direction);item.IsEnabled=problem==null;item.ToolTip=problem;
            item.Click+=async(_,_)=>await Guard(()=>PreparePartitionActionAsync(disk,partition,kind,direction));menu.Items.Add(item);
        }
        Add("Move…",OperationKind.MovePartition);Add("Shrink…",OperationKind.ResizePartition,"Shrink");Add("Expand…",OperationKind.ResizePartition,"Expand");Add("Format…",OperationKind.FormatPartition);Add("Erase contents…",OperationKind.WipePartition);
        menu.Items.Add(new Separator());Add("Rename…",OperationKind.SetLabel);Add("Change drive letter…",OperationKind.SetLetter);Add("Delete partition",OperationKind.DeletePartition);
        menu.Items.Add(new Separator());var more=new MenuItem{Header="More tools"};
        AddAdvancedPartitionMenu(more,disk,partition);menu.Items.Add(more);return menu;
    }
    void UpdatePartitionMenu(ContextMenu? menu,DiskInfo d,PartitionInfo p)
    {
        if(menu==null)return;foreach(var item in menu.Items.OfType<MenuItem>())if(item.Tag is OperationKind kind){var reason=PartitionActionProblem(d,p,kind);item.IsEnabled=reason==null;item.ToolTip=reason;}
    }
    ContextMenu BuildFreeSpaceMenu(DiskInfo d,long offset,long length)
    {
        var menu=TrackPartitionMenu(new ContextMenu());string? reason=PartitionActionProblem(d,null,OperationKind.CreatePartition);
        if(reason==null)try{if(length<16*QueuePlanner.MiB||QueuePlanner.Allocate(d,16*QueuePlanner.MiB)!=offset)reason="Use the first available allocation region, or apply earlier changes first.";}catch(InvalidOperationException ex){reason=ex.Message;}
        var item=new MenuItem{Header="Create partition…",Tag=OperationKind.CreatePartition,IsEnabled=reason==null,ToolTip=reason};item.Click+=async(_,_)=>await Guard(()=>PreparePartitionActionInRegionAsync(d,null,OperationKind.CreatePartition,"",offset,length));menu.Items.Add(item);return menu;
    }
    ContextMenu BuildDiskMenu(DiskInfo d)
    {
        var menu=TrackPartitionMenu(new ContextMenu());
        foreach(var (label,kind) in new (string,OperationKind)[]{("Initialize GPT",OperationKind.InitializeGpt),("Initialize MBR",OperationKind.InitializeMbr),("Convert GPT / MBR…",OperationKind.ConvertTable),("Basic / dynamic…",OperationKind.ConvertDynamic),("Take offline",OperationKind.Offline),("Bring online",OperationKind.Online),("Read-only",OperationKind.ReadOnly),("Writable",OperationKind.ReadWrite)})
        {var item=new MenuItem{Header=label,Tag=kind};string? reason=PartitionActionProblem(d,null,kind);if(reason==null&&kind is OperationKind.InitializeGpt or OperationKind.InitializeMbr&&(d.Style!="RAW"||d.Partitions.Count>0))reason="Initialization requires an empty RAW disk.";item.IsEnabled=reason==null;item.ToolTip=reason;item.Click+=async(_,_)=>await Guard(()=>PreparePartitionActionAsync(d,null,kind));menu.Items.Add(item);}return menu;
    }
    Task PreparePartitionActionAsync(DiskInfo disk,PartitionInfo? partition,OperationKind kind,string direction="")=>PreparePartitionActionInRegionAsync(disk,partition,kind,direction,null,null);
    async Task PreparePartitionActionInRegionAsync(DiskInfo disk,PartitionInfo? partition,OperationKind kind,string direction,long? freeOffset,long? freeLength)
    {
        if(PartitionActionProblem(disk,partition,kind,direction) is string problem)throw new IOException(problem);
        var op=DiskOperation.For(disk,kind,partition);var d=disk;
        if(kind is OperationKind.CreatePartition or OperationKind.ResizePartition)
        {
            long minimum=16*QueuePlanner.MiB,maximum=freeLength??disk.Size;
            if(kind==OperationKind.ResizePartition)
            {
                if(partition==null)throw new IOException("Select a partition.");
                var limits=JsonSerializer.Deserialize<PartitionSizeLimits>(await StorageService.LimitsAsync(disk,partition),Data.Json)??throw new IOException("Windows did not return partition size limits.");minimum=limits.SizeMin;maximum=limits.SizeMax;
                if(PartitionActionProblem(disk,partition,kind,direction) is string changed)throw new IOException(changed);
            }
            long initial=kind==OperationKind.CreatePartition?Math.Min(maximum,1024*QueuePlanner.MiB):direction=="Shrink"?Math.Max(minimum,partition!.Size/2):maximum;
            initial=initial/QueuePlanner.MiB*QueuePlanner.MiB;
            string title=kind==OperationKind.CreatePartition?"Create partition":direction=="Shrink"?"Shrink partition":"Expand partition";
            string hint=$"New total size in GiB. Windows range: {Data.Size(minimum)} to {Data.Size(maximum)}."+(partition!=null?$" Current size: {Data.Size(partition.Size)}.":"");
            var answer=Prompt(title,hint,(initial/(double)(1L<<30)).ToString("0.######",CultureInfo.InvariantCulture));if(answer==null)return;
            if(!double.TryParse(answer,NumberStyles.Float,CultureInfo.InvariantCulture,out double gib)||!double.IsFinite(gib)||gib<=0)throw new IOException("Enter a positive size using a decimal point.");
            long bytes=checked((long)Math.Floor(gib*1024)*QueuePlanner.MiB);if(bytes<minimum||bytes>maximum)throw new IOException("Choose a size within the Windows limits.");
            if(partition!=null&&(direction=="Shrink"&&bytes>=partition.Size||direction=="Expand"&&bytes<=partition.Size))throw new IOException(direction=="Shrink"?"The new size must be smaller than the current partition.":"The new size must be larger than the current partition.");
            if(freeOffset.HasValue&&QueuePlanner.Allocate(d,bytes)!=freeOffset.Value)throw new IOException("The size does not fit this unallocated region. Select it again and choose a smaller size.");op=op with{Bytes=bytes};
        }
        if(kind is OperationKind.CreatePartition or OperationKind.FormatPartition){var format=ChoosePartitionFormat(partition?.Label??"Data");if(format==null)return;op=op with{FileSystem=format.FileSystem,Value=format.Label};}
        if(kind==OperationKind.SetLabel){var label=Prompt("Rename partition","Volume label (up to 11 characters)",partition?.Label??"Data");if(label==null)return;op=op with{Value=label};}
        if(kind==OperationKind.SetLetter){var letter=Prompt("Drive letter","Choose an unused letter D–Z",partition?.Letter??"S");if(letter==null)return;op=op with{Value=letter.ToUpperInvariant()};}
        if(kind==OperationKind.WipePartition){if(partition==null)throw new IOException("Select a partition.");var wipe=ChoosePartitionErase(partition);if(wipe==null)return;op=op with{WipeMode=wipe.Value};}
        if(AdvancedPartitionService.Handles(kind)||kind==OperationKind.ConvertDynamic){var configured=ConfigureAdvancedPartition(d,partition,op);if(configured==null)return;op=configured;}
        PrepareBatch(()=>
        {
            if(PartitionActionProblem(disk,partition,kind,direction) is string changed)throw new IOException(changed);
            if(kind is OperationKind.MovePartition or OperationKind.ConvertTable or OperationKind.SetPartitionKind or OperationKind.WipePartition)
            {if(!d.Offline){AddWithoutNavigate(DiskOperation.For(d,OperationKind.Offline));d=ProjectedDisks().Single(x=>x.Number==d.Number);}op=op with{Identity=d.Identity,Layout=d.Layout};}
            Queue(op,d);
        });
    }
    sealed record PartitionSizeLimits(long SizeMin,long SizeMax);
    sealed record PartitionFormatChoice(string FileSystem,string Label);
    PartitionFormatChoice? ChoosePartitionFormat(string label)
    {
        var window=new Window{Title="Partition format",Owner=this,Width=460,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var fs=Choices("NTFS","exFAT","FAT32");var name=new TextBox{Text=label};var buttons=new WrapPanel();var cancelButton=Btn("Cancel",()=>window.DialogResult=false);cancelButton.IsCancel=true;buttons.Children.Add(cancelButton);buttons.Children.Add(Btn("Add to queue",()=>window.DialogResult=true,true));
        window.Content=new Border{Padding=new Thickness(24),Child=Stack(T("Partition format",21,Ink,true),T("Filesystem",12,Muted),fs,T("Volume label (up to 11 characters)",12,Muted),name,T("Formatting removes the current files. Nothing changes until you apply the queue.",12,Muted),buttons)};
        return window.ShowDialog()==true?new((string)fs.SelectedItem,name.Text):null;
    }
    WipeMode? ChoosePartitionErase(PartitionInfo partition)
    {
        var window=new Window{Title="Erase partition contents",Owner=this,Width=520,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};var mode=Choices("Zero overwrite + verification","Random overwrite + verification","Three passes + verification after each pass");var buttons=new WrapPanel();var cancelButton=Btn("Cancel",()=>window.DialogResult=false);cancelButton.IsCancel=true;buttons.Children.Add(cancelButton);buttons.Children.Add(Btn("Add to queue",()=>window.DialogResult=true,true));
        window.Content=new Border{Padding=new Thickness(24),Child=Stack(T("Erase partition contents",21,Ink,true),T(partition.Display,13,Ink),T("Overwrites only this partition's sectors and keeps its partition-table entry. The disk is taken offline first. Other partitions are not erased. Reformat the erased partition before using it again. SSD remapped or overprovisioned cells are outside this overwrite.",12,Muted,false,new Thickness(0,14,0,14)),mode,buttons)};return window.ShowDialog()==true?(WipeMode)mode.SelectedIndex:null;
    }
}
