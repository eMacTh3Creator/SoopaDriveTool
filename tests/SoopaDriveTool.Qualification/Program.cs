using System.IO;
using System.Text.Json;
using SoopaDriveTool.Core;
using SoopaDriveTool.Windows;

// This harness runs only against disposable, explicitly fingerprinted lab disks.
// It calls the same QueueRunner and StorageService used by the desktop Apply button.
record CloneJob(int Source,int Target,string SourceIdentity,string TargetIdentity,string FixtureId,string Mode,string Workspace,long ExpectedDiskBytes,long TargetBytes=0);
class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            if(args.Length==2&&args[0]=="--inventory")
            {await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(await StorageService.InventoryAsync(),Data.Json));return 0;}
            if(args.Length!=3||args[0]!="--clone")throw new ArgumentException("Use --inventory output.json or --clone private-job.json activity-output.json");
            if(!Environment.MachineName.Equals("SDTLAB",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Run this destructive qualification harness only inside the dedicated SDTLab VM.");
            var job=JsonSerializer.Deserialize<CloneJob>(await File.ReadAllTextAsync(args[1]),Data.Json)??throw new IOException("Missing job.");
            var disks=await StorageService.InventoryAsync();var source=disks.Single(d=>d.Number==job.Source);var target=disks.Single(d=>d.Number==job.Target);
            var mode=Enum.Parse<CloneMode>(job.Mode);bool live=mode==CloneMode.WindowsLiveUefi;
            if(source.Protected&&!live||target.Protected||source.Number==target.Number||job.ExpectedDiskBytes<64L*1024*1024*1024||job.ExpectedDiskBytes>512L*1024*1024*1024||source.Size!=job.ExpectedDiskBytes||target.Size!=(job.TargetBytes==0?source.Size:job.TargetBytes)||source.Identity!=job.SourceIdentity||target.Identity!=job.TargetIdentity)throw new InvalidOperationException("Disposable source/target fingerprints do not match.");
            var partition=source.Partitions.Single(p=>p.FileSystem=="NTFS"&&p.Letter.Length==1&&File.Exists(Path.Combine(p.Letter+@":\",live?"SoopaLiveQualification":"SoopaQualification","fixture-id.txt")));
            var marker=await File.ReadAllTextAsync(Path.Combine(partition.Letter+@":\",live?"SoopaLiveQualification":"SoopaQualification","fixture-id.txt"));
            if(!Guid.TryParse(job.FixtureId,out _)||marker.Trim()!=job.FixtureId)throw new IOException("Source fixture identity does not match.");
            if(mode is not (CloneMode.WindowsUefi or CloneMode.WindowsLiveUefi or CloneMode.ByteExact or CloneMode.ByteExactGrow))throw new IOException("Unsupported qualification mode.");
            var operations=new List<DiskOperation>();
            if(mode is CloneMode.ByteExact or CloneMode.ByteExactGrow)
            {if(!source.Offline)operations.Add(DiskOperation.For(source,OperationKind.Offline));if(!target.Offline)operations.Add(DiskOperation.For(target,OperationKind.Offline));}
            var clone=DiskOperation.For(source,OperationKind.CloneDisk,mode is CloneMode.WindowsUefi or CloneMode.WindowsLiveUefi?partition:null) with{CloneMode=mode,TargetDiskNumber=target.Number,TargetIdentity=target.Identity,TargetLayout=target.Layout,Workspace=job.Workspace};operations.Add(clone);
            var activity=new RunActivity{Steps=operations.Select(o=>new StepActivity{Description=o.Description}).ToList()};
            string last="";double lastPercent=-10;
            try
            {
                await QueueRunner.RunAsync(operations,new Dictionary<int,DiskInfo>{{source.Number,source},{target.Number,target}},activity,a=>
                {var step=a.Steps[a.CurrentStep];if(step.Phase!=last||step.Percent.HasValue&&step.Percent>=lastPercent+5){Console.WriteLine($"{DateTime.UtcNow:O} {step.Phase} {step.Percent:0.0}% {step.Message}");last=step.Phase;lastPercent=step.Percent??-10;}});
            }
            finally{await File.WriteAllTextAsync(args[2],JsonSerializer.Serialize(activity,Data.Json));}
            Console.WriteLine("CLONE COMPLETED AND VERIFIED; independent boot check is the next stage.");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
