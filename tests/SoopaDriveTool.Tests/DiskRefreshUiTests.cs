using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SoopaDriveTool.Core;
using SoopaDriveTool.Windows;

partial class Program
{
    // Runs without showing a window, loading user settings, or touching a real disk.
    static void DiskRefreshUiChecks()
    {
        Exception? failure=null;
        var thread=new Thread(()=>
        {
            App? application=null;var windows=new List<MainWindow>();var original=Preferences.Current;
            try
            {
                application=new App{ShutdownMode=ShutdownMode.OnExplicitShutdown};application.InitializeComponent();
                SetPreferences(new AppPreferences(Theme:"Light"));
                ConfirmationUiChecks(windows);PartitionMapUiChecks(windows);
                var old=JsonSerializer.Deserialize<AppPreferences>("{\"Theme\":\"Dark\",\"WaitingFacts\":false,\"Compact\":true}",Data.Json)!;
                Check(!old.AutoRefreshDisks&&old.DiskRefreshSeconds==10&&old.Theme=="Dark"&&!old.WaitingFacts&&old.Compact,"Existing preferences get disabled auto refresh and a ten-second interval");
                var saved=old with{AutoRefreshDisks=true,DiskRefreshSeconds=30};
                Check(JsonSerializer.Deserialize<AppPreferences>(JsonSerializer.Serialize(saved,Data.Json),Data.Json)==saved,"Auto-refresh preference survives JSON round trip");
                Check(new[]{5,10,30,60}.All(n=>Preferences.NormalizeRefreshSeconds(n)==n)&&new[]{-1,0,1,9,600}.All(n=>Preferences.NormalizeRefreshSeconds(n)==10),"Refresh intervals accept supported values and normalize invalid settings");

                var disk=Disk() with{Name="Refresh fixture",Bus="USB",NativeIdentity="refresh-native"};
                var other=disk with{Number=3,UniqueId="other-refresh-disk",Serial="other",NativeIdentity="other-native"};
                var plugged=disk with{Number=4,UniqueId="plugged-refresh-disk",Serial="plugged",NativeIdentity="plugged-native"};
                var clone=MakeWindow("Clone & image",[disk,other]);windows.Add(clone);
                var tab=Descendants(GetField<StackPanel>(clone,"body")).OfType<TabItem>().Single(t=>Equals(t.Header,"Clone"));
                var form=(StackPanel)((Border)((StackPanel)tab.Content).Children[0]).Child;
                var selectors=form.Children.OfType<ComboBox>().ToArray();
                var mode=selectors[0];var source=selectors[1];var partition=selectors[2];var target=selectors[3];
                mode.SelectedIndex=1;source.SelectedItem=source.Items.Cast<DiskInfo>().Single(d=>d.Number==disk.Number);target.SelectedItem=target.Items.Cast<DiskInfo>().Single(d=>d.Number==other.Number);
                var scratch=form.Children.OfType<TextBox>().Single();scratch.Text=@"R:\unsaved scratch folder";
                var selectedSource=source.SelectedItem;var selectedPartition=partition.SelectedItem;
                Call(clone,"AcceptInventory",new List<DiskInfo>{disk,other,plugged});
                Check(Descendants(GetField<StackPanel>(clone,"body")).Contains(scratch)&&mode.SelectedIndex==1&&scratch.Text==@"R:\unsaved scratch folder","Inventory refresh preserves unsaved clone method and scratch folder");
                Check(ReferenceEquals(source.SelectedItem,selectedSource)&&ReferenceEquals(partition.SelectedItem,selectedPartition),"Unrelated hotplug retains source and partition selections");
                Check(source.Items.Count==3&&target.Items.Count==3&&((DiskInfo)target.SelectedItem).Identity==other.Identity,"Hotplug adds a drive without changing clone destination");
                var replacement=disk with{UniqueId="replacement-refresh-disk",Serial="replacement",NativeIdentity="replacement-native"};
                Call(clone,"AcceptInventory",new List<DiskInfo>{replacement,other,plugged});
                Check(source.SelectedItem==null&&partition.SelectedItem==null,"Replacement using the removed disk number clears source and volume selections");
                Check(mode.SelectedIndex==1&&scratch.Text==@"R:\unsaved scratch folder","Disk removal preserves unrelated form entries");

                var manager=MakeWindow("Disk manager",[disk,other]);windows.Add(manager);
                var operation=DiskOperation.For(disk,OperationKind.SetLabel,disk.Partitions[0]) with{Value="Reviewed"};
                Call(manager,"Queue",operation,disk,null);
                var queue=GetField<List<DiskOperation>>(manager,"queue");var baselines=GetField<Dictionary<int,DiskInfo>>(manager,"queueBaselines");
                Call(manager,"AcceptInventory",new List<DiskInfo>{disk,other,plugged});
                Check(queue.Count==1&&ReferenceEquals(baselines[disk.Number],disk)&&GetField<Button>(manager,"queueApply").IsEnabled,"Unrelated hotplug preserves reviewed queue and immutable baseline");
                Call(manager,"AcceptInventory",new List<DiskInfo>{replacement,other});
                var issue=GetField<string>(manager,"queueIssue");
                Check(queue.Count==1&&!GetField<Button>(manager,"queueApply").IsEnabled&&GetField<TextBlock>(manager,"queueWarning").Visibility==Visibility.Visible&&!string.IsNullOrWhiteSpace(issue),"Replacing a queued disk retains the plan with a warning and disables Apply");
                Call(manager,"AcceptInventory",new List<DiskInfo>{disk,other});
                Check(GetField<string>(manager,"queueIssue")==issue&&!GetField<Button>(manager,"queueApply").IsEnabled,"Reconnecting a queued disk does not silently rearm the plan");
                var clear=Descendants(GetField<StackPanel>(manager,"body")).OfType<Button>().Single(b=>Equals(b.Content,"Clear queue"));
                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(queue.Count==0&&baselines.Count==0&&GetField<object?>(manager,"queueIssue")==null,"Clearing a stale queue removes its warning and baselines");

                var stale=MakeWindow("Disk manager",[replacement,other]);windows.Add(stale);
                Reject(()=>Call(stale,"Queue",operation,disk,null),"Captured disk selection cannot queue an operation against its replacement");
                Check(GetField<List<DiskOperation>>(stale,"queue").Count==0&&GetField<Dictionary<int,DiskInfo>>(stale,"queueBaselines").Count==0,"Rejected stale selection leaves no pending operation or baseline");

                var settings=MakeWindow("Settings",[]);windows.Add(settings);
                Check(CanRefresh(settings),"Idle enabled window permits an automatic inventory check");
                SetField(settings,"busy",true);Check(!CanRefresh(settings),"Automatic refresh pauses during an operation");
                ((Task)Call(settings,"RefreshAutomaticallyAsync")!).GetAwaiter().GetResult();
                Check(!GetField<bool>(settings,"inventoryRefreshing"),"Paused automatic refresh does not start inventory work");
                SetField(settings,"busy",false);SetField(settings,"inventoryRefreshing",true);Check(!CanRefresh(settings),"Automatic refresh prevents concurrent inventory reads");SetField(settings,"inventoryRefreshing",false);
                settings.IsEnabled=false;Check(!CanRefresh(settings),"Disabled owner pauses automatic refresh for dialogs");settings.IsEnabled=true;
                SetPreferences(new AppPreferences(Theme:"Light",AutoRefreshDisks:false,DiskRefreshSeconds:30));Call(settings,"ConfigureDiskRefresh");
                var timer=GetField<DispatcherTimer>(settings,"diskRefreshTimer");Check(timer.Interval==TimeSpan.FromSeconds(30)&&!timer.IsEnabled,"Disabled preference stops timer and retains selected interval");
                SetPreferences(Preferences.Current with{AutoRefreshDisks=true});Call(settings,"ConfigureDiskRefresh");
                Check(timer.IsEnabled&&timer.Interval==TimeSpan.FromSeconds(30),"Enabled preference starts the configured refresh timer");
                settings.Close();Check(!timer.IsEnabled&&!CanRefresh(settings),"Closing the window stops automatic refresh");windows.Remove(settings);
            }
            catch(Exception ex){failure=ex is TargetInvocationException {InnerException:not null} reflected?reflected.InnerException:ex;}
            finally
            {
                foreach(var window in windows)try{SetField(window,"busy",false);window.Close();}catch{}
                try{SetPreferences(original);}catch{}
                application?.Shutdown();
            }
        }){IsBackground=true,Name="Disk refresh UI tests"};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
        if(!thread.Join(TimeSpan.FromSeconds(45)))throw new TimeoutException("Disk refresh UI tests did not finish.");
        if(failure!=null)ExceptionDispatchInfo.Capture(failure).Throw();
    }
    static MainWindow MakeWindow(string page,List<DiskInfo> inventory)
    {
        var window=new MainWindow(false,"Settings");SetField(window,"disks",inventory);Call(window,"Navigate",page);return window;
    }
    static bool CanRefresh(MainWindow window)=>(bool)typeof(MainWindow).GetProperty("CanRefreshAutomatically",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
    static void SetPreferences(AppPreferences preferences)
    {
        typeof(Preferences).GetProperty(nameof(Preferences.Current))!.SetValue(null,preferences);
        typeof(Preferences).GetMethod("Apply",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null);
    }
    static T GetField<T>(object instance,string name)=>(T)instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(instance)!;
    static void SetField(object instance,string name,object? value)=>instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(instance,value);
    static object? Call(object instance,string name,params object?[] arguments)=>instance.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(instance,arguments);
    static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach(var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {yield return child;foreach(var descendant in Descendants(child))yield return descendant;}
    }
}
