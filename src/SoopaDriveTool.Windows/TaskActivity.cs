using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public partial class MainWindow
{
    async Task RunTaskWithActivity(string label,Func<IProgress<DriveProgress>,CancellationToken,Task> action,bool cancellable=true)
    {
        cts=new CancellationTokenSource();var token=cts.Token;
        try
        {
            await Work(label,async()=>
            {
                var run=new RunActivity{Steps=[new(){Description=label,State="Running",StartedUtc=DateTime.UtcNow,Phase="Preparing"}]};activeRun=run;Navigate("Activity");body.IsEnabled=true;StartActivityTimer();string lastPhase="";double last=-10;string path=Path.Combine(Data.Home,"activity",run.StartedUtc.ToString("yyyyMMdd-HHmmss-fff")+"-"+run.Id+".json");await Data.SaveAsync(path,run);
                var progress=new Progress<DriveProgress>(p=>{if(run.State!="Running")return;var step=run.Steps[0];step.Phase=p.Phase;step.Percent=p.Percent;step.Message=p.Message;if(p.Phase!=lastPhase||p.Percent>=last+5){run.Events.Add(new(DateTime.UtcNow,0,p.Phase,p.Percent,p.Message));lastPhase=p.Phase;last=p.Percent??-10;}RefreshRunDisplay();});
                try{await Task.Run(()=>action(progress,token),token);run.State="Completed";run.Steps[0].State="Completed";run.Steps[0].Phase="Verified";run.Steps[0].Percent=100;}
                catch(OperationCanceledException){run.State="Cancelled";run.Steps[0].State="Cancelled";run.Steps[0].Message="Stopped at a safe cancellation point. Cleanup completed.";throw;}
                catch(Exception ex){run.State="Failed";run.Steps[0].State="Failed";run.Steps[0].Message=ex.Message;run.Events.Add(new(DateTime.UtcNow,0,"Failed",null,ex.Message));throw;}
                finally{run.CompletedUtc=DateTime.UtcNow;run.Steps[0].CompletedUtc=run.CompletedUtc;StopActivityTimer();RefreshRunDisplay();await Data.SaveAsync(path,run);}
            },cancellable);
        }
        finally{cts.Dispose();cts=null;}
    }
}
