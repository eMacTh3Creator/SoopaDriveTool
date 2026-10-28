namespace SoopaDriveTool.Core;
public record DriveProgress(string Phase,double? Percent,string Message,long? CompletedBytes=null,long? TotalBytes=null,double? BytesPerSecond=null);
public class StepActivity
{
    public string Description {get;set;}="";
    public string State {get;set;}="Pending";
    public string Phase {get;set;}="Waiting";
    public double? Percent {get;set;}
    public string Message {get;set;}="";
    public DateTime? StartedUtc {get;set;}
    public DateTime? CompletedUtc {get;set;}
}
public record DriveIdentity(int DiskNumber,string Model,string Serial,string IdentityHash);
public record ActivityEvent(DateTime Utc,int Step,string Phase,double? Percent,string Message);
public class RunActivity
{
    public Guid Id {get;set;}=Guid.NewGuid();
    public DateTime StartedUtc {get;set;}=DateTime.UtcNow;
    public DateTime? CompletedUtc {get;set;}
    public string State {get;set;}="Running";
    public int CurrentStep {get;set;}
    public List<StepActivity> Steps {get;set;}=[];
    public List<DriveIdentity> Devices {get;set;}=[];
    public List<ActivityEvent> Events {get;set;}=[];
    public double OverallPercent=>Steps.Count==0?0:100.0*Steps.Count(s=>s.State=="Completed")/Steps.Count+(CurrentStep<Steps.Count&&Steps[CurrentStep].State=="Running"?(Steps[CurrentStep].Percent??0)/Steps.Count:0);
    public static readonly string[] WaitingNotes=["A GiB contains 1,073,741,824 bytes. Every one gets a seat.","The progress bar is working hard on its very long loading-bar résumé.","A clone copies data. A backup also deserves a verification check.","SSDs do not need defragmenting to line up their tiny digital ducks.","Quick partition reset is quick because file contents remain recoverable.","Drive firmware and USB bridges decide which SMART details are available.","Counting sectors. They still refuse to sort themselves alphabetically.","Keep the drive connected until verification has finished."];
}

public record PendingPlan(List<DiskInfo> Baselines,List<DiskOperation> Operations);
