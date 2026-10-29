using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SoopaDriveTool.Core;
using SoopaDriveTool.Windows;

partial class Program
{
    // Runs in the existing DiskRefreshUiChecks STA/Application. Selecting a
    // segment or queuing a step never opens a physical device or applies it.
    static void PartitionMapUiChecks(List<MainWindow> windows)
    {
        const long MiB=1024*1024;
        var first=new PartitionInfo{Number=1,Offset=MiB,Size=64*MiB,Letter="R",Label="First",FileSystem="NTFS",Encryption="None"};
        var second=first with{Number=2,Offset=96*MiB,Letter="S",Label="Second"};
        var disk=Disk() with{Number=7,Name="Partition map fixture",UniqueId="MAP-A",Serial="map-A",NativeIdentity="map-native-A",Size=256*MiB,Partitions=[first,second]};
        var manager=MakeWindow("Disk manager",[disk]);windows.Add(manager);
        var body=GetField<StackPanel>(manager,"body");
        ComboBox Partitions(MainWindow window)=>Descendants(GetField<StackPanel>(window,"body")).OfType<ComboBox>().Single(c=>c.Items.OfType<PartitionInfo>().Any());
        Button Segment(MainWindow window,int number)=>Descendants(GetField<StackPanel>(window,"body")).OfType<Button>().Single(b=>b.Tag is PartitionMapTarget t&&t.Disk.Number==disk.Number&&t.Partition?.Number==number);
        MenuItem Item(ContextMenu menu,string header)=>menu.Items.OfType<MenuItem>().Single(i=>Equals(i.Header,header));
        var selected=Partitions(manager);var secondSegment=Segment(manager,second.Number);
        Check(secondSegment.Focusable&&secondSegment.IsTabStop,"Partition segments support keyboard focus and tab navigation");
        secondSegment.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(((PartitionInfo?)selected.SelectedItem)?.Number==second.Number,"Clicking a graphical partition selects the matching partition control");
        Check(GetField<List<DiskOperation>>(manager,"queue").Count==0,"Selecting a graphical partition does not queue or apply an operation");
        Check(System.Windows.Automation.AutomationProperties.GetItemStatus(secondSegment)=="Selected","The selected graphical partition is marked for assistive technology");
        var firstSegment=Segment(manager,first.Number);
        firstSegment.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Right){RoutedEvent=UIElement.PreviewMouseRightButtonDownEvent});
        Check(((PartitionInfo?)selected.SelectedItem)?.Number==first.Number,"Right-click selects its graphical partition before showing commands");
        Check(Descendants(body).Contains(firstSegment),"Right-click selection keeps the clicked map element attached for its context menu");
        Check(firstSegment.ContextMenu!=null,"Every selectable partition has a context menu");
        var menu=firstSegment.ContextMenu!;
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        Check(GetField<bool>(manager,"partitionMenuOpen")&&!CanRefresh(manager),"Opening a partition context menu pauses automatic inventory refresh");
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
        Check(!GetField<bool>(manager,"partitionMenuOpen"),"Closing a partition context menu releases the refresh pause");
        foreach(var header in new[]{"Move…","Shrink…","Expand…","Format…","Erase contents…","Rename…","Delete partition"})
            Check(menu.Items.OfType<MenuItem>().Any(i=>Equals(i.Header,header)),"Partition menu exposes "+header);
        Check(Equals(Item(menu,"Shrink…").Tag,OperationKind.ResizePartition)&&Equals(Item(menu,"Expand…").Tag,OperationKind.ResizePartition),"Shrink and expand use the reviewed resize operation");
        Check(Equals(Item(menu,"Erase contents…").Tag,OperationKind.WipePartition)&&Equals(Item(menu,"Delete partition").Tag,OperationKind.DeletePartition),"Partition content erasure and partition deletion are separate menu actions");
        Check(Item(menu,"Rename…").IsEnabled&&Item(menu,"Delete partition").IsEnabled,"Ordinary unencrypted data partitions permit rename and delete actions");
        Item(menu,"Delete partition").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var pending=GetField<List<DiskOperation>>(manager,"queue");
        Check(pending.Count==1&&pending[0].Kind==OperationKind.DeletePartition&&pending[0].PartitionNumber==first.Number,"The context-menu delete action queues the clicked partition without applying it");
        var diskList=Descendants(body).OfType<ComboBox>().Single(c=>c.Items.OfType<DiskInfo>().Any());
        Check((diskList.SelectedItem as DiskInfo)?.Number==disk.Number,"Queuing a layout change retains the selected physical disk");
        Check(Descendants(body).OfType<Button>().Any(b=>b.Tag is PartitionMapTarget t&&t.Partition?.Number==second.Number)&&!Descendants(body).OfType<Button>().Any(b=>b.Tag is PartitionMapTarget t&&t.Partition?.Number==first.Number),"The partition map remains visible with the queued deletion projected");
        var clearQueue=Descendants(body).OfType<Button>().Single(b=>Equals(b.Content,"Clear queue"));clearQueue.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(Descendants(body).OfType<Button>().Any(b=>b.Tag is PartitionMapTarget t&&t.Partition?.Number==first.Number),"Clearing the queue restores the original map without dropping disk selection");
        Check(GetField<List<DiskOperation>>(manager,"queue").Count==0,"Clearing graphical changes never applies them");
        // Requeue for the baseline assertion after checking the cleared preview.
        Call(manager,"Queue",DiskOperation.For(disk,OperationKind.DeletePartition,first),disk,null);
        Check(GetField<Dictionary<int,DiskInfo>>(manager,"queueBaselines")[disk.Number].Partitions.Count==2,"A queued context-menu action leaves the actual disk baseline unchanged");

        foreach(var (name,restricted) in new[]{
            ("system disk",disk with{IsSystem=true}),
            ("protected partition",disk with{Partitions=[first with{Type="Recovery"},second]}),
            ("encrypted partition",disk with{Partitions=[first with{Encryption="BitLocker"},second]}),
            ("unknown encryption",disk with{Partitions=[first with{Encryption="Unknown"},second]}),
            ("incomplete inventory",disk with{InventoryWarning="Fixture: partition provider unavailable"}),
            ("read-only disk",disk with{ReadOnly=true})})
        {
            var window=MakeWindow("Disk manager",[restricted]);windows.Add(window);
            var restrictedMenu=Segment(window,first.Number).ContextMenu!;
            Check(new[]{"Move…","Shrink…","Expand…","Format…","Erase contents…","Rename…","Delete partition"}.All(header=>!Item(restrictedMenu,header).IsEnabled),"Partition mutations are disabled for "+name);
            Check(Item(restrictedMenu,"Delete partition").ToolTip!=null,"Disabled partition actions explain the restriction for "+name);
        }

        var freeWindow=MakeWindow("Disk manager",[disk]);windows.Add(freeWindow);
        var gaps=Descendants(GetField<StackPanel>(freeWindow,"body")).OfType<Button>().Where(b=>b.Tag is PartitionMapTarget t&&t.Partition==null&&t.Length>=16*MiB).ToList();
        Check(gaps.Count>0&&gaps.All(b=>b.ContextMenu?.Items.OfType<MenuItem>().Any(i=>Equals(i.Tag,OperationKind.CreatePartition))==true),"Graphical unallocated-space segments expose partition creation");
        long firstAllocation=QueuePlanner.Allocate(disk,16*MiB);
        Check(gaps.Any(b=>b.Tag is PartitionMapTarget t&&t.Offset==firstAllocation&&b.ContextMenu!.Items.OfType<MenuItem>().Single(i=>Equals(i.Tag,OperationKind.CreatePartition)).IsEnabled),"The first usable free-space gap permits a new partition");
        Check(gaps.Where(b=>b.Tag is PartitionMapTarget t&&t.Offset!=firstAllocation).All(b=>!b.ContextMenu!.Items.OfType<MenuItem>().Single(i=>Equals(i.Tag,OperationKind.CreatePartition)).IsEnabled),"Later gaps cannot silently create a partition in a different free-space region");

        var replacement=disk with{UniqueId="MAP-REPLACEMENT",Serial="map-B",NativeIdentity="map-native-B"};
        var stale=MakeWindow("Disk manager",[replacement]);windows.Add(stale);
        var attempt=(Task)Call(stale,"PreparePartitionActionAsync",disk,first,OperationKind.ResizePartition,"Shrink")!;
        Check(attempt.IsCompleted,"A stale graphical target is rejected before querying Windows or opening a size dialog");
        Reject(()=>attempt.GetAwaiter().GetResult(),"A captured partition cannot prepare an action against a replacement disk");
        Check(GetField<List<DiskOperation>>(stale,"queue").Count==0,"Rejected stale partition commands leave the operation queue empty");
    }
}
