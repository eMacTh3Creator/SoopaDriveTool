using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public partial class MainWindow
{
    DiskOperation[]? reviewedQueue;
    Dictionary<int,DiskInfo>? reviewedQueueBaselines;
    Border? applyConfirmation;
    Button? applyNo,applyYes;
    TextBlock? applySummary;
    void QueuePanel()
    {
        ResetApplyConfirmation();
        PreviewKeyDown-=CancelApplyConfirmation;
        PreviewKeyDown+=CancelApplyConfirmation;
        var panel=Stack(T($"Pending operations ({queue.Count})",16,Ink,true));
        queueWarning=T("",12,B("#B34545"),false,new Thickness(0,12,0,12));panel.Children.Add(queueWarning);
        if(queue.Count==0)panel.Children.Add(T("Your queue is empty. Disk changes appear here before they run.",12,Muted,false,new Thickness(0,12,0,16)));
        foreach(var item in queue.Select((operation,index)=>(operation,index)))
        {
            var row=new DockPanel();var remove=Btn("Remove from here",()=>
            {
                ResetApplyConfirmation();queue.RemoveRange(item.index,queue.Count-item.index);
                if(queue.Count==0){queueBaselines.Clear();queueIssue=null;}
                Navigate(page);
            });
            DockPanel.SetDock(remove,Dock.Right);row.Children.Add(remove);
            row.Children.Add(T($"{item.index+1}. {item.operation.Description}",13,Ink,false,new Thickness(0,12,0,12)));panel.Children.Add(row);
        }
        var actions=new WrapPanel();
        queueApply=Btn(StorageService.IsAdmin?"Apply":"Restart as administrator to apply",async()=>
        {
            if(!StorageService.IsAdmin){await ElevateWithPlan();return;}
            ArmApplyConfirmation();
        },true);
        actions.Children.Add(queueApply);
        actions.Children.Add(Btn("Clear queue",()=>{ResetApplyConfirmation();queue.Clear();queueBaselines.Clear();queueIssue=null;Navigate(page);status.Text="Queue cleared.";}));
        panel.Children.Add(actions);
        applySummary=T("",12,Muted,false,new Thickness(0,8,0,12));
        applyNo=Btn("No",()=>{ResetApplyConfirmation();status.Text="Changes were not applied.";});
        applyYes=Btn("Yes",Apply,true);
        var answers=new WrapPanel();answers.Children.Add(applyNo);answers.Children.Add(applyYes);
        applyConfirmation=Card(Stack(T("Are you sure?",20,Ink,true),applySummary,T("These changes affect disk data. Earlier changes remain applied if a later step fails.",12,Muted,false,new Thickness(0,0,0,12)),answers),new Thickness(0,8,0,12));
        applyConfirmation.Visibility=Visibility.Collapsed;panel.Children.Add(applyConfirmation);
        panel.Children.Add(T("Queued operations can be cleared. Applied operations cannot be undone automatically.",11,Muted));
        UpdateQueueWarning();body.Children.Add(Card(panel));
    }
    void ArmApplyConfirmation()
    {
        if(busy||queue.Count==0)return;
        RequireCurrentQueue();
        reviewedQueue=queue.ToArray();
        reviewedQueueBaselines=queueBaselines.ToDictionary(pair=>pair.Key,pair=>pair.Value with{Partitions=pair.Value.Partitions.ToList()});
        if(applyConfirmation==null||applySummary==null||applyNo==null||queueApply==null)throw new IOException("The operation review is unavailable. Return to the disk manager and review the queue.");
        applySummary.Text=string.Join("\n",reviewedQueue.Select((operation,index)=>$"{index+1}. {operation.Description}"));
        queueApply.Visibility=Visibility.Collapsed;
        applyConfirmation.Visibility=Visibility.Visible;
        applyNo.IsDefault=true;
        applyNo.Focus();Keyboard.Focus(applyNo);
        Dispatcher.BeginInvoke(new Action(()=>{if(reviewedQueue!=null&&applyNo!=null){applyNo.Focus();Keyboard.Focus(applyNo);}}),DispatcherPriority.Input);
        status.Text="Review the operations, then choose Yes or No. No is selected by default.";
    }
    void ResetApplyConfirmation()
    {
        reviewedQueue=null;reviewedQueueBaselines=null;
        if(applyNo!=null)applyNo.IsDefault=false;
        if(applyConfirmation!=null)applyConfirmation.Visibility=Visibility.Collapsed;
        if(queueApply!=null)queueApply.Visibility=Visibility.Visible;
    }
    void CancelApplyConfirmation(object sender,KeyEventArgs e)
    {
        if(e.Key!=Key.Escape||reviewedQueue==null)return;
        ResetApplyConfirmation();status.Text="Changes were not applied.";e.Handled=true;
    }
    bool ReviewedQueueIsCurrent()=>reviewedQueue!=null&&reviewedQueueBaselines!=null&&queue.SequenceEqual(reviewedQueue)&&
        queueBaselines.Count==reviewedQueueBaselines.Count&&queueBaselines.All(pair=>reviewedQueueBaselines.TryGetValue(pair.Key,out var original)&&original.Identity==pair.Value.Identity&&original.Layout==pair.Value.Layout)&&
        queueIssue==null&&DiskRefreshPolicy.QueueProblem(disks,reviewedQueueBaselines.Values)==null;
    async Task Apply()
    {
        if(busy||reviewedQueue==null)return;
        if(!ReviewedQueueIsCurrent())
        {
            ResetApplyConfirmation();RequireCurrentQueue();
            throw new IOException("The reviewed operations or selected disks changed. Review the queue and press Apply again.");
        }
        var pending=reviewedQueue;var baselines=reviewedQueueBaselines!;
        ResetApplyConfirmation();
        await Work("Applying disk operations",async()=>
        {
            activeRun=new RunActivity{Steps=pending.Select(operation=>new StepActivity{Description=operation.Description}).ToList()};Navigate("Activity");body.IsEnabled=true;StartActivityTimer();
            try{await QueueRunner.RunAsync(pending,baselines,activeRun,_=>RefreshRunDisplay());}
            finally
            {
                queue.Clear();queueBaselines.Clear();queueIssue=null;disks=await StorageService.InventoryAsync();StopActivityTimer();RefreshRunDisplay();
            }
        });
    }
}
