using System.IO;
using System.Windows;
using System.Windows.Controls;
using SoopaDriveTool.Core;
using SoopaDriveTool.Windows;

partial class Program
{
    // Call inside the existing STA/Application fixture. No disk work is invoked.
    static void ConfirmationUiChecks(List<MainWindow> windows)
    {
        var disk=Disk() with{Name="Confirmation fixture",NativeIdentity="confirmation-native"};
        var window=MakeWindow("Disk manager",[disk]);windows.Add(window);
        var operation=DiskOperation.For(disk,OperationKind.SetLabel,disk.Partitions[0]) with{Value="Reviewed"};
        Call(window,"Queue",operation,disk,null);
        var apply=GetField<Button>(window,"queueApply");
        Check(Equals(apply.Content,"Apply")&&apply.Visibility==Visibility.Visible&&!apply.IsDefault,"Queue starts with an explicit Apply button that is not the Enter default");
        Call(window,"ArmApplyConfirmation");
        var confirmation=GetField<Border>(window,"applyConfirmation");var no=GetField<Button>(window,"applyNo");var yes=GetField<Button>(window,"applyYes");
        Check(confirmation.Visibility==Visibility.Visible&&apply.Visibility==Visibility.Collapsed,"Apply changes to the inline Are you sure confirmation");
        Check(no.IsDefault&&!yes.IsDefault&&Equals(no.Content,"No")&&Equals(yes.Content,"Yes"),"No is the confirmation default and Yes requires a deliberate action");
        Check(Descendants(confirmation).OfType<TextBlock>().Any(t=>t.Text=="Are you sure?")&&!Descendants(confirmation).OfType<TextBox>().Any()&&!Descendants(confirmation).OfType<CheckBox>().Any(),"Queue confirmation has no typed phrase or extra acknowledgment checkbox");
        Check(GetField<TextBlock>(window,"applySummary").Text.Contains(operation.Description,StringComparison.Ordinal),"Confirmation identifies the reviewed operation and destination");
        var frozen=GetField<Dictionary<int,DiskInfo>>(window,"reviewedQueueBaselines");
        Check(!ReferenceEquals(frozen[disk.Number].Partitions,disk.Partitions)&&(bool)Call(window,"ReviewedQueueIsCurrent")!,"Confirmation freezes the reviewed partition list and recognizes the unchanged queue");
        no.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(GetField<object?>(window,"reviewedQueue")==null&&confirmation.Visibility==Visibility.Collapsed&&apply.Visibility==Visibility.Visible&&!no.IsDefault,"No cancels confirmation and restores Apply without keeping a hidden default");
        Check(GetField<List<DiskOperation>>(window,"queue").Count==1&&GetField<RunActivity?>(window,"activeRun")==null,"Declining preserves pending operations and never starts activity");

        Call(window,"ArmApplyConfirmation");
        GetField<List<DiskOperation>>(window,"queue")[0]=operation with{Value="Changed after review"};
        Check(!(bool)Call(window,"ReviewedQueueIsCurrent")!,"Changing a queued action invalidates its confirmation");
        Reject(()=>((Task)Call(window,"Apply")!).GetAwaiter().GetResult(),"A changed chain cannot pass the Yes execution gate");
        Check(GetField<RunActivity?>(window,"activeRun")==null&&GetField<object?>(window,"reviewedQueue")==null,"Rejected confirmation resets before any disk work begins");

        // Give the hidden owner a native handle without Show() or Loaded.
        new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
        var dialog=(Window)Call(window,"CreateConfirmationDialog","Erase selected partition","Disk 2 / partition 1 will be overwritten.")!;
        try
        {
            var answers=Descendants(dialog).OfType<Button>().ToList();
            Check(answers.Single(b=>Equals(b.Content,"No")).IsDefault&&answers.Single(b=>Equals(b.Content,"No")).IsCancel&&!answers.Single(b=>Equals(b.Content,"Yes")).IsDefault,"Direct action dialog defaults to No and Escape cancels");
            Check(!Descendants(dialog).OfType<TextBox>().Any()&&Descendants(dialog).OfType<TextBlock>().Any(t=>t.Text.Contains("Disk 2 / partition 1",StringComparison.Ordinal)),"Direct confirmation names its affected partition without a typing field");
        }
        finally{dialog.Close();}
    }
}
