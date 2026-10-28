using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public static class StorageService
{
    public static bool IsAdmin => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    public static async Task<string> PowerShellAsync(string script,CancellationToken ct=default)
    {
        var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),@"System32\WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        foreach(var a in new[]{"-NoLogo","-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes("[Console]::OutputEncoding=[Text.Encoding]::UTF8; $ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; try { "+script+" } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }"))})start.ArgumentList.Add(a);
        using var process=Process.Start(start)??throw new IOException("Could not start Windows storage provider.");
        var output=process.StandardOutput.ReadToEndAsync(ct);var error=process.StandardError.ReadToEndAsync(ct);
        try{await process.WaitForExitAsync(ct);}catch{try{process.Kill(true);}catch{}throw;}
        var text=await output;var err=await error;if(process.ExitCode!=0)throw new IOException(err.Length>0?err.Trim():"Windows storage provider failed.");return text.Trim();
    }
    static string Script(string name)
    {
        using var s=Assembly.GetExecutingAssembly().GetManifestResourceStream($"SoopaDriveTool.Scripts.{name}.ps1")??Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(n=>n.EndsWith($".{name}.ps1")).Select(n=>Assembly.GetExecutingAssembly().GetManifestResourceStream(n)).First()!;
        using var r=new StreamReader(s);return r.ReadToEnd();
    }
    public static async Task<List<DiskInfo>> InventoryAsync(CancellationToken ct=default)=>JsonSerializer.Deserialize<List<DiskInfo>>(await PowerShellAsync(Script("Inventory"),ct),Data.Json)??[];
    public static async Task<string> HealthAsync(CancellationToken ct=default)=>await PowerShellAsync("$r=@(Get-PhysicalDisk | ForEach-Object { $p=$_; try { $c=$p | Get-StorageReliabilityCounter -ErrorAction Stop; [pscustomobject]@{Name=$p.FriendlyName;Health=[string]$p.HealthStatus;Temperature=$c.Temperature;Wear=$c.Wear;PowerOnHours=$c.PowerOnHours;ReadErrors=$c.ReadErrorsTotal;WriteErrors=$c.WriteErrorsTotal} } catch { [pscustomobject]@{Name=$p.FriendlyName;Health=[string]$p.HealthStatus;Note='Controller did not expose reliability counters'} } }); ConvertTo-Json -InputObject $r -Depth 5",ct);
    public static async Task<string> LimitsAsync(DiskInfo d,PartitionInfo p)=>await PowerShellAsync($"Get-PartitionSupportedSize -DiskNumber {d.Number} -PartitionNumber {p.Number} | Select-Object SizeMin,SizeMax | ConvertTo-Json");
    public static async Task<string> ExecuteAsync(DiskOperation op,IProgress<string>? progress=null)
    {
        if(!IsAdmin)throw new UnauthorizedAccessException("Restart as administrator to apply disk changes.");
        var all=await InventoryAsync();var d=all.SingleOrDefault(d=>d.Number==op.DiskNumber)??throw new IOException("Disk disconnected.");var target=all.SingleOrDefault(d=>d.Number==op.TargetDiskNumber);
        OperationPolicy.Validate(op,d,target);
        if(op.Kind is OperationKind.BackupImage or OperationKind.RestoreImage or OperationKind.CloneDisk or OperationKind.WipeDisk)
            return await RawDiskEngine.ExecuteAsync(op,d,target,progress);
        var payload=Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{Operation=new{op.DiskNumber,op.PartitionNumber,op.Bytes,op.Value,op.FileSystem,KindName=op.Kind.ToString()},Disk=d},Data.Json)));
        var script="$request=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+payload+"')) | ConvertFrom-Json; "+Script("Mutate");
        var result=await PowerShellAsync(script);
        var after=(await InventoryAsync()).SingleOrDefault(x=>x.Number==d.Number);
        return result+Environment.NewLine+(after is null?"Disk unavailable after operation.":$"Verified device {after.Number}: {after.Style}, {after.Partitions.Count} partitions, {(after.Offline?"offline":"online")}.");
    }
    public static void Elevate()
    {
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true,Verb="runas"});
    }
}
