using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public partial class MainWindow
{
    readonly DispatcherTimer diskRefreshTimer=new();
    readonly CancellationTokenSource inventoryLifetime=new();
    readonly List<Action> inventoryObservers=[];
    readonly CheckBox automaticRefresh=new(){Content="Auto refresh",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)};
    bool inventoryRefreshing,windowClosed;long inventoryRevision;
    string? queueIssue,smartReportIdentity;TextBlock? queueWarning;Button? queueApply;CheckBox? settingsAutomaticRefresh;
    DateTime? inventoryUpdated;
    void InitializeDiskRefresh(StackPanel actions)
    {
        automaticRefresh.IsChecked=Preferences.Current.AutoRefreshDisks;
        automaticRefresh.ToolTip=$"Check connected disks every {Preferences.Current.DiskRefreshSeconds} seconds. Pauses during operations and dialogs.";
        automaticRefresh.Click+=async(_,_)=>
        {
            automaticRefresh.IsEnabled=false;
            try{await Preferences.SaveAsync(Preferences.Current with{AutoRefreshDisks=automaticRefresh.IsChecked==true});ConfigureDiskRefresh();if(settingsAutomaticRefresh!=null)settingsAutomaticRefresh.IsChecked=Preferences.Current.AutoRefreshDisks;}
            catch(Exception ex){automaticRefresh.IsChecked=Preferences.Current.AutoRefreshDisks;Error(ex);}
            finally{automaticRefresh.IsEnabled=true;}
        };
        actions.Children.Add(automaticRefresh);
        diskRefreshTimer.Tick+=async(_,_)=>await RefreshAutomaticallyAsync();
        Loaded+=(_,_)=>ConfigureDiskRefresh();
        Closed+=(_,_)=>{windowClosed=true;diskRefreshTimer.Stop();inventoryLifetime.Cancel();StopActivityTimer();};
    }
    void ConfigureDiskRefresh()
    {
        diskRefreshTimer.Stop();diskRefreshTimer.Interval=TimeSpan.FromSeconds(Preferences.Current.DiskRefreshSeconds);
        if(!demo&&!windowClosed&&Preferences.Current.AutoRefreshDisks)diskRefreshTimer.Start();
    }
    bool CanRefreshAutomatically=>!demo&&!windowClosed&&!busy&&!inventoryRefreshing&&!partitionMenuOpen&&IsEnabled&&OwnedWindows.Count==0;
    async Task RefreshAutomaticallyAsync()
    {
        if(!Preferences.Current.AutoRefreshDisks||!CanRefreshAutomatically)return;
        inventoryRefreshing=true;long revision=inventoryRevision;
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(inventoryLifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(90));
            var latest=await StorageService.InventoryAsync(timeout.Token);
            // A result read before a write or queue edit must not replace newer state.
            if(windowClosed||busy||revision!=inventoryRevision||partitionMenuOpen||!IsEnabled||OwnedWindows.Count>0||!Preferences.Current.AutoRefreshDisks)return;
            AcceptInventory(latest);status.Text=InventoryStatus();
        }
        catch(OperationCanceledException){if(!windowClosed)status.Text="Automatic disk refresh timed out. Use Refresh to try again.";}
        catch(Exception ex){if(!windowClosed)status.Text="Automatic disk refresh failed: "+ex.Message;}
        finally{inventoryRefreshing=false;}
    }
    async Task RefreshAsync()
    {
        if(busy)return;
        if(inventoryRefreshing){status.Text="Disk refresh is already in progress.";return;}
        inventoryRefreshing=true;bool refreshed=false;
        try{await Work("Refreshing storage inventory",async()=>
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(inventoryLifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(90));
            AcceptInventory(await StorageService.InventoryAsync(timeout.Token));refreshed=true;
        });if(!windowClosed&&refreshed)status.Text=InventoryStatus();}
        finally{inventoryRefreshing=false;}
    }
    string InventoryStatus()=>queueIssue??$"{disks.Count} disk(s) · Refreshed {inventoryUpdated:HH:mm:ss}";
    void AcceptInventory(List<DiskInfo> latest)
    {
        if(windowClosed)return;
        // Preserve the reviewed baselines. A later reconnect never rearms a stale queue.
        if(queue.Count>0)queueIssue??=DiskRefreshPolicy.QueueProblem(latest,queueBaselines.Values);
        disks=latest;inventoryUpdated=DateTime.Now;
        if(page=="Overview")Navigate(page);
        else{foreach(var update in inventoryObservers.ToArray())update();UpdateQueueWarning();}
        if(smartReport!=null&&!demo&&!disks.Any(d=>d.Identity==smartReportIdentity))smartReport=null;
    }
    List<DiskInfo> ProjectedDisks()=>queueIssue==null?QueuePlanner.Project(disks,queue):disks;
    void RequireCurrentQueue()
    {
        if(queueIssue!=null)throw new IOException(queueIssue);
        if(queue.Count>0&&DiskRefreshPolicy.QueueProblem(disks,queueBaselines.Values) is string issue){queueIssue=issue;UpdateQueueWarning();throw new IOException(issue);}
    }
    void UpdateQueueWarning()
    {
        if(queueWarning!=null){queueWarning.Text=queueIssue??"";queueWarning.Visibility=queueIssue==null?Visibility.Collapsed:Visibility.Visible;}
        if(queueIssue!=null)ResetApplyConfirmation();
        if(queueApply!=null)queueApply.IsEnabled=queue.Count>0&&queueIssue==null;
    }
    ComboBox VolumePicker()
    {
        static List<string> ReadyVolumes()
        {
            var result=new List<string>();foreach(var drive in DriveInfo.GetDrives())try{if(drive.IsReady&&drive.DriveType is DriveType.Fixed or DriveType.Removable)result.Add(drive.RootDirectory.FullName);}catch(IOException){}catch(UnauthorizedAccessException){}return result;
        }
        var items=new ObservableCollection<string>(ReadyVolumes());var combo=new ComboBox{ItemsSource=items};if(items.Count>0)combo.SelectedIndex=0;
        inventoryObservers.Add(()=>
        {
            var latest=ReadyVolumes();for(int i=items.Count-1;i>=0;i--)if(!latest.Contains(items[i]))items.RemoveAt(i);
            foreach(var volume in latest)if(!items.Contains(volume))items.Add(volume);
        });return combo;
    }
}
