using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public record VssVolume(string Volume,string SnapshotId,string Device);
public record VssWriter(string Name,int State,bool Succeeded);
public record VssSnapshotSet(string SnapshotSetId,List<VssVolume> Volumes,List<VssWriter> Writers);
public sealed class VssSession:IAsyncDisposable
{
    readonly Process process;readonly Task<string> errors;bool complete;
    public VssSnapshotSet Snapshot{get;}
    VssSession(Process process,Task<string> errors,VssSnapshotSet snapshot){this.process=process;this.errors=errors;Snapshot=snapshot;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool GetVolumePathName(string path,StringBuilder volume,int size);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool GetVolumeNameForVolumeMountPoint(string mount,StringBuilder name,int size);
    public static string VolumeForPath(string path)
    {
        var mount=new StringBuilder(1024);if(!GetVolumePathName(Path.GetFullPath(path),mount,mount.Capacity))throw new IOException("Cannot resolve the source volume.");var id=new StringBuilder(1024);if(!GetVolumeNameForVolumeMountPoint(mount.ToString(),id,id.Capacity))throw new IOException("Cannot resolve the persistent source volume.");return id.ToString();
    }
    public static async Task<VssSession> CreateAsync(IEnumerable<string> volumes,IProgress<DriveProgress>? progress=null,CancellationToken ct=default)
    {
        if(!StorageService.IsAdmin)throw new UnauthorizedAccessException("Administrator access is required for consistent snapshots of open files.");
        var selected=volumes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();if(selected.Length is 0 or >64||selected.Any(v=>!Regex.IsMatch(v,@"^\\\\\?\\Volume\{[0-9a-fA-F-]{36}\}\\$")))throw new IOException("Invalid VSS volume selection.");
        var exe=Path.Combine(AppContext.BaseDirectory,"Tools","SoopaNative.exe");if(!File.Exists(exe))throw new IOException("The native snapshot engine is missing. Reinstall the full package.");
        var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};start.ArgumentList.Add("vss-session");foreach(var v in selected)start.ArgumentList.Add(v);
        progress?.Report(new("Preparing consistent snapshot",null,"Collecting writer metadata, preparing applications, and creating an atomic copy snapshot."));var process=Process.Start(start)??throw new IOException("Cannot start the VSS requester.");var errors=process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromMinutes(10));var line=await process.StandardOutput.ReadLineAsync(timeout.Token);if(line==null){await process.WaitForExitAsync(ct);throw new IOException("VSS snapshot failed: "+await errors);}
            var snapshot=JsonSerializer.Deserialize<VssSnapshotSet>(line,Data.Json)??throw new IOException("Invalid snapshot response.");
            if(!Guid.TryParse(snapshot.SnapshotSetId,out _)||snapshot.Volumes.Count!=selected.Length||snapshot.Writers.Any(w=>!w.Succeeded)||snapshot.Volumes.Any(v=>!selected.Contains(v.Volume,StringComparer.OrdinalIgnoreCase)||!v.Device.StartsWith(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy",StringComparison.Ordinal)||!Guid.TryParse(v.SnapshotId,out _)))throw new IOException("Snapshot/writer verification failed.");
            progress?.Report(new("Consistent snapshot ready",100,$"Snapshot set contains {snapshot.Volumes.Count} volume(s); {snapshot.Writers.Count} writer statuses verified."));return new(process,errors,snapshot);
        }
        catch{try{process.Kill(true);}catch{}process.Dispose();throw;}
    }
    public string PathFor(string source)
    {
        source=Path.GetFullPath(source);string volume=VolumeForPath(source);var item=Snapshot.Volumes.Single(v=>v.Volume.Equals(volume,StringComparison.OrdinalIgnoreCase));var mount=new StringBuilder(1024);if(!GetVolumePathName(source,mount,mount.Capacity))throw new IOException("Cannot resolve snapshot-relative path.");string relative=Path.GetRelativePath(mount.ToString(),source);return item.Device.TrimEnd('\\')+"\\"+(relative=="."?"":relative);
    }
    public async Task CompleteAsync(CancellationToken ct=default)
    {
        await process.StandardInput.WriteLineAsync("complete");await process.StandardInput.FlushAsync();using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromMinutes(4));var output=await process.StandardOutput.ReadToEndAsync(timeout.Token);await process.WaitForExitAsync(timeout.Token);if(process.ExitCode!=0)throw new IOException("VSS writer completion failed: "+await errors);using var result=JsonDocument.Parse(output);if(!result.RootElement.GetProperty("Completed").GetBoolean())throw new IOException("VSS backup completion was not confirmed.");complete=true;
    }
    public async ValueTask DisposeAsync()
    {
        if(!process.HasExited){try{await process.StandardInput.WriteLineAsync("abort");await process.StandardInput.FlushAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));await process.WaitForExitAsync(timeout.Token);}catch{try{process.Kill(true);}catch{}}}process.Dispose();GC.KeepAlive(complete);
    }
}
