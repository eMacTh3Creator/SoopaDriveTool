using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public partial class MainWindow:Window
{
    static Brush Ink=>B("#15253F");static Brush Muted=>B("#69778F");static Brush Accent=>B("#5355E8");static Brush Mint=>B("#28B89A");
    readonly StackPanel body=new(){Margin=new Thickness(32,24,32,32)};
    readonly StackPanel rail=new();readonly TextBlock title=new(),subtitle=new(),status=new();
    readonly Button cancel=new(){Content="Stop benchmark",Visibility=Visibility.Collapsed};
    readonly Dictionary<int,DiskInfo> queueBaselines=[];readonly Dictionary<string,int> diskSelections=[];
    RunActivity? activeRun;SmartReport? smartReport;
    readonly List<DiskOperation> queue=[];List<DiskInfo> disks=[];readonly bool demo;bool busy;string page="Overview";CancellationTokenSource? cts;BenchmarkRun? lastRun;
    public MainWindow(bool demo=false,string initialPage="Overview",PendingPlan? restoredPlan=null)
    {
        FontSize=13;FontFamily=new FontFamily("Segoe UI Variable Text");Foreground=Ink;Background=B("#F4F6FA");this.demo=demo;Title="SoopaDriveTool";Width=1280;Height=880;MinWidth=1060;MinHeight=700;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Icon=BitmapFrame.Create(new Uri("pack://application:,,,/Assets/logo.png"));
        var root=new Grid{Background=B("#F4F6FA")};root.ColumnDefinitions.Add(new(){Width=new GridLength(210)});root.ColumnDefinitions.Add(new());Content=root;
        var sidebar=new Grid{Background=B("#14233E")};sidebar.RowDefinitions.Add(new(){Height=new GridLength(112)});sidebar.RowDefinitions.Add(new());sidebar.RowDefinitions.Add(new(){Height=new GridLength(110)});root.Children.Add(sidebar);
        var brand=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(20,30,0,0),VerticalAlignment=VerticalAlignment.Top};brand.Children.Add(new Image{Source=new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")),Width=38,Height=38});var brandText=new StackPanel{Margin=new Thickness(10,1,0,0)};brandText.Children.Add(T("SoopaDrive",17,Brushes.White,true));brandText.Children.Add(T("TOOL",10,B("#93A5C5"),true));brand.Children.Add(brandText);sidebar.Children.Add(brand);
        rail.Margin=new Thickness(12,0,12,0);var navScroll=new ScrollViewer{Content=rail,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetRow(navScroll,1);sidebar.Children.Add(navScroll);
        rail.Children.Add(T("WORKSPACE",10,B("#8295B8"),true,new Thickness(14,0,0,18)));
        string[] names=["Overview","Disk manager","Benchmark","Backup & restore","Clone & image","Recovery inspector","Health","Recovery media","Activity","Settings","About & roadmap"];
        string[] icons=["◫","▤","ϟ","↺","⇄","⌕","♡","◉","≡","⚙","ⓘ"];
        for(int i=0;i<names.Length;i++){var name=names[i];var button=Btn(icons[i]+"    "+name,()=>Navigate(name));button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Background=Brushes.Transparent;button.Foreground=B("#BECBE0");button.BorderThickness=new Thickness(0);button.Padding=new Thickness(14,13,4,13);button.Margin=new Thickness(0,0,0,4);button.Tag=name;rail.Children.Add(button);}
        var foot=new StackPanel{Margin=new Thickness(24,12,18,18)};foot.Children.Add(T("LOCAL. PRIVATE. IN CONTROL.",9,B("#8295B8"),true));foot.Children.Add(T("v0.3.0  ·  Storage suite preview",11,B("#BECBE0"),false,new Thickness(0,10,0,0)));Grid.SetRow(foot,2);sidebar.Children.Add(foot);
        var main=new Grid();main.RowDefinitions.Add(new(){Height=new GridLength(104)});main.RowDefinitions.Add(new());main.RowDefinitions.Add(new(){Height=new GridLength(44)});Grid.SetColumn(main,1);root.Children.Add(main);
        var header=new Grid{Margin=new Thickness(32,24,24,12)};header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});var headings=new StackPanel();title.FontSize=26;title.FontWeight=FontWeights.SemiBold;subtitle.Foreground=Muted;subtitle.Margin=new Thickness(0,6,0,0);headings.Children.Add(title);headings.Children.Add(subtitle);header.Children.Add(headings);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};cancel.Click+=(_,_)=>cts?.Cancel();actions.Children.Add(cancel);
        if(!StorageService.IsAdmin&&!demo)actions.Children.Add(Btn("↗  Administrator",ElevateWithPlan));
        actions.Children.Add(Btn("↻  Refresh",async()=>await RefreshAsync()));Grid.SetColumn(actions,1);header.Children.Add(actions);main.Children.Add(header);
        var scroll=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,1);main.Children.Add(scroll);
        var bar=new Border{Background=Preferences.Surface,BorderBrush=B("#E5E9F1"),BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(32,10,20,8)};status.Foreground=Muted;status.Text="Ready · Disk changes require review before applying";bar.Child=status;Grid.SetRow(bar,2);main.Children.Add(bar);
        if(demo){disks=DemoDisks();
            if(initialPage=="Health")smartReport=SmartReport.Parse("""{"model_name":"Illustrative NVMe device (sample)","serial_number":"DEMO-NOT-A-REAL-DRIVE","firmware_version":"Sample","device":{"protocol":"NVMe"},"smart_status":{"passed":true},"nvme_smart_health_information_log":{"critical_warning":0,"temperature":39,"available_spare":100,"percentage_used":18,"data_units_read":2212345,"data_units_written":1333444,"power_on_hours":4200,"power_cycles":241,"unsafe_shutdowns":2,"media_errors":0,"num_err_log_entries":0},"smartctl":{"output":["ILLUSTRATIVE SAMPLE — synthetic SMART data for this interface render.","Model: Illustrative NVMe device","Available spare: 100%","Percentage used: 18%","Power on hours: 4200","Media/data integrity errors: 0"]}}""",0,1);
            if(initialPage=="Activity")activeRun=new RunActivity{StartedUtc=DateTime.UtcNow.AddMinutes(-3),CurrentStep=2,Steps=[new(){Description="Delete partition · Disk 1 / partition 1",State="Completed",Phase="Verified",Percent=100},new(){Description="Create NTFS partition · Disk 1",State="Completed",Phase="Verified",Percent=100},new(){Description="Byte-exact clone · Disk 1 → Disk 2",State="Running",Phase="Copying sectors",Percent=42,Message="Illustrative progress · 76 GiB / 180 GiB · Copying sectors, then a full read-back verification."},new(){Description="Read-back verification",State="Pending"}],Events=[new(DateTime.UtcNow.AddMinutes(-3),0,"Verified",100,"Requested data partition was deleted."),new(DateTime.UtcNow.AddMinutes(-2),1,"Verified",100,"New partition geometry and NTFS label verified."),new(DateTime.UtcNow,2,"Copying sectors",42,"Illustrative progress data for this rendered preview.")]};
            Navigate(initialPage);}else{Navigate(initialPage);Loaded+=async(_,_)=>{await RefreshAsync();if(restoredPlan!=null)try{foreach(var d in restoredPlan.Baselines)QueuePlanner.AssertUnchanged(d,disks.Single(x=>x.Number==d.Number));QueuePlanner.Project(disks,restoredPlan.Operations);foreach(var d in restoredPlan.Baselines)queueBaselines[d.Number]=d;queue.AddRange(restoredPlan.Operations);Navigate("Disk manager");status.Text="Reviewed plan restored after elevation. Review and Apply to start.";}catch(Exception ex){Error(ex);}};}
        Closing+=(_,e)=>{if(busy){e.Cancel=true;MessageBox.Show("Wait for the active operation to finish. Benchmarks can be stopped using the Stop benchmark button.","Operation in progress");}};
    }
    static Brush B(string hex)=>(Brush)new BrushConverter().ConvertFromString(Preferences.Palette(hex))!;
    static TextBlock T(string text,double size=13,Brush? color=null,bool bold=false,Thickness? margin=null)=>new(){Text=text,FontSize=size,Foreground=color??Ink,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap,Margin=margin??new Thickness(0)};
    Button Btn(string text,Action action,bool primary=false){var b=new Button{Content=text};if(primary){b.Background=(Brush)Application.Current.FindResource("Primary");b.Foreground=Brushes.White;b.BorderBrush=b.Background;}b.Click+=(_,_)=>{try{action();}catch(Exception ex){Error(ex);}};return b;}
    Button Btn(string text,Func<Task> action,bool primary=false)=>Btn(text,(Action)(()=>{_ = Guard(action);}),primary);
    async Task Guard(Func<Task> action){try{await action();}catch(Exception ex){Error(ex);}}
    void Error(Exception ex){status.Text=ex.Message;MessageBox.Show(this,ex.Message,"SoopaDriveTool",MessageBoxButton.OK,MessageBoxImage.Warning);}
    Border Card(UIElement child,Thickness? margin=null)=>new(){Background=Preferences.Surface,CornerRadius=new CornerRadius(12),BorderBrush=B("#E5E9F1"),BorderThickness=new Thickness(1),Padding=new Thickness(Preferences.Current.Compact?14:22),Margin=margin??new Thickness(0,0,0,Preferences.Current.Compact?10:18),Child=child};
    StackPanel Stack(params UIElement[] children){var p=new StackPanel();TextBlock? label=null;foreach(var c in children){p.Children.Add(c);if(c is TextBlock text)label=text;else if(c is System.Windows.Controls.Control control&&control is TextBox or PasswordBox or ComboBox){if(label!=null)System.Windows.Automation.AutomationProperties.SetLabeledBy(control,label);}}return p;}
    void Note(string text)=>body.Children.Add(new Border{Background=B("#EDEFFD"),CornerRadius=new CornerRadius(8),Padding=new Thickness(16,12,16,12),Margin=new Thickness(0,0,0,18),Child=T(text,12,B("#51558D"))});
    async Task Work(string label,Func<Task> action,bool canCancel=false)
    {
        if(busy)return;busy=true;body.IsEnabled=false;rail.IsEnabled=false;status.Text=label;cancel.Content=page=="Benchmark"?"Stop benchmark":"Cancel after current safe unit";cancel.Visibility=canCancel?Visibility.Visible:Visibility.Collapsed;
        try{await action();status.Text="Completed · "+label;}
        catch(OperationCanceledException){status.Text="Operation cancelled. Temporary resources were cleaned up.";}
        catch(Exception ex){Error(ex);}
        finally{busy=false;body.IsEnabled=true;rail.IsEnabled=true;cancel.Visibility=Visibility.Collapsed;}
    }
    async Task RefreshAsync()=>await Work("Refreshing storage inventory",async()=>{disks=await StorageService.InventoryAsync();Navigate(page);});
    void Navigate(string name)
    {
        page=name;body.Children.Clear();title.Text=name;subtitle.Text=name switch{"Overview"=>"A clearer view of every drive.","Disk manager"=>"Plan a change. Review it. Apply with confidence.","Benchmark"=>"Understand your storage, one workload at a time.","Backup & restore"=>"Versioned file backups with verified restores.","Clone & image"=>"Choose how to clone, image, or reset a drive.","Recovery inspector"=>"Inspect partition tables without changing the source.","Health"=>"See what your storage controller can tell you.","Activity"=>"Every step, every phase, and what comes next.",_=>"Built for Windows on ARM and Intel / AMD."};
        foreach(var b in rail.Children.OfType<Button>()){bool selected=(string)b.Tag==name;b.Background=selected?B("#31436A"):Brushes.Transparent;b.Foreground=selected?Brushes.White:B("#BECBE0");}
        if(demo)Note("ILLUSTRATIVE DEMO DATA · This screenshot shows the interface, not measured hardware performance.");
        switch(name){case "Overview":Overview();break;case "Disk manager":DiskManager();break;case "Benchmark":Benchmark();break;case "Backup & restore":Backup();break;case "Clone & image":Clone();break;case "Recovery inspector":Recovery();break;case "Health":Health();break;case "Recovery media":RecoveryMedia();break;case "Activity":Activity();break;case "Settings":Settings();break;default:About();break;}
    }
    void Overview()
    {
        var hero=new Grid();hero.ColumnDefinitions.Add(new());hero.ColumnDefinitions.Add(new(){Width=new GridLength(210)});
        var copy=Stack(T("YOUR STORAGE, SIMPLIFIED",10,Accent,true),T("Room for what’s next.",30,Ink,true,new Thickness(0,10,0,7)),T("Manage space, measure speed, and keep a verified backup.\nOne workspace for your drives.",14,Muted),new WrapPanel{Children={Btn("Manage disks  →",()=>Navigate("Disk manager"),true),Btn("Run a benchmark",()=>Navigate("Benchmark"))},Margin=new Thickness(0,20,0,0)});hero.Children.Add(copy);
        var illustration=new Image{Source=new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")),Width=156,Height=156,Opacity=.95};Grid.SetColumn(illustration,1);hero.Children.Add(illustration);body.Children.Add(Card(hero));
        var stats=new Grid();for(int i=0;i<3;i++)stats.ColumnDefinitions.Add(new());
        var values=new[]{(disks.Count.ToString(),"Detected disks"),(Data.Size(disks.Sum(d=>d.Size)),"Total capacity"),(Data.Size(disks.SelectMany(d=>d.Partitions).Sum(p=>p.Free)),"Free on mounted volumes")};
        for(int i=0;i<3;i++){var c=Card(Stack(T(values[i].Item2,12,Muted),T(values[i].Item1,25,Ink,true,new Thickness(0,10,0,0))),new Thickness(0,0,i<2?16:0,18));Grid.SetColumn(c,i);stats.Children.Add(c);}body.Children.Add(stats);
        body.Children.Add(T("Your drives",17,Ink,true,new Thickness(0,6,0,14)));
        if(disks.Count==0)Note("No disk inventory is available yet. Refresh or restart as administrator if Windows restricts access.");
        foreach(var disk in disks)body.Children.Add(DiskCard(disk));
        body.Children.Add(T("Changes stay in the operation queue until you review and apply them. System disks remain protected in this preview.",12,Muted));
    }
    Border DiskCard(DiskInfo d)
    {
        var panel=Stack();var head=new Grid();head.ColumnDefinitions.Add(new());head.ColumnDefinitions.Add(new(){Width=GridLength.Auto});head.Children.Add(Stack(T($"Disk {d.Number}   {d.Name}",15,Ink,true),T($"{Data.Size(d.Size)}  ·  {d.Bus}  ·  {d.Style}  ·  {(d.Offline?"Offline":"Online")}"+(d.Protected?"  ·  System protected":""),12,Muted,false,new Thickness(0,5,0,0))));
        var badge=new Border{ToolTip="Windows storage-provider status. Read full SMART for device attributes and logs.",Background=d.Health=="Healthy"?B("#E4F7F1"):B("#FFF2DD"),CornerRadius=new CornerRadius(6),Padding=new Thickness(10,6,10,6),VerticalAlignment=VerticalAlignment.Top,Child=T(d.Health,11,d.Health=="Healthy"?B("#188A71"):B("#9C6C20"),true)};Grid.SetColumn(badge,1);head.Children.Add(badge);panel.Children.Add(head);
        var map=new Grid{Height=68,Margin=new Thickness(0,18,0,0)};long cursor=0;int column=0;string[] colors=["#5355E8","#38B9A1","#58A6E7","#9A8BE8"];
        foreach(var p in d.Partitions.OrderBy(p=>p.Offset))
        {
            if(p.Offset>cursor)Segment(p.Offset-cursor,"Free",B("#E6EAF2"),Muted);
            Segment(p.Size,(p.Letter.Length>0?p.Letter+": ":"")+(p.Label.Length>0?p.Label:p.Type)+"\n"+Data.Size(p.Size),B(colors[Math.Abs(p.Number)%colors.Length]),Brushes.White);cursor=p.Offset+p.Size;
        }
        if(cursor<d.Size)Segment(d.Size-cursor,"Unallocated\n"+Data.Size(d.Size-cursor),B("#E6EAF2"),Muted);
        void Segment(long length,string label,Brush background,Brush color){map.ColumnDefinitions.Add(new(){Width=new GridLength(Math.Max(.018,(double)length/d.Size),GridUnitType.Star)});var b=new Border{Background=background,CornerRadius=new CornerRadius(5),Margin=new Thickness(0,0,4,0),Padding=new Thickness(9,8,4,5),ClipToBounds=true,ToolTip=label,Child=T((double)length/d.Size<.04?"":label,11,color,true)};Grid.SetColumn(b,column++);map.Children.Add(b);}
        panel.Children.Add(map);panel.Children.Add(T("Layout shown proportionally with a minimum visible width for small partitions.",10,Muted,false,new Thickness(0,9,0,0)));return Card(panel);
    }
    ComboBox DiskPicker(bool dataOnly=false,string key="")
    {
        var items=QueuePlanner.Project(disks,queue).Where(d=>!dataOnly||!d.Protected).ToList();var combo=new ComboBox{ItemsSource=items,DisplayMemberPath="Display"};key=page+":"+key;
        combo.SelectedItem=items.FirstOrDefault(d=>diskSelections.TryGetValue(key,out var n)&&d.Number==n)??items.FirstOrDefault(d=>!d.Protected)??items.FirstOrDefault();
        combo.SelectionChanged+=(_,_)=>{if(combo.SelectedItem is DiskInfo d)diskSelections[key]=d.Number;};return combo;
    }
    ComboBox Choices(params string[] values){var c=new ComboBox{ItemsSource=values};c.SelectedIndex=0;return c;}
    void DiskManager()
    {
        Note("System, EFI, reserved and recovery partitions are protected. Changes to encrypted partitions are blocked. This is the projected layout after your queued steps; pending partitions can be selected for later actions.");
        var disk=DiskPicker();var partition=new ComboBox{DisplayMemberPath="Display"};var panel=Stack(T("Select disk",12,Muted),disk,T("Select partition",12,Muted),partition);var detail=new StackPanel();
        disk.SelectionChanged+=(_,_)=>Update();Update();
        void Update(){partition.ItemsSource=(disk.SelectedItem as DiskInfo)?.Partitions;if(partition.Items.Count>0)partition.SelectedItem=partition.Items.Cast<PartitionInfo>().LastOrDefault(p=>p.Number<0)??partition.Items.Cast<PartitionInfo>().FirstOrDefault(p=>!p.Protected)??partition.Items[0];detail.Children.Clear();if(disk.SelectedItem is DiskInfo d)detail.Children.Add(DiskCard(d));}
        var buttons=new WrapPanel();
        void Add(string text,OperationKind kind,bool requiresPartition=false)=>buttons.Children.Add(Btn(text,async()=>
        {
            if(disk.SelectedItem is not DiskInfo d)throw new IOException("Select a disk.");PartitionInfo? p=requiresPartition?partition.SelectedItem as PartitionInfo:null;if(requiresPartition&&p==null)throw new IOException("Select a partition.");var op=DiskOperation.For(d,kind,p);
            if(kind is OperationKind.CreatePartition or OperationKind.ResizePartition){string hint="Size in GiB (e.g. 32 or 0.5)";if(kind==OperationKind.ResizePartition&&p!.Number>0)hint+="\nWindows limits: "+await StorageService.LimitsAsync(d,p!);var answer=Prompt("Partition size",hint,"1");if(answer==null)return;if(!double.TryParse(answer,NumberStyles.Float,CultureInfo.InvariantCulture,out var gib)||gib<=0||!double.IsFinite(gib))throw new IOException("Enter a positive size using a decimal point.");op=op with{Bytes=checked((long)Math.Floor(gib*1024)*1024*1024)};}
            if(kind is OperationKind.CreatePartition or OperationKind.FormatPartition){var fs=Prompt("Filesystem","Enter NTFS, exFAT, or FAT32","NTFS");if(fs==null)return;op=op with{FileSystem=fs};}
            if(kind is OperationKind.SetLabel or OperationKind.CreatePartition or OperationKind.FormatPartition){var label=Prompt("Volume label","Up to 11 characters",p?.Label??"Data");if(label==null)return;op=op with{Value=label};}
            if(kind==OperationKind.SetLetter){var letter=Prompt("Drive letter","Choose an unused letter D–Z","S");if(letter==null)return;op=op with{Value=letter.ToUpperInvariant()};}
            Queue(op,d);
        }));
        Add("Create partition",OperationKind.CreatePartition);Add("Resize",OperationKind.ResizePartition,true);Add("Format",OperationKind.FormatPartition,true);Add("Delete",OperationKind.DeletePartition,true);Add("Rename",OperationKind.SetLabel,true);Add("Drive letter",OperationKind.SetLetter,true);Add("Remove letter",OperationKind.RemoveLetter,true);Add("Check filesystem",OperationKind.CheckFileSystem,true);Add("SSD TRIM",OperationKind.ReTrim,true);Add("Initialize GPT",OperationKind.InitializeGpt);Add("Initialize MBR",OperationKind.InitializeMbr);Add("Take offline",OperationKind.Offline);Add("Bring online",OperationKind.Online);Add("Read-only",OperationKind.ReadOnly);Add("Writable",OperationKind.ReadWrite);
        panel.Children.Add(buttons);body.Children.Add(Card(panel));body.Children.Add(detail);AdvancedPartitionActions(disk,partition);QueuePanel();
    }
    void Queue(DiskOperation op,DiskInfo d,DiskInfo? target=null)
    {
        var proposed=queue.Append(op).ToList();QueuePlanner.Project(disks,proposed);
        if(!queueBaselines.ContainsKey(d.Number))queueBaselines[d.Number]=disks.Single(x=>x.Number==d.Number);
        if(target!=null&&!queueBaselines.ContainsKey(target.Number))queueBaselines[target.Number]=disks.Single(x=>x.Number==target.Number);
        queue.Add(op);Navigate(page);status.Text="Step added to the chain. Nothing changes until Apply.";
    }
    void QueuePanel()
    {
        var p=Stack(T($"Pending operations ({queue.Count})",16,Ink,true));if(queue.Count==0)p.Children.Add(T("Your queue is empty. Disk changes appear here before they run.",12,Muted,false,new Thickness(0,12,0,16)));
        foreach(var item in queue.Select((o,i)=>(o,i))){var rowItem=new DockPanel();var remove=Btn("Remove from here",()=>{queue.RemoveRange(item.i,queue.Count-item.i);if(queue.Count==0)queueBaselines.Clear();Navigate(page);});DockPanel.SetDock(remove,Dock.Right);rowItem.Children.Add(remove);rowItem.Children.Add(T($"{item.i+1}. {item.o.Description}",13,Ink,false,new Thickness(0,12,0,12)));p.Children.Add(rowItem);}
        var row=new WrapPanel();var apply=Btn(StorageService.IsAdmin?"Review & apply":"Restart as administrator to apply",async()=>{if(!StorageService.IsAdmin){await ElevateWithPlan();return;}await Apply();},true);apply.IsEnabled=queue.Count>0;row.Children.Add(apply);row.Children.Add(Btn("Clear queue",()=>{queue.Clear();queueBaselines.Clear();Navigate(page);}));p.Children.Add(row);p.Children.Add(T("Queued operations can be cleared. Applied operations are not transactional and cannot be undone automatically.",11,Muted));body.Children.Add(Card(p));
    }
    async Task Apply()
    {
        if(queue.Count==0)return;var review=new Window{Title="Review disk operations",Owner=this,Width=650,Height=530,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};
        var p=Stack(T("Review before applying",23,Ink,true),T(string.Join("\n",queue.Select(o=>o.Description)),13,Ink,false,new Thickness(0,20,0,20)),T("These operations change disk data. Keep a separate verified backup. If an operation fails, later operations stop; earlier changes remain applied.",13,Muted));
        string phrase=string.Join(", ",queue.Where(o=>o.Destructive).Select(o=>"DISK "+(o.Kind==OperationKind.CloneDisk?o.TargetDiskNumber:o.DiskNumber)).Distinct());TextBox? confirm=null;
        if(phrase.Length>0){p.Children.Add(T("Destructive operation. Type exactly: "+phrase,13,B("#B34545"),true,new Thickness(0,20,0,0)));confirm=new TextBox();p.Children.Add(confirm);}var ack=new CheckBox{Content="I reviewed the target disks and have a separate backup.",Margin=new Thickness(0,16,0,20)};p.Children.Add(ack);
        var run=Btn("Apply operations",()=>{if(ack.IsChecked!=true||(confirm!=null&&confirm.Text!=phrase)){MessageBox.Show(review,"Complete the review confirmation.");return;}review.DialogResult=true;},true);p.Children.Add(run);review.Content=new Border{Padding=new Thickness(28),Child=new ScrollViewer{Content=p}};if(review.ShowDialog()!=true)return;
        await Work("Applying disk operations",async()=>
        {
            var pending=queue.ToArray();activeRun=new RunActivity{Steps=pending.Select(o=>new StepActivity{Description=o.Description}).ToList()};Navigate("Activity");body.IsEnabled=true;StartActivityTimer();
            try
            {
                await QueueRunner.RunAsync(pending,queueBaselines,activeRun,_=>RefreshRunDisplay());
            }
            finally
            {
                queue.Clear();queueBaselines.Clear();disks=await StorageService.InventoryAsync();StopActivityTimer();RefreshRunDisplay();
            }
        });
    }
    string? Prompt(string heading,string message,string initial="")
    {
        var w=new Window{Title=heading,Owner=this,Width=520,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};var input=new TextBox{Text=initial};var panel=Stack(T(heading,21,Ink,true),T(message,13,Muted,false,new Thickness(0,16,0,8)),input,Btn("Continue",()=>w.DialogResult=true,true));w.Content=new Border{Padding=new Thickness(26),Child=panel};w.Loaded+=(_,_)=>{input.Focus();input.SelectAll();};return w.ShowDialog()==true?input.Text:null;
    }
    void Benchmark()
    {
        Note("Uses an isolated temporary file, never a raw disk. Writing test data consumes drive endurance. Results depend on file size, cache, workload and virtualization; they are not numerically interchangeable with CrystalDiskMark.");
        var volume=new ComboBox{ItemsSource=DriveInfo.GetDrives().Where(d=>d.IsReady&&d.DriveType is DriveType.Fixed or DriveType.Removable).Select(d=>d.RootDirectory.FullName).ToList()};if(volume.Items.Count>0)volume.SelectedIndex=0;
        ComboBox size=Choices("256","1024","4096","16384"),duration=Choices("3","5","10","30"),passes=Choices("1","3","5"),profile=Choices("Standard","Real world"),pattern=Choices("Random data","Zero fill");var mixed=new CheckBox{Content="Include mixed 70% read / 30% write",Margin=new Thickness(0,6,0,18)};
        var fields=new Grid();for(int i=0;i<3;i++)fields.ColumnDefinitions.Add(new());fields.RowDefinitions.Add(new());fields.RowDefinitions.Add(new());var controls=new (string,UIElement)[]{("Target volume",volume),("Test file (MiB)",size),("Seconds / workload",duration),("Passes (mean)",passes),("Profile",profile),("Data pattern",pattern)};
        for(int i=0;i<controls.Length;i++){var p=Stack(T(controls[i].Item1,12,Muted),controls[i].Item2);p.Margin=new Thickness(0,0,i%3<2?18:0,0);Grid.SetColumn(p,i%3);Grid.SetRow(p,i/3);fields.Children.Add(p);}
        int editorIndex=body.Children.Count;var custom=BenchmarkEditor();var editor=body.Children[editorIndex];body.Children.RemoveAt(editorIndex);
        var results=new StackPanel();var run=Btn("ϟ  Start benchmark",async()=>
        {
            if(volume.SelectedItem is not string folder)throw new IOException("Select a volume.");cts=new CancellationTokenSource();
            await Work("Benchmarking storage",async()=>{lastRun=await BenchmarkService.RunAsync(folder,int.Parse((string)size.SelectedItem),int.Parse((string)duration.SelectedItem),int.Parse((string)passes.SelectedItem),(string)profile.SelectedItem,mixed.IsChecked==true,pattern.SelectedIndex==1,new Progress<string>(s=>status.Text=s),cts.Token,custom());Draw();},true);
            cts.Dispose();cts=null;
        },true);
        body.Children.Add(Card(Stack(fields,mixed,run)));body.Children.Add(results);body.Children.Add(editor);Draw();
        void Draw()
        {
            results.Children.Clear();if(lastRun==null){results.Children.Add(Card(Stack(T("Ready when you are.",20,Ink,true),T("Sequential and 4K random workloads at low and high queue depths.\nRead / write throughput, IOPS, average and 99th-percentile latency.",13,Muted,false,new Thickness(0,12,0,0)))));return;}
            var grid=Table(lastRun.Results,("Workload","Name"),("Mode","Mode"),("MB/s","MBps"),("IOPS","Iops"),("Avg ms","LatencyMs"),("p99 ms","P99Ms"));var row=new WrapPanel();row.Children.Add(Btn("Export JSON",()=>ExportBenchmark(false)));row.Children.Add(Btn("Export CSV",()=>ExportBenchmark(true)));results.Children.Add(Card(Stack(T("Benchmark results",18,Ink,true),T($"{lastRun.Engine} · {lastRun.FileMiB} MiB · {lastRun.Passes} pass(es) · {lastRun.Architecture}",12,Muted,false,new Thickness(0,8,0,18)),grid,row)));
        }
        BenchmarkComparison();
        var history=Directory.Exists(Path.Combine(Data.Home,"benchmarks"))?Directory.GetFiles(Path.Combine(Data.Home,"benchmarks"),"*.json").OrderDescending().Take(20).ToArray():[];
        if(history.Length>0){var combo=new ComboBox{ItemsSource=history.Select(Path.GetFileName).ToArray()};combo.SelectedIndex=0;body.Children.Add(Card(Stack(T("Previous runs",16,Ink,true),combo,Btn("Load saved run",async()=>{lastRun=JsonSerializer.Deserialize<BenchmarkRun>(await File.ReadAllTextAsync(history[combo.SelectedIndex]),Data.Json);Draw();}))));}
    }
    void ExportBenchmark(bool csv)
    {
        if(lastRun==null)return;var d=new SaveFileDialog{FileName="SoopaDriveTool-benchmark",Filter=csv?"CSV|*.csv":"JSON|*.json"};if(d.ShowDialog()!=true)return;
        if(!csv)File.WriteAllText(d.FileName,JsonSerializer.Serialize(lastRun,Data.Json));else File.WriteAllLines(d.FileName,new[]{"Workload,Mode,MB/s,IOPS,Average ms,p99 ms"}.Concat(lastRun.Results.Select(r=>CsvCell(r.Name)+","+CsvCell(r.Mode)+FormattableString.Invariant($",{r.MBps:0.00},{r.Iops:0.00},{r.LatencyMs:0.000},{r.P99Ms:0.000}"))));
    }
    static string CsvCell(string value){if(value.TrimStart().Length>0&&"=+-@".Contains(value.TrimStart()[0]))value="'"+value;string quote=char.ConvertFromUtf32(34);return quote+value.Replace(quote,quote+quote)+quote;}
    DataGrid Table(System.Collections.IEnumerable rows,params (string Header,string Path)[] columns)
    {
        var g=new DataGrid{ItemsSource=rows,MinHeight=90,MaxHeight=380,Margin=new Thickness(0,0,0,18)};foreach(var c in columns)g.Columns.Add(new DataGridTextColumn{Header=c.Header,Binding=new Binding(c.Path){StringFormat=c.Path is "MBps" or "Iops"?"{0:N2}":c.Path is "LatencyMs" or "P99Ms"?"{0:N3}":null},Width=new DataGridLength(1,DataGridLengthUnitType.Star)});return g;
    }
    string? PickFolder(string title){var d=new OpenFolderDialog{Title=title};return d.ShowDialog(this)==true?d.FolderName:null;}
    string? PickFile(string filter){var d=new OpenFileDialog{Filter=filter};return d.ShowDialog(this)==true?d.FileName:null;}
    void LegacyBackup()
    {
        Note("File backups are compressed, content-addressed snapshots. Unchanged file content is reused. Objects are not encrypted. Locked files and links stop the backup; live system/app-consistent VSS images are not supported.");
        TextBox source=new TextBox{IsReadOnly=true},repository=new TextBox{IsReadOnly=true};var p=Stack(T("Create a file backup",20,Ink,true),T("Source folder",12,Muted,false,new Thickness(0,18,0,0)),source,Btn("Choose source…",()=>{source.Text=PickFolder("Choose source folder")??source.Text;}),T("Backup repository",12,Muted),repository,Btn("Choose repository…",()=>{repository.Text=PickFolder("Choose a separate backup repository")??repository.Text;}));
        p.Children.Add(Btn("Create verified snapshot",async()=>await Work("Backing up files",async()=>{var m=await BackupEngine.CreateAsync(source.Text,repository.Text,new Progress<string>(s=>status.Text="Backing up: "+s));await BackupEngine.VerifyAsync(m,new Progress<string>(s=>status.Text="Verifying: "+s));MessageBox.Show(this,"Snapshot saved and verified:\n"+m,"Backup complete");}),true));
        p.Children.Add(Btn("Schedule daily backup…",async()=>
        {
            if(!Directory.Exists(source.Text)||!Directory.Exists(repository.Text))throw new IOException("Choose source and repository first.");if(BackupEngine.IsInside(source.Text,repository.Text)||BackupEngine.IsInside(repository.Text,source.Text))throw new IOException("Use separate folders.");var time=Prompt("Daily backup","Local time in 24-hour HH:mm format. Runs when your Windows user is logged on.","18:00");if(time==null)return;if(!TimeOnly.TryParseExact(time,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out _))throw new IOException("Use HH:mm, such as 18:00.");
            var id=Guid.NewGuid().ToString("N");var job=Path.Combine(Data.Home,"jobs",id+".json");await Data.SaveAsync(job,new BackupJob(source.Text,repository.Text));var name="SoopaDriveTool-"+id;var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"schtasks.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};foreach(var arg in new[]{"/Create","/TN",name,"/SC","DAILY","/ST",time,"/TR","\""+Environment.ProcessPath+"\" --backup-job \""+job+"\"","/IT","/RL","LIMITED"})start.ArgumentList.Add(arg);using var process=Process.Start(start)!;var err=process.StandardError.ReadToEndAsync();var output=process.StandardOutput.ReadToEndAsync();await process.WaitForExitAsync();if(process.ExitCode!=0)throw new IOException(await err);await output;await File.WriteAllTextAsync(job+".task.txt",name);MessageBox.Show(this,"Scheduled: "+name+"\nManage or delete this task in Windows Task Scheduler.","Daily backup scheduled");
        }));body.Children.Add(Card(p));
        var restore=Stack(T("Verify or restore a snapshot",20,Ink,true),T("Choose a JSON manifest from your repository’s snapshots folder. Restore to a new or empty folder; existing files are never overwritten.",13,Muted,false,new Thickness(0,12,0,20)));
        var buttons=new WrapPanel();buttons.Children.Add(Btn("Verify snapshot…",async()=>{var m=PickFile("Backup manifest|*.json");if(m==null)return;await Work("Verifying backup",async()=>{await BackupEngine.VerifyAsync(m,new Progress<string>(s=>status.Text=s));MessageBox.Show(this,"All backup objects passed SHA-256 verification.","Backup verified");});}));buttons.Children.Add(Btn("Restore snapshot…",async()=>{var m=PickFile("Backup manifest|*.json");if(m==null)return;var target=PickFolder("Choose an empty restore folder");if(target==null)return;await Work("Restoring files",async()=>{await BackupEngine.RestoreAsync(m,target,new Progress<string>(s=>status.Text=s));MessageBox.Show(this,"Files restored and verified.","Restore complete");});},true));restore.Children.Add(buttons);body.Children.Add(Card(restore));
    }
    void LegacyRecovery()
    {
        Note("Read-only partition inspection. Validates MBR bounds or GPT header/table CRCs in raw image files. This tool does not undelete partitions or recover deleted files.");
        var output=new StackPanel();body.Children.Add(Card(Stack(T("Look inside a disk image",21,Ink,true),T("Open a .sdi, .img, or raw .bin image to examine its partition map. VHD/VHDX containers must be exported to raw format first.",13,Muted,false,new Thickness(0,12,0,20)),Btn("Inspect image…",async()=>{var path=PickFile("Raw disk image|*.sdi;*.img;*.bin|All files|*.*");if(path==null)return;var sector=Prompt("Logical sector size","Enter 512 or 4096. Match the source disk.","512");if(sector==null)return;if(!int.TryParse(sector,out var n)||n is not (512 or 4096))throw new IOException("Choose 512 or 4096.");await Work("Inspecting image",async()=>{var results=await Task.Run(()=>{using var f=File.OpenRead(path);return PartitionInspector.Inspect(f,n);});output.Children.Clear();output.Children.Add(Card(Stack(T(Path.GetFileName(path),17,Ink,true),Table(results,("Scheme","Scheme"),("Index","Index"),("Offset bytes","Offset"),("Length bytes","Length"),("Name","Name"),("Type","Type")))));});},true))));body.Children.Add(output);
    }
    void About()
    {
        body.Children.Add(Card(Stack(T("SoopaDriveTool",27,Ink,true),T("0.3.0 · Storage suite preview · Native Windows ARM64 & x64",14,Accent,true,new Thickness(0,12,0,18)),T("Partition management, Windows and sector cloning, encrypted versioned backup, native recovery media, benchmarks and full available SMART exports. No account, advertisements, or telemetry.",14,Muted),T("This release is unsigned. Device and filesystem capabilities determine which operations are available. Keep independent backups before applying disk changes.",13,B("#9C6C20"),false,new Thickness(0,18,0,0)))));
        body.Children.Add(Card(Stack(T("Storage and recovery capabilities",20,Ink,true),T("Move, split, merge and redistribute partition space\nLive VSS Windows migration and protected Windows images\nEncrypted full, incremental and differential backups\nLost partition and deleted-file recovery with preview/export\nNative WinPE restore, disk cloning, erasure and UEFI boot repair\nARM64 and x64 installers, portable packages and local activity journals",14,Muted,false,new Thickness(0,18,0,0)))));
        var row=new WrapPanel();row.Children.Add(Btn("Source & issue tracker ↗",()=>Process.Start(new ProcessStartInfo("https://github.com/eMacTh3Creator/SoopaDriveTool"){UseShellExecute=true})));row.Children.Add(Btn("Research & roadmap ↗",()=>Process.Start(new ProcessStartInfo("https://github.com/eMacTh3Creator/SoopaDriveTool/blob/main/docs/RESEARCH-AND-PLAN.md"){UseShellExecute=true})));body.Children.Add(row);
    }
    static List<DiskInfo> DemoDisks()=>[new(){Number=0,Name="Windows SSD",UniqueId="DEMO0",Size=1024L*1024*1024*1024,Style="GPT",Bus="NVMe",Health="Healthy",IsSystem=true,IsBoot=true,Partitions=[new(){Number=1,Offset=1048576,Size=260L*1024*1024,Type="System",IsSystem=true},new(){Number=2,Offset=272629760,Size=740L*1024*1024*1024,Letter="C",Label="Windows",FileSystem="NTFS",Free=318L*1024*1024*1024,IsBoot=true,Encryption="None"},new(){Number=3,Offset=800L*1024*1024*1024,Size=220L*1024*1024*1024,Letter="D",Label="Projects",FileSystem="NTFS",Free=150L*1024*1024*1024,Encryption="None"}]},new(){Number=1,Name="Portable SSD",UniqueId="DEMO1",Size=500L*1024*1024*1024,Style="GPT",Bus="USB",Health="Healthy",Partitions=[new(){Number=1,Offset=1048576,Size=450L*1024*1024*1024,Letter="E",Label="Archive",FileSystem="exFAT",Free=281L*1024*1024*1024,Encryption="None"}]}];
}
