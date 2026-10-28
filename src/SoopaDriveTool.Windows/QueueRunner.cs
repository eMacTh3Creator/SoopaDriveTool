using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public static class QueueRunner
{
    public static async Task RunAsync(IReadOnlyList<DiskOperation> operations,IReadOnlyDictionary<int,DiskInfo> baselines,RunActivity activity,Action<RunActivity>? changed=null)
    {
        if(!StorageService.IsAdmin)throw new UnauthorizedAccessException("Administrator access is required.");
        QueuePlanner.Project(baselines.Values,operations);
        activity.Devices=baselines.Values.Select(d=>new DriveIdentity(d.Number,d.Name,d.Serial,d.Identity)).ToList();
        var expected=baselines.ToDictionary(x=>x.Key,x=>x.Value);var resolved=new Dictionary<(int,int),int>();
        var file=Path.Combine(Data.Home,"activity",activity.StartedUtc.ToString("yyyyMMdd-HHmmss-fff")+"-"+activity.Id+".json");
        double? lastLogged=null;string lastPhase="";
        try
        {
            var initial=await StorageService.InventoryAsync();foreach(var pair in expected)QueuePlanner.AssertUnchanged(pair.Value,initial.Single(d=>d.Number==pair.Key));
            for(int index=0;index<operations.Count;index++)
            {
                activity.CurrentStep=index;var row=activity.Steps[index];row.State="Running";row.StartedUtc=DateTime.UtcNow;row.Phase="Preflight";row.Percent=null;
                var all=await StorageService.InventoryAsync();var original=operations[index];var before=all.Single(d=>d.Number==original.DiskNumber);QueuePlanner.AssertUnchanged(expected[before.Number],before);
                DiskInfo? target=null;if(original.TargetDiskNumber>=0){target=all.Single(d=>d.Number==original.TargetDiskNumber);QueuePlanner.AssertUnchanged(expected[target.Number],target);}
                int number=original.PartitionNumber;if(number<0&&!resolved.TryGetValue((before.Number,number),out number))throw new InvalidOperationException("A preceding create-partition step was not resolved.");
                var op=original with{PartitionNumber=number,Identity=before.Identity,Layout=before.Layout,TargetIdentity=target?.Identity??"",TargetLayout=target?.Layout??""};
                if(op.Kind==OperationKind.CreatePartition)op=op with{Offset=QueuePlanner.Allocate(before,op.Bytes)};
                Report(new("Preflight",null,"Checking identity, protection, capacity and the previous step’s verified state."));await Data.SaveAsync(file,activity);
                var result=await StorageService.ExecuteDetailedAsync(op,new DirectProgress(Report));
                var afterAll=await StorageService.InventoryAsync();var after=afterAll.Single(d=>d.Number==before.Number);
                var created=QueuePlanner.VerifyTransition(before,after,op);if(created!=null)resolved[(before.Number,QueuePlanner.VirtualNumber(index))]=created.Number;
                expected[before.Number]=after;
                if(target!=null)
                {
                    var targetAfter=afterAll.Single(d=>d.Number==target.Number);
                    if(targetAfter.Identity!=target.Identity||targetAfter.Protected)throw new InvalidOperationException("The destination identity changed during cloning.");
                    expected[target.Number]=targetAfter;
                }
                row.State="Completed";row.Phase="Verified";row.Percent=100;row.Message=result;row.CompletedUtc=DateTime.UtcNow;
                activity.Events.Add(new(DateTime.UtcNow,index,"Verified",100,result));changed?.Invoke(activity);await Data.SaveAsync(file,activity);lastPhase="";lastLogged=null;
                void Report(DriveProgress p)
                {
                    row.Phase=p.Phase;row.Percent=p.Percent;row.Message=p.Message;
                    if(lastPhase!=p.Phase||p.Percent.HasValue&&(!lastLogged.HasValue||Math.Abs(p.Percent.Value-lastLogged.Value)>=5))
                    {activity.Events.Add(new(DateTime.UtcNow,index,p.Phase,p.Percent,p.Message));lastPhase=p.Phase;lastLogged=p.Percent;}
                    changed?.Invoke(activity);
                }
            }
            activity.State="Completed";
        }
        catch(Exception ex)
        {
            activity.State="Failed";var row=activity.Steps[activity.CurrentStep];row.State="Failed";row.Message=ex.Message;row.CompletedUtc=DateTime.UtcNow;
            foreach(var later in activity.Steps.Skip(activity.CurrentStep+1))later.State="Skipped";
            activity.Events.Add(new(DateTime.UtcNow,activity.CurrentStep,"Failed",null,ex.Message));throw;
        }
        finally{activity.CompletedUtc=DateTime.UtcNow;changed?.Invoke(activity);await Data.SaveAsync(file,activity);}
    }
    sealed class DirectProgress(Action<DriveProgress> callback):IProgress<DriveProgress>{public void Report(DriveProgress value)=>callback(value);}
}
