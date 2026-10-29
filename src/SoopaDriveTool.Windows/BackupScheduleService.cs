using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public record VaultBackupJob(int Version,string Id,string Source,string Repository,VaultOptions Options,bool Vss,bool SystemAccount,string Frequency,string Time,string Secret,bool Enabled=true);
public record BackupScheduleRequest(string Source,string Repository,VaultOptions Options,bool Vss,bool SystemAccount,string Frequency,string Time,string? Passphrase);
public static class BackupScheduleService
{
    [StructLayout(LayoutKind.Sequential)]struct Blob{public int Length;public IntPtr Data;}
    [DllImport("crypt32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CryptProtectData(ref Blob input,string description,ref Blob entropy,IntPtr reserved,IntPtr prompt,uint flags,out Blob output);
    [DllImport("crypt32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CryptUnprotectData(ref Blob input,IntPtr description,ref Blob entropy,IntPtr reserved,IntPtr prompt,uint flags,out Blob output);
    [DllImport("kernel32.dll")]static extern IntPtr LocalFree(IntPtr memory);
    public static string DirectoryFor(bool system)=>system?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"SoopaDriveTool","SystemJobs"):Path.Combine(Data.Home,"jobs-v2");
    public static string JobPath(VaultBackupJob job)=>Path.Combine(DirectoryFor(job.SystemAccount),job.Id+".json");
    static string Seal(string secret,bool system,bool decrypt=false)
    {
        byte[] bytes=decrypt?Convert.FromBase64String(secret):Encoding.UTF8.GetBytes(secret),salt=Encoding.UTF8.GetBytes("SoopaDriveTool protected backup schedule v2");Blob input=new(){Length=bytes.Length,Data=Marshal.AllocHGlobal(bytes.Length)},entropy=new(){Length=salt.Length,Data=Marshal.AllocHGlobal(salt.Length)},output=default;
        try{Marshal.Copy(bytes,0,input.Data,bytes.Length);Marshal.Copy(salt,0,entropy.Data,salt.Length);bool ok=decrypt?CryptUnprotectData(ref input,IntPtr.Zero,ref entropy,IntPtr.Zero,IntPtr.Zero,1,out output):CryptProtectData(ref input,"SoopaDriveTool backup passphrase",ref entropy,IntPtr.Zero,IntPtr.Zero,(uint)(1|(system?4:0)),out output);if(!ok)throw new IOException("Windows could not protect/unprotect this backup credential: "+Marshal.GetLastWin32Error());byte[] result=new byte[output.Length];Marshal.Copy(output.Data,result,0,result.Length);try{return decrypt?Encoding.UTF8.GetString(result):Convert.ToBase64String(result);}finally{System.Security.Cryptography.CryptographicOperations.ZeroMemory(result);}}
        finally{if(input.Data!=IntPtr.Zero){for(int i=0;i<input.Length;i++)Marshal.WriteByte(input.Data,i,0);Marshal.FreeHGlobal(input.Data);}Marshal.FreeHGlobal(entropy.Data);if(output.Data!=IntPtr.Zero){for(int i=0;i<output.Length;i++)Marshal.WriteByte(output.Data,i,0);LocalFree(output.Data);}System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);}
    }
    static void Protect(string directory,bool system)
    {
        var security=new DirectorySecurity();security.SetAccessRuleProtection(true,false);var rights=FileSystemRights.FullControl;var inherit=InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit;
        foreach(var sid in new[]{new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null)})security.AddAccessRule(new(sid,rights,inherit,PropagationFlags.None,AccessControlType.Allow));
        if(!system)security.AddAccessRule(new(WindowsIdentity.GetCurrent().User!,rights,inherit,PropagationFlags.None,AccessControlType.Allow));new DirectoryInfo(directory).SetAccessControl(security);
    }
    public static async Task<VaultBackupJob> CreateAsync(string source,string repository,VaultOptions options,bool vss,string? password,bool system,string frequency,string time)
    {
        if(frequency is not ("Daily" or "Weekly" or "Hourly" or "At logon"))throw new IOException("Unsupported backup schedule.");if(!TimeOnly.TryParseExact(time,"HH:mm",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out _))throw new IOException("Enter time as HH:mm.");
        if((system||vss)&&!StorageService.IsAdmin)throw new UnauthorizedAccessException("Administrator access is required for VSS or unattended system backups.");
        var executable=Environment.ProcessPath??throw new IOException("Cannot identify the installed application.");
        if(system&&!BackupEngine.IsInside(executable,Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)))throw new IOException("Install SoopaDriveTool in Program Files before scheduling unattended SYSTEM jobs. Portable applications can schedule logged-on-user jobs.");
        using(var vault=await VaultBackup.OpenAsync(repository,password,true))if(vault.Config.Encrypted!=options.Encrypted)throw new IOException("Schedule encryption does not match the repository.");
        var job=new VaultBackupJob(2,Guid.NewGuid().ToString("N"),Path.GetFullPath(source),Path.GetFullPath(repository),options,vss,system,frequency,time,options.Encrypted?Seal(password!,system):"");string directory=DirectoryFor(system);BackupEngine.RejectExistingAncestorLinks(directory);Directory.CreateDirectory(directory);Protect(directory,system);await Data.SaveAsync(JobPath(job),job);
        var args=new List<string>{"/Create","/F","/TN","SoopaDriveTool-"+job.Id,"/TR","\""+executable+"\" --vault-job \""+JobPath(job)+"\"","/SC",frequency switch{"At logon"=>"ONLOGON","Weekly"=>"WEEKLY","Hourly"=>"HOURLY",_=>"DAILY"}};
        if(frequency!="At logon"){args.AddRange(["/ST",time]);if(frequency=="Weekly")args.AddRange(["/D","SUN"]);}
        if(system)args.AddRange(["/RU","SYSTEM","/RL","HIGHEST"]);else args.AddRange(["/IT","/RL",vss?"HIGHEST":"LIMITED"]);
        try{await TaskCommand(args);}catch{File.Delete(JobPath(job));throw;}return job;
    }
    static async Task TaskCommand(IEnumerable<string> args)
    {
        var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"schtasks.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var a in args)start.ArgumentList.Add(a);using var process=Process.Start(start)??throw new IOException("Cannot open Task Scheduler.");var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();if(process.ExitCode!=0)throw new IOException((await error)+" "+(await output));await output;await error;
    }
    static void Validate(VaultBackupJob job){if(job.Version!=2||!System.Text.RegularExpressions.Regex.IsMatch(job.Id,"^[a-f0-9]{32}$"))throw new IOException("Invalid protected backup job.");}
    public static async Task<List<VaultBackupJob>> ListAsync()
    {
        var rows=new List<VaultBackupJob>();foreach(bool system in new[]{false,true}){string root=DirectoryFor(system);if(!Directory.Exists(root))continue;try{foreach(var file in Directory.GetFiles(root,"*.json").Where(p=>!p.EndsWith(".last-run.json",StringComparison.OrdinalIgnoreCase))){BackupEngine.RejectExistingAncestorLinks(file);var job=JsonSerializer.Deserialize<VaultBackupJob>(await File.ReadAllTextAsync(file),Data.Json)??throw new IOException("Invalid schedule.");Validate(job);rows.Add(job);}}catch(UnauthorizedAccessException)when(!StorageService.IsAdmin){}}return rows;
    }
    public static async Task SetEnabledAsync(VaultBackupJob job,bool enabled){Validate(job);await TaskCommand(["/Change","/TN","SoopaDriveTool-"+job.Id,enabled?"/ENABLE":"/DISABLE"]);await Data.SaveAsync(JobPath(job),job with{Enabled=enabled});}
    public static async Task RunNowAsync(VaultBackupJob job){Validate(job);await TaskCommand(["/Run","/TN","SoopaDriveTool-"+job.Id]);}
    public static async Task DeleteAsync(VaultBackupJob job){Validate(job);await TaskCommand(["/Delete","/TN","SoopaDriveTool-"+job.Id,"/F"]);File.Delete(JobPath(job));}
    public static async Task RunJobAsync(string path)
    {
        path=Path.GetFullPath(path);BackupEngine.RejectExistingAncestorLinks(path);if(new FileInfo(path).Length>1024*1024)throw new IOException("Invalid backup job length.");var job=JsonSerializer.Deserialize<VaultBackupJob>(await File.ReadAllTextAsync(path),Data.Json)??throw new IOException("Missing backup job.");Validate(job);if(path!=Path.GetFullPath(JobPath(job)))throw new IOException("Job is outside the protected schedule folder.");
        try{string? password=job.Options.Encrypted?Seal(job.Secret,job.SystemAccount,true):null;string id=await RunBackupAsync(job.Source,job.Repository,job.Options,job.Vss,password);await Data.SaveAsync(path+".last-run.json",new{Success=true,Snapshot=id,CompletedUtc=DateTime.UtcNow});}
        catch(Exception e){await Data.SaveAsync(path+".last-run.json",new{Success=false,Error=e.Message,CompletedUtc=DateTime.UtcNow});throw;}
    }
    public static async Task<string> RunBackupAsync(string source,string repository,VaultOptions options,bool vss,string? password,IProgress<string>? progress=null,CancellationToken ct=default,IProgress<DriveProgress>? activity=null)
    {
        VssSession? snapshot=null;source=Path.GetFullPath(source);BackupEngine.RejectExistingAncestorLinks(source);
        try
        {
            if(vss)snapshot=await VssSession.CreateAsync([VssSession.VolumeForPath(source)],new Adapter(progress,activity),ct);
            using var vault=await VaultBackup.OpenAsync(repository,password,true,options.PreserveMetadata?new WindowsVaultMetadata(source):null,ct);
            string id=await vault.CreateAsync(snapshot?.PathFor(source)??source,options,progress,ct,source,new Weighted(activity,5,70));await vault.VerifyAsync(id,progress,ct,new Weighted(activity,75,23));if(snapshot!=null)await snapshot.CompleteAsync(ct);activity?.Report(new("Verified",100,"Protected backup, resource hashes and snapshot-writer completion verified."));return id;
        }
        finally{if(snapshot!=null)await snapshot.DisposeAsync();}
    }
    sealed class Adapter(IProgress<string>? progress,IProgress<DriveProgress>? activity):IProgress<DriveProgress>{public void Report(DriveProgress value){progress?.Report(value.Phase+": "+value.Message);activity?.Report(value with{Percent=value.Percent*.05});}}
    sealed class Weighted(IProgress<DriveProgress>? progress,double start,double span):IProgress<DriveProgress>{public void Report(DriveProgress value)=>progress?.Report(value with{Percent=value.Percent.HasValue?start+span*value.Percent/100:null});}
}
