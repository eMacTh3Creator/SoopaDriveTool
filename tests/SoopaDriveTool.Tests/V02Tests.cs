using System.IO;
using System.Text.Json;
using SoopaDriveTool.Core;
using SoopaDriveTool.Windows;
partial class Program
{
    static void V02CoreTests()
    {
        var d=Disk() with{Partitions=[new(){Number=1,Offset=1024*1024,Size=64L*1024*1024,FileSystem="NTFS",Label="A",Letter="S",Encryption="None"},new(){Number=2,Offset=65L*1024*1024,Size=64L*1024*1024,FileSystem="NTFS",Label="B",Letter="T",Encryption="None"}]};
        var ops=new List<DiskOperation>{DiskOperation.For(d,OperationKind.DeletePartition,d.Partitions[0]),DiskOperation.For(d,OperationKind.DeletePartition,d.Partitions[1]),DiskOperation.For(d,OperationKind.CreatePartition) with{Bytes=128L*1024*1024,Value="New"}};
        var projected=QueuePlanner.Project([d],ops).Single();var pending=projected.Partitions.Single(p=>p.Number<0);Check(pending.Number==QueuePlanner.VirtualNumber(2)&&pending.Offset==16L*1024*1024,"Queue creates a virtual partition after two deletions");
        ops.Add(DiskOperation.For(projected,OperationKind.SetLabel,pending) with{Value="Resale"});Check(QueuePlanner.Project([d],ops).Single().Partitions.Single(p=>!p.Protected).Label=="Resale","Dependent rename targets the newly created partition");
        Check(d.Partitions.Count==2&&d.Partitions[0].Label=="A","Queue projection does not mutate the real baseline");
        Reject(()=>QueuePlanner.Project([d],[DiskOperation.For(d,OperationKind.CreatePartition) with{Bytes=d.Size,Value="TooLarge"}]),"Planner rejects an overlapping/out-of-capacity allocation");
        Reject(()=>QueuePlanner.AssertUnchanged(d,d with{Partitions=[d.Partitions[0] with{Label="External"},d.Partitions[1]]}),"External rename is detected between steps");
        var after=d with{Partitions=[d.Partitions[1]]};QueuePlanner.VerifyTransition(d,after,ops[0]);Check(true,"Deletion transition verifies unaffected partitions");
        Reject(()=>QueuePlanner.VerifyTransition(d,after with{Partitions=[d.Partitions[1] with{Size=10}]},ops[0]),"Unexpected unrelated geometry change stops a chain");
        var target=d with{Number=9,UniqueId="other",Size=256L*1024*1024,Partitions=[]};
        var c=DiskOperation.For(d,OperationKind.CloneDisk,d.Partitions[0]) with{CloneMode=CloneMode.FileSystem,TargetDiskNumber=9,TargetIdentity=target.Identity,TargetLayout=target.Layout};OperationPolicy.Validate(c,d,target);Check(true,"Filesystem-aware clone permits a smaller destination with sufficient used capacity");
        Reject(()=>OperationPolicy.Validate(c with{CloneMode=CloneMode.ByteExact},d,target),"Byte-exact mode does not silently become a smaller-target clone");
        Reject(()=>OperationPolicy.Validate(c,d with{IsSystem=true},target),"Filesystem-aware cloning blocks the running system source");
        OperationPolicy.Validate(DiskOperation.For(d,OperationKind.QuickWipeDisk),d);Check(true,"Quick metadata reset can be queued for an online data disk");
        var quick=DiskOperation.For(d,OperationKind.QuickWipeDisk);var cleared=QueuePlanner.Project([d],[quick]).Single();Check(cleared.Style=="RAW"&&cleared.Partitions.Count==0,"Quick reset projection removes layout without claiming secure erasure");
        const string ata="""{"model_name":"Test ATA","serial_number":"ATA1","firmware_version":"1","device":{"protocol":"ATA"},"smart_status":{"passed":true},"ata_smart_attributes":{"table":[{"id":5,"name":"Reallocated_Sector_Ct","value":100,"worst":100,"thresh":10,"raw":{"value":7,"string":"7"},"when_failed":""}]},"ata_smart_error_log":{"summary":{"count":3}},"smartctl":{"output":["Full original text"]}}""";
        var report=SmartReport.Parse(ata,64,2,"ATA1");Check(report.Attributes.Single().Raw=="7"&&report.Overall=="Warnings present","ATA raw attributes and nonzero health bitfields are retained");Check(report.Fields.Any(f=>f.Name=="ata_smart_error_log.summary.count"&&f.Value=="3"),"Detailed ATA error-log fields are preserved");
        Check(report.OriginalText=="Full original text"&&report.Raw.GetProperty("serial_number").GetString()=="ATA1","SMART export retains original text and complete JSON");
        const string nvme="""{"model_name":"NVMe <script>alert(1)</script>","device":{"protocol":"NVMe"},"smart_status":{"passed":false},"nvme_smart_health_information_log":{"critical_warning":1,"temperature":70,"percentage_used":95,"data_units_written":123456,"unsafe_shutdowns":8,"media_errors":2},"nvme_error_information_log":{"size":2},"smartctl":{"output":[]}}""";
        var n=SmartReport.Parse(nvme,8,3);Check(n.Overall=="FAILING"&&n.Fields.Any(f=>f.Name.EndsWith("media_errors")&&f.Value=="2"),"Failing NVMe media/wear counters are exported without suppressing health failures");Check(!n.Html().Contains("<script>")&&n.Html().Contains("&lt;script&gt;"),"Printable SMART HTML escapes device-supplied content");
        var unavailable=SmartReport.Parse("{\"smartctl\":{\"messages\":[]}}",2,0);Check(unavailable.Overall=="Unavailable","Unsupported VM/USB SMART is never reported as healthy");
        Check(SmartReport.Parse(ata,4,2).Overall=="Partial report","Unsupported commands produce an explicitly partial report");
        var scsi=SmartReport.Parse("{\"device\":{\"protocol\":\"SCSI\"},\"smart_status\":{\"passed\":true},\"scsi_grown_defect_list\":12,\"scsi_error_counter_log\":{\"read\":{\"total_uncorrected_errors\":4}}}",0,4);Check(scsi.Fields.Any(f=>f.Value=="12")&&scsi.Fields.Any(f=>f.Value=="4"),"SCSI/SAS defect and error counters remain in the full report");
        Check(SmartReport.Parse(ata.Replace("Test ATA","=HYPERLINK(test)"),0,2).Csv().Contains("\"'=HYPERLINK(test)\""),"SMART CSV neutralizes device-supplied spreadsheet formulas");
        var run=new RunActivity{Steps=[new(){State="Completed"},new(){State="Running",Percent=50}],CurrentStep=1};Check(run.OverallPercent==75,"Chain progress combines completed steps and measured active-step progress");
    }
    static async Task V02Integration(string root,DiskInfo source,DiskInfo target)
    {
        // The two arguments are the VHDs created and owned by the integration harness.
        Check(source.Bus=="File Backed Virtual"&&target.Bus=="File Backed Virtual","Extended destructive tests are restricted to owned VHDs");
        async Task<DiskInfo> Current(int n)=>(await StorageService.InventoryAsync()).Single(d=>d.Number==n);
        async Task Apply(DiskInfo d,OperationKind k,PartitionInfo? p=null,Func<DiskOperation,DiskOperation>? edit=null){var o=DiskOperation.For(d,k,p);await StorageService.ExecuteAsync(edit?.Invoke(o)??o);}
        await Apply(source,OperationKind.DeletePartition,source.Partitions.Single(p=>!p.Protected));source=await Current(source.Number);
        await Apply(source,OperationKind.CreatePartition,edit:o=>o with{Bytes=48L*1024*1024,Value="First"});source=await Current(source.Number);
        await Apply(source,OperationKind.CreatePartition,edit:o=>o with{Bytes=48L*1024*1024,Value="Second"});source=await Current(source.Number);
        var data=source.Partitions.Where(p=>!p.Protected).OrderBy(p=>p.Offset).ToArray();var actions=new List<DiskOperation>{DiskOperation.For(source,OperationKind.DeletePartition,data[0]),DiskOperation.For(source,OperationKind.DeletePartition,data[1]),DiskOperation.For(source,OperationKind.CreatePartition) with{Bytes=128L*1024*1024,Value="NewData"}};
        var preview=QueuePlanner.Project([source],actions).Single();actions.Add(DiskOperation.For(preview,OperationKind.SetLabel,preview.Partitions.Single(p=>p.Number<0)) with{Value="CHAINOK"});
        var activity=new RunActivity{Steps=actions.Select(o=>new StepActivity{Description=o.Description}).ToList()};await QueueRunner.RunAsync(actions,new Dictionary<int,DiskInfo>{{source.Number,source}},activity);
        source=await Current(source.Number);var created=source.Partitions.Single(p=>!p.Protected);Check(created.Label=="CHAINOK"&&created.Size==128L*1024*1024,"Apply executes delete + delete + create + dependent rename on a real VHD");Check(activity.State=="Completed"&&activity.Steps.All(s=>s.State=="Completed")&&activity.Events.Count>=actions.Count,"Activity records current phases, completed steps and verified outcomes");
        var planned=new[]{DiskOperation.For(source,OperationKind.SetLabel,created) with{Value="Later"}};await Apply(source,OperationKind.SetLabel,created,o=>o with{Value="External"});var stale=new RunActivity{Steps=[new(){Description=planned[0].Description}]};await RejectAsync(()=>QueueRunner.RunAsync(planned,new Dictionary<int,DiskInfo>{{source.Number,source}},stale),"Real external layout/label change stops a reviewed queue before writing");Check(stale.State=="Failed","Preflight failure is recorded in the activity run");source=await Current(source.Number);created=source.Partitions.Single(p=>!p.Protected);
        var content=Path.Combine(created.Letter+@":\","clone-payload.txt");await File.WriteAllTextAsync(content,"Filesystem-aware clone byte verification");
        target=await Current(target.Number);await Apply(target,OperationKind.Online);target=await Current(target.Number);
        var clone=DiskOperation.For(source,OperationKind.CloneDisk,created) with{CloneMode=CloneMode.FileSystem,TargetDiskNumber=target.Number,TargetIdentity=target.Identity,TargetLayout=target.Layout,Workspace=root};var events=new List<DriveProgress>();await StorageService.ExecuteDetailedAsync(clone,new CollectProgress(events));
        var cloned=(await Current(target.Number)).Partitions.Single(p=>p.FileSystem=="NTFS");Check(await File.ReadAllTextAsync(Path.Combine(cloned.Letter+@":\","clone-payload.txt"))=="Filesystem-aware clone byte verification","Real DISM filesystem clone preserves source file content");Check(!(await Current(source.Number)).ReadOnly,"Filesystem clone restores the source read-only state");Check(events.Any(e=>e.Phase.Contains("Capturing"))&&events.Any(e=>e.Phase.Contains("Applying")),"DISM capture/apply phases are visible to activity");
        source=await Current(source.Number);target=await Current(target.Number);var os=clone with{CloneMode=CloneMode.WindowsUefi,Identity=source.Identity,Layout=source.Layout,TargetIdentity=target.Identity,TargetLayout=target.Layout};await RejectAsync(()=>StorageService.ExecuteDetailedAsync(os),"Windows-aware mode rejects an ordinary data volume before clearing the destination");Check((await Current(target.Number)).Layout==target.Layout,"Rejected OS clone preserves destination layout");
        await Apply(target,OperationKind.QuickWipeDisk);target=await Current(target.Number);Check(target.Style=="RAW"&&target.Partitions.Count==0,"Quick wipe removes partition metadata and reports RAW state");await Apply(target,OperationKind.Offline);target=await Current(target.Number);
        foreach(var mode in new[]{WipeMode.Random,WipeMode.ThreePass})
        {var progress=new List<DriveProgress>();await StorageService.ExecuteDetailedAsync(DiskOperation.For(target,OperationKind.WipeDisk) with{WipeMode=mode},new CollectProgress(progress));Check(progress.Any(p=>p.Phase.StartsWith("Verify pass"))&&progress.Last().Percent>=99,"Thorough "+mode+" overwrite reports progress and verifies every pass");target=await Current(target.Number);}
        await UefiLayoutChecks(root,source);
        var smart=await SmartService.ReadAsync((await StorageService.InventoryAsync()).First(d=>d.Protected));Check(smart.Raw.ValueKind==JsonValueKind.Object&&smart.Overall!="", "Bundled smartctl runs on Windows ARM/x64 and returns a device/unsupported report");
    }
    static async Task UefiLayoutChecks(string root,DiskInfo source)
    {
        var file=Path.Combine(root,"uefi-owned.vhd");var script=Path.Combine(root,"uefi-owned.txt");
        try
        {
            await File.WriteAllTextAsync(script,$"create vdisk file=\"{file}\" maximum=2048 type=expandable\r\nselect vdisk file=\"{file}\"\r\nattach vdisk\r\nexit\r\n");
            var text=await StorageService.PowerShellAsync($"diskpart /s '{script.Replace("'","''")}'; (Get-DiskImage -ImagePath '{file.Replace("'","''")}'|Get-Disk).Number");int number=int.Parse(text.Split('\n').Last().Trim());
            var target=(await StorageService.InventoryAsync()).Single(d=>d.Number==number);Check(target.Bus=="File Backed Virtual"&&!target.Protected,"UEFI layout check uses its own disposable VHD");
            var op=DiskOperation.For(source,OperationKind.CloneDisk) with{CloneMode=CloneMode.WindowsUefi,TargetDiskNumber=number,TargetIdentity=target.Identity,TargetLayout=target.Layout};
            var letters=await FileSystemCloneEngine.CreateDestinationLayoutAsync(op,target,false);var after=(await StorageService.InventoryAsync()).Single(d=>d.Number==number);
            Check(after.Style=="GPT"&&after.Partitions.Any(p=>p.FileSystem=="FAT32")&&after.Partitions.Any(p=>p.FileSystem=="NTFS")&&after.Partitions.Any(p=>p.Type=="Reserved"),"Windows-aware destination builds actual GPT / EFI / MSR / NTFS layout");
            var firmwareBefore=await StorageService.PowerShellAsync("bcdedit.exe /enum firmware");var system=Environment.GetFolderPath(Environment.SpecialFolder.Windows);var boot=await ProcessTool.RunAsync(Path.Combine(system,"System32","bcdboot.exe"),[system,"/s",letters.Efi+":","/f","UEFI","/c","/v"]);
            Check(boot.ExitCode==0&&File.Exists(Path.Combine(letters.Efi+@":\",@"EFI\Microsoft\Boot\BCD")),"BCDBoot writes a real BCD store only to the explicit disposable EFI target");
            var firmwareAfter=await StorageService.PowerShellAsync("bcdedit.exe /enum firmware");Check(firmwareBefore==firmwareAfter,"Explicit-target BCDBoot preserves the host firmware boot entries");
        }
        finally{await StorageService.PowerShellAsync($"Dismount-DiskImage -ImagePath '{file.Replace("'","''")}' -ErrorAction SilentlyContinue | Out-Null");}
    }
    sealed class CollectProgress(List<DriveProgress> target):IProgress<DriveProgress>{public void Report(DriveProgress value)=>target.Add(value);}
}
