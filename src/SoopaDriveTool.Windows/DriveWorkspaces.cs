using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public partial class MainWindow
{
    readonly DispatcherTimer activityTimer=new(){Interval=TimeSpan.FromSeconds(12)};
    TextBlock? runHeading,runStep,runMessage,runElapsed,runFact;ProgressBar? overallBar,stepBar;StackPanel? runSteps,runEvents;
    async Task ElevateWithPlan()
    {
        if(busy)throw new IOException("Finish or stop the current operation before restarting as administrator.");
        string? file=null;
        if(queue.Count>0){file=Path.Combine(Data.Home,"pending",Guid.NewGuid()+".json");await Data.SaveAsync(file,new PendingPlan(queueBaselines.Values.ToList(),queue.ToList()));}
        try{StorageService.Elevate(file);Close();}catch{if(file!=null&&File.Exists(file))File.Delete(file);throw;}
    }
    void PrepareBatch(Action prepare)
    {
        int count=queue.Count;var baseline=queueBaselines.ToDictionary(x=>x.Key,x=>x.Value);
        try{prepare();}catch{if(queue.Count>count)queue.RemoveRange(count,queue.Count-count);queueBaselines.Clear();foreach(var pair in baseline)queueBaselines[pair.Key]=pair.Value;Navigate(page);throw;}
    }
    void Clone()
    {
        Note("Byte-exact copies include every sector. Filesystem-aware and Windows-aware copies use verified WIM capture/apply, so unused sectors are skipped and smaller destinations can fit. Live Windows mode reads the running system through VSS; all destination writes remain subject to protection and identity checks.");
        int tabStart=body.Children.Count;
        var source=DiskPicker(false,"source");var target=DiskPicker(true,"target");var partition=new ComboBox{DisplayMemberPath="Display"};
        var mode=Choices("Byte-exact whole disk","Filesystem-aware NTFS volume","Windows-aware UEFI (inactive installation)","Live Windows UEFI (consistent VSS snapshot)","All sectors to larger disk (relocate GPT)");
        var workspace=new TextBox{Text=Path.GetTempPath(),IsReadOnly=true};var help=T("",13,Muted);var p=Stack(T("Clone a drive",21,Ink,true),T("Clone method",12,Muted,false,new Thickness(0,18,0,0)),mode,help,T("Source disk",12,Muted,false,new Thickness(0,18,0,0)),source,T("Source volume (filesystem / Windows modes)",12,Muted),partition,T("Destination disk · all destination data is replaced",12,Muted),target,T("WIM scratch folder (separate from destination; live mode can use source free space)",12,Muted),workspace,Btn("Choose scratch folder…",()=>workspace.Text=PickFolder("Choose scratch storage, separate from source and destination")??workspace.Text));
        void Update(){partition.ItemsSource=(source.SelectedItem as DiskInfo)?.Partitions.Where(x=>(!x.Protected||mode.SelectedIndex==3&&x.IsBoot)&&x.FileSystem=="NTFS").ToList();if(partition.Items.Count>0)partition.SelectedIndex=0;}
        source.SelectionChanged+=(_,_)=>Update();Update();mode.SelectionChanged+=(_,_)=>{Update();Describe();};Describe();
        void Describe(){bool usedFiles=mode.SelectedIndex is 1 or 2 or 3;partition.IsEnabled=usedFiles;workspace.IsEnabled=usedFiles;help.Text=mode.SelectedIndex switch{4=>"Copies and verifies every source sector to an equal or larger destination, then rebuilds the GPT backup at the new disk boundary. Extra capacity remains unallocated. Disconnect the source before booting its clone.",0=>"Exact source-size and sector-size match required. Both disks are taken offline by visible queued steps. All copied sectors are read back and hash-verified.",1=>"Copies one mounted, unencrypted NTFS volume into a fresh GPT/NTFS destination using DISM. The source is temporarily read-only. This is a used-file clone, not an exact copy of free space.",3=>"Reads the running Windows volume through a writer-verified VSS snapshot. The source remains online and writable. Builds a bootable UEFI destination with fresh identifiers; all destination data is replaced.",_=>"Select an inactive Windows installation on an attached data disk. Rebuilds GPT + EFI + MSR + Windows, copies WinRE when available, and configures destination BCD without changing host firmware order. Independent boot testing is required for each hardware configuration."};}
        p.Children.Add(Btn("Add clone to the action chain",()=>PrepareBatch(()=>
        {
            if(source.SelectedItem is not DiskInfo s||target.SelectedItem is not DiskInfo t)throw new IOException("Select source and destination data disks.");if(s.Number==t.Number)throw new IOException("Choose a different destination.");
            var selected=(CloneMode)mode.SelectedIndex;
            if(selected is CloneMode.ByteExact or CloneMode.ByteExactGrow)
            {
                if(!s.Offline)AddWithoutNavigate(DiskOperation.For(s,OperationKind.Offline));
                if(!t.Offline)AddWithoutNavigate(DiskOperation.For(t,OperationKind.Offline));
                var projection=QueuePlanner.Project(disks,queue);s=projection.Single(d=>d.Number==s.Number);t=projection.Single(d=>d.Number==t.Number);
            }
            var part=selected is CloneMode.ByteExact or CloneMode.ByteExactGrow?null:partition.SelectedItem as PartitionInfo;
            if(selected is not (CloneMode.ByteExact or CloneMode.ByteExactGrow)&&part==null)throw new IOException("Select a mounted NTFS source volume.");
            var op=DiskOperation.For(s,OperationKind.CloneDisk,part) with{CloneMode=selected,TargetDiskNumber=t.Number,TargetIdentity=t.Identity,TargetLayout=t.Layout,Workspace=workspace.Text};Queue(op,s,t);
        }),true));body.Children.Add(Card(p));
        var disk=DiskPicker(true,"image");var image=Stack(T("Disk images",20,Ink,true),T("Selected data disk",12,Muted,false,new Thickness(0,16,0,0)),disk);var actions=new WrapPanel();
        actions.Children.Add(Btn("Back up to raw image…",()=>{if(disk.SelectedItem is not DiskInfo d)throw new IOException("Select a data disk.");var save=new SaveFileDialog{Filter="SoopaDriveTool raw image|*.sdi",FileName="disk-"+d.Number+".sdi"};if(save.ShowDialog(this)!=true)return;if(!d.Offline){AddWithoutNavigate(DiskOperation.For(d,OperationKind.Offline));d=QueuePlanner.Project(disks,queue).Single(x=>x.Number==d.Number);}Queue(DiskOperation.For(d,OperationKind.BackupImage) with{Value=save.FileName},d);}));
        actions.Children.Add(Btn("Restore raw image…",()=>{if(disk.SelectedItem is not DiskInfo d)throw new IOException("Select a data disk.");var file=PickFile("Raw image|*.sdi");if(file==null)return;if(!d.Offline){AddWithoutNavigate(DiskOperation.For(d,OperationKind.Offline));d=QueuePlanner.Project(disks,queue).Single(x=>x.Number==d.Number);}Queue(DiskOperation.For(d,OperationKind.RestoreImage) with{Value=file},d);}));image.Children.Add(actions);body.Children.Add(Card(image));
        var wipeDisk=DiskPicker(true,"wipe");var wipe=Choices("Quick partition reset · data remains recoverable","Thorough zero overwrite + full read-back","Thorough random overwrite + full read-back","Three-pass random / ones / zeros · verify every pass");
        body.Children.Add(Card(Stack(T("Wipe a drive",20,Ink,true),T("Wipe method",12,Muted,false,new Thickness(0,16,0,0)),wipe,T("Data disk to erase",12,Muted),wipeDisk,T("Quick reset removes partition metadata only. Overwrites cover user-addressable sectors. Neither is a firmware sanitize command or a guarantee for remapped/overprovisioned SSD cells.",12,Muted,false,new Thickness(0,0,0,16)),Btn("Add wipe to the action chain",()=>PrepareBatch(()=>
        {
            if(wipeDisk.SelectedItem is not DiskInfo d)throw new IOException("Select a data disk.");bool quick=wipe.SelectedIndex==0;
            if(quick&&d.Offline){AddWithoutNavigate(DiskOperation.For(d,OperationKind.Online));d=QueuePlanner.Project(disks,queue).Single(x=>x.Number==d.Number);}
            if(!quick&&!d.Offline){AddWithoutNavigate(DiskOperation.For(d,OperationKind.Offline));d=QueuePlanner.Project(disks,queue).Single(x=>x.Number==d.Number);}
            Queue(DiskOperation.For(d,quick?OperationKind.QuickWipeDisk:OperationKind.WipeDisk) with{WipeMode=wipe.SelectedIndex switch{2=>WipeMode.Random,3=>WipeMode.ThreePass,_=>WipeMode.Zero}},d);
        }),true))));ProtectedImages();TabWorkspace(tabStart,("Clone",1),("Raw images",1),("Wipe drive",1),("Protected images",1));QueuePanel();
    }
    void AddWithoutNavigate(DiskOperation op)
    {
        RequireCurrentQueue();var selected=ProjectedDisks().SingleOrDefault(d=>d.Number==op.DiskNumber);
        if(selected==null||selected.Identity!=op.Identity||selected.Layout!=op.Layout)throw new IOException("The selected disk changed or disconnected. Select it again before adding operations.");
        ResetApplyConfirmation();inventoryRevision++;QueuePlanner.Project(disks,queue.Append(op).ToList());
        if(!queueBaselines.ContainsKey(op.DiskNumber))queueBaselines[op.DiskNumber]=disks.Single(d=>d.Number==op.DiskNumber);queue.Add(op);
    }
    void Health()
    {
        Note("Full SMART means all attributes, counters and logs the drive and its connection expose. USB bridges, RAID controllers and VMs can hide data; unavailable information is never treated as a health pass. Collection does not start self-tests or change firmware settings.");
        var disk=DiskPicker();var transport=Choices("auto","sat","sat,12","sat,16","scsi","nvme","sntasmedia","sntjmicron","sntrealtek");var output=new StackPanel();var controls=Stack(T("SMART & drive condition report",21,Ink,true),T("Select drive",12,Muted,false,new Thickness(0,18,0,0)),disk,T("Transport override (normally auto)",12,Muted),transport,Btn("Read full SMART and logs",async()=>{if(disk.SelectedItem is not DiskInfo d)throw new IOException("Select a drive.");await Work("Reading device SMART attributes and logs",async()=>{smartReport=await SmartService.ReadAsync(d,(string)transport.SelectedItem);smartReportIdentity=d.Identity;Draw();});},true));body.Children.Add(Card(controls));body.Children.Add(output);disk.SelectionChanged+=(_,_)=>{if((disk.SelectedItem as DiskInfo)?.Identity!=smartReportIdentity){smartReport=null;Draw();}};if(!demo&&(disk.SelectedItem as DiskInfo)?.Identity!=smartReportIdentity)smartReport=null;Draw();int diagnosticStart=body.Children.Count;DiagnosticActions(disk,transport);TabWorkspace(diagnosticStart,("Tests & temperature",1),("Surface scan",1),("Device erase",1),("Free-space wipe",1),("Topology",1));
        void Draw()
        {
            output.Children.Clear();if(smartReport==null)return;var r=smartReport;var p=Stack(T($"{r.Model} · {r.Overall}",21,r.Overall=="FAILING"?B("#B34545"):Ink,true),T($"Serial: {r.Serial} · Firmware: {r.Firmware} · {r.Protocol} · Collected {r.CollectedUtc:u}",12,Muted,false,new Thickness(0,12,0,14)),T(r.Coverage,13,Muted));
            foreach(var warning in r.Warnings)p.Children.Add(T("⚠ "+warning,12,B("#9C6C20"),false,new Thickness(0,10,0,0)));
            var export=new WrapPanel{Margin=new Thickness(0,20,0,12)};foreach(var format in new[]{"JSON","HTML","CSV","Text"})export.Children.Add(Btn("Export "+format,()=>ExportSmart(format)));p.Children.Add(export);
            if(r.Attributes.Count>0)p.Children.Add(Table(r.Attributes,("ID","Id"),("Attribute","Name"),("Value","Current"),("Worst","Worst"),("Threshold","Threshold"),("Raw","Raw"),("Failed","Failed")));
            p.Children.Add(T("All fields (NVMe, ATA, SCSI and available logs)",16,Ink,true,new Thickness(0,12,0,12)));p.Children.Add(Table(r.Fields,("Section","Section"),("Field","Name"),("Value","Value")));p.Children.Add(new TextBox{Text=r.OriginalText,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=300,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});output.Children.Add(Card(p));
        }
    }
    void ExportSmart(string format)
    {
        if(smartReport==null)return;string extension=format=="Text"?"txt":format.ToLowerInvariant();var d=new SaveFileDialog{FileName="SoopaDriveTool-SMART-disk-"+smartReport.WindowsDiskNumber,Filter=format+" report|*."+extension};if(d.ShowDialog(this)!=true)return;
        var r=smartReport;File.WriteAllText(d.FileName,format switch{"JSON"=>JsonSerializer.Serialize(r,Data.Json),"HTML"=>r.Html(),"CSV"=>r.Csv(),_=>$"SoopaDriveTool drive report · UTC {r.CollectedUtc:O}\nDisk {r.WindowsDiskNumber} · {r.Model}\nSerial: {r.Serial}\nOverall: {r.Overall}\n{string.Join("\n",r.Warnings)}\n\n{r.OriginalText}\n\nFull fields:\n"+string.Join("\n",r.Fields.Select(f=>f.Name+": "+f.Value))});
    }
    void StartActivityTimer(){activityTimer.Tick-=ActivityTick;activityTimer.Tick+=ActivityTick;activityTimer.Start();}
    void StopActivityTimer()=>activityTimer.Stop();
    void ActivityTick(object? sender,EventArgs e){if(runFact!=null&&Preferences.Current.WaitingFacts)runFact.Text=RunActivity.WaitingNotes[Random.Shared.Next(RunActivity.WaitingNotes.Length)];RefreshRunDisplay();}
    void Activity()
    {
        Note("Live step progress and the remaining chain are shown here. A missing device percentage is shown as indeterminate. Completed steps are verified before dependent steps begin. Activity is saved locally for export.");
        if(activeRun!=null)
        {
            runHeading=T("",21,Ink,true);runStep=T("",15,Accent,true,new Thickness(0,16,0,10));runMessage=T("",13,Muted);runElapsed=T("",12,Muted,false,new Thickness(0,12,0,12));runFact=T(Preferences.Current.WaitingFacts?RunActivity.WaitingNotes[0]:"",12,Muted,false,new Thickness(0,18,0,0));
            overallBar=new ProgressBar{Height=10,Minimum=0,Maximum=100,Margin=new Thickness(0,14,0,6)};stepBar=new ProgressBar{Height=7,Minimum=0,Maximum=100,Margin=new Thickness(0,12,0,10)};runSteps=new StackPanel();runEvents=new StackPanel();
            body.Children.Add(Card(Stack(runHeading,overallBar,runStep,stepBar,runMessage,runElapsed,runFact)));body.Children.Add(Card(Stack(T("Steps · completed / current / next",17,Ink,true),runSteps)));body.Children.Add(Card(Stack(T("Detailed event log",17,Ink,true),runEvents,Btn("Export this run…",()=>{var d=new SaveFileDialog{Filter="JSON activity report|*.json",FileName="SoopaDriveTool-run-"+activeRun.Id};if(d.ShowDialog(this)==true)File.WriteAllText(d.FileName,JsonSerializer.Serialize(activeRun,Data.Json));}))));RefreshRunDisplay();
        }
        var dir=Path.Combine(Data.Home,"activity");var files=Directory.Exists(dir)?Directory.GetFiles(dir,"*.json").OrderDescending().Take(30).ToArray():[];
        if(files.Length==0&&activeRun==null)body.Children.Add(Card(T("No drive-action chains have run yet.",15,Muted)));
        foreach(var file in files)
        {
            if(activeRun!=null&&file.Contains(activeRun.Id.ToString(),StringComparison.OrdinalIgnoreCase))continue;
            var expander=new Expander{Header=Path.GetFileName(file),Content=new TextBox{Text=File.ReadAllText(file),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=300,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};body.Children.Add(Card(expander));
        }
    }
    void RefreshRunDisplay()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(RefreshRunDisplay);return;}if(page!="Activity"||activeRun==null||runHeading==null)return;var r=activeRun;var index=Math.Min(r.CurrentStep,r.Steps.Count-1);if(index<0)return;var step=r.Steps[index];
        runHeading.Text=$"{r.State} · {r.OverallPercent:0.0}% of reviewed steps";overallBar!.Value=r.OverallPercent;runStep!.Text=$"Step {index+1} / {r.Steps.Count} · {step.Phase}";stepBar!.IsIndeterminate=step.State=="Running"&&!step.Percent.HasValue;stepBar.Value=step.Percent??0;runMessage!.Text=step.Message;runElapsed!.Text=$"Elapsed {((r.CompletedUtc??DateTime.UtcNow)-r.StartedUtc):hh\\:mm\\:ss}"+(step.Percent.HasValue?$" · Current step {step.Percent:0.0}%":" · Windows has not supplied a percentage for this step.");
        runSteps!.Children.Clear();for(int i=0;i<r.Steps.Count;i++){var s=r.Steps[i];runSteps.Children.Add(T($"{i+1}. [{s.State}] {s.Description}",13,s.State=="Failed"?B("#B34545"):s.State=="Running"?Accent:Muted,s.State=="Running",new Thickness(0,12,0,4)));}
        runEvents!.Children.Clear();foreach(var item in r.Events.TakeLast(35))runEvents.Children.Add(T($"{item.Utc:HH:mm:ss} UTC · Step {item.Step+1} · {item.Phase}"+(item.Percent.HasValue?$" · {item.Percent:0.0}%":"")+" · "+item.Message,12,Muted,false,new Thickness(0,9,0,0)));
    }
}
