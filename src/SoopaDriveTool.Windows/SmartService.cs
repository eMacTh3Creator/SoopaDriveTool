using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public static class SmartService
{
    public static async Task<SmartReport> ReadAsync(DiskInfo disk,string deviceType="auto")
    {
        if(!StorageService.IsAdmin)throw new UnauthorizedAccessException("Restart as administrator for full SMART/device-log access.");
        if(deviceType is not ("auto" or "sat" or "scsi" or "nvme"))throw new IOException("Unsupported transport override.");
        var assembly=Assembly.GetExecutingAssembly();using var manifest=assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n=>n.EndsWith("dependencies.json")))!;using var data=await JsonDocument.ParseAsync(manifest);var pin=data.RootElement.GetProperty("smartmontools");
        string root=Path.Combine(AppContext.BaseDirectory,"Tools","smartmontools");string exe=Path.Combine(root,"smartctl.exe"),db=Path.Combine(root,"drivedb.h");
        foreach(var pair in new[]{(exe,"smartctl_sha256"),(db,"drivedb_sha256")})
        {
            if(!File.Exists(pair.Item1))throw new IOException("The bundled SMART collector/database is missing. Reinstall the full package.");
            await using var file=File.OpenRead(pair.Item1);if(Convert.ToHexString(await SHA256.HashDataAsync(file)).ToLowerInvariant()!=pin.GetProperty(pair.Item2).GetString())throw new IOException("SMART collector integrity verification failed.");
        }
        var before=(await StorageService.InventoryAsync()).Single(d=>d.Number==disk.Number);if(before.Identity!=disk.Identity)throw new IOException("The selected drive was replaced or disconnected.");
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));var result=await ProcessTool.RunAsync(exe,["-x","--json=o","-B",db,"-d",deviceType,"/dev/pd"+disk.Number],ct:timeout.Token);
        if(result.Output.TrimStart().StartsWith('{')==false)throw new IOException("SMART collector did not return JSON. "+result.Error+" "+result.Output);
        if((result.ExitCode&1)!=0)throw new IOException("SMART collector command/configuration error: "+result.Output);
        var report=SmartReport.Parse(result.Output,result.ExitCode,disk.Number,disk.Serial);
        var after=(await StorageService.InventoryAsync()).Single(d=>d.Number==disk.Number);if(after.Identity!=before.Identity)throw new IOException("Drive identity changed during SMART collection; report discarded.");return report;
    }
}
