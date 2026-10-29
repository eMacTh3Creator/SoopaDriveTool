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
    public static async Task<string> PowerShellAsync(string script,CancellationToken ct=default,IProgress<DriveProgress>? progress=null)
    {
        var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),@"System32\WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        foreach(var a in new[]{"-NoLogo","-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes("[Console]::OutputEncoding=[Text.Encoding]::UTF8; $ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; try { "+script+" } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }"))})start.ArgumentList.Add(a);
        using var process=Process.Start(start)??throw new IOException("Could not start Windows storage provider.");
        var output=ReadOutput();var error=process.StandardError.ReadToEndAsync();
        async Task<string> ReadOutput()
        {
            var b=new StringBuilder();string? line;
            while((line=await process.StandardOutput.ReadLineAsync())!=null)
            {
                const string marker="__SDT_PROGRESS__";
                if(line.StartsWith(marker,StringComparison.Ordinal))
                {try{progress?.Report(JsonSerializer.Deserialize<DriveProgress>(line[marker.Length..],Data.Json)!);}catch(JsonException){} }
                else b.AppendLine(line);
            }
            return b.ToString();
        }
        try{await process.WaitForExitAsync(ct);}catch{try{process.Kill(true);}catch{}throw;}
        var text=await output;var err=await error;if(process.ExitCode!=0)throw new IOException(err.Length>0?err.Trim():"Windows storage provider failed.");return text.Trim();
    }
    static string Script(string name)
    {
        using var s=Assembly.GetExecutingAssembly().GetManifestResourceStream($"SoopaDriveTool.Scripts.{name}.ps1")??Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(n=>n.EndsWith($".{name}.ps1")).Select(n=>Assembly.GetExecutingAssembly().GetManifestResourceStream(n)).First()!;
        using var r=new StreamReader(s);return r.ReadToEnd();
    }
    public static async Task<List<DiskInfo>> InventoryAsync(CancellationToken ct=default)
    {
        var disks=JsonSerializer.Deserialize<List<DiskInfo>>(await PowerShellAsync(Script("Inventory"),ct),Data.Json)??[];
        if(IsAdmin)for(int i=0;i<disks.Count;i++){try{if(disks[i].Style=="Dynamic")disks[i]=DynamicDiskService.Inspect(disks[i]);disks[i]=disks[i] with{NativeIdentity=DiskAccess.Fingerprint(disks[i].Number)};}catch(System.ComponentModel.Win32Exception){/* controller did not expose direct descriptors */}}
        return disks;
    }
    public static async Task<string> HealthAsync(CancellationToken ct=default)=>await PowerShellAsync("$r=@(Get-PhysicalDisk | ForEach-Object { $p=$_; try { $c=$p | Get-StorageReliabilityCounter -ErrorAction Stop; [pscustomobject]@{Name=$p.FriendlyName;Health=[string]$p.HealthStatus;Temperature=$c.Temperature;Wear=$c.Wear;PowerOnHours=$c.PowerOnHours;ReadErrors=$c.ReadErrorsTotal;WriteErrors=$c.WriteErrorsTotal} } catch { [pscustomobject]@{Name=$p.FriendlyName;Health=[string]$p.HealthStatus;Note='Controller did not expose reliability counters'} } }); ConvertTo-Json -InputObject $r -Depth 5",ct);
    public static async Task<string> LimitsAsync(DiskInfo d,PartitionInfo p)=>await PowerShellAsync($"Get-PartitionSupportedSize -DiskNumber {d.Number} -PartitionNumber {p.Number} | Select-Object SizeMin,SizeMax | ConvertTo-Json");
    public static Task<string> ExecuteAsync(DiskOperation op,IProgress<string>? progress=null)=>ExecuteDetailedAsync(op,new TextProgress(progress));
    sealed class TextProgress(IProgress<string>? target):IProgress<DriveProgress>{public void Report(DriveProgress p)=>target?.Report(p.Phase+" · "+(p.Percent.HasValue?$"{p.Percent:0}% · ":"")+p.Message);}
    public static async Task<string> ExecuteDetailedAsync(DiskOperation op,IProgress<DriveProgress>? progress=null)
    {
        if(!IsAdmin)throw new UnauthorizedAccessException("Restart as administrator to apply disk changes.");
        if(op.PartitionNumber<0)throw new InvalidOperationException("Resolve the created-partition reference through the queue before execution.");
        progress?.Report(new("Preflight",null,"Re-enumerating disk identity and protection state."));
        var all=await InventoryAsync();var d=all.SingleOrDefault(d=>d.Number==op.DiskNumber)??throw new IOException("Disk disconnected.");var target=all.SingleOrDefault(d=>d.Number==op.TargetDiskNumber);
        OperationPolicy.Validate(op,d,target);
        if(op.Kind==OperationKind.CreatePartition&&op.Offset==0)op=op with{Offset=QueuePlanner.Allocate(d,op.Bytes)};
        if(op.Kind==OperationKind.ConvertDynamic)return await DynamicDiskService.ConvertAsync(op,d,progress);
        if(AdvancedPartitionService.Handles(op.Kind))return await AdvancedPartitionService.ExecuteAsync(op,d,progress);
        if(op.Kind==OperationKind.CloneDisk&&op.CloneMode is not (CloneMode.ByteExact or CloneMode.ByteExactGrow))return await FileSystemCloneEngine.ExecuteAsync(op,d,target!,progress);
        if(op.Kind is OperationKind.BackupImage or OperationKind.RestoreImage or OperationKind.CloneDisk or OperationKind.WipeDisk)
        {
            var result=await RawDiskEngine.ExecuteAsync(op,d,target,progress);
            if(op.Kind==OperationKind.WipeDisk||op.Kind==OperationKind.RestoreImage)await PowerShellAsync("Update-HostStorageCache");
            return result;
        }
        var payload=Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{Operation=new{op.DiskNumber,op.PartitionNumber,op.Bytes,op.Offset,op.Value,op.FileSystem,KindName=op.Kind.ToString()},Disk=d},Data.Json)));
        var script="$request=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+payload+"')) | ConvertFrom-Json; "+Script("Mutate");
        var resultText=await PowerShellAsync(script,progress:progress);
        var after=(await InventoryAsync()).SingleOrDefault(x=>x.Number==d.Number);
        if(after==null)throw new IOException("Disk unavailable after operation; completion is indeterminate.");
        QueuePlanner.VerifyTransition(d,after,op);progress?.Report(new("Verified",100,"The resulting disk state matches the requested change."));
        return resultText+Environment.NewLine+$"Verified disk {after.Number}: {after.Style}, {after.Partitions.Count} partitions, {(after.Offline?"offline":"online")}.";
    }
    public static void Elevate(string? plan=null)
    {
        string executable=Environment.ProcessPath??throw new IOException("Cannot identify the application executable.");
        var info=new ProcessStartInfo(executable){UseShellExecute=true,Verb="runas"};
        if(string.Equals(Path.GetFileNameWithoutExtension(executable),"dotnet",StringComparison.OrdinalIgnoreCase))info.ArgumentList.Add(typeof(App).Assembly.Location);
        if(plan!=null){info.ArgumentList.Add("--resume-plan");info.ArgumentList.Add(Path.GetFullPath(plan));}
        using var child=Process.Start(info)??throw new IOException("Windows did not start the administrator process.");
    }
}
