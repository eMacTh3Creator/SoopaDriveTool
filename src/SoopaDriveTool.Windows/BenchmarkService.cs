using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public record BenchmarkProfile(string Name,int BlockBytes,int QueueDepth,int Threads,bool Random,int? ReadPercent=null);
public record BenchmarkResult(string Name,string Mode,double MBps,double Iops,double LatencyMs,double P99Ms);
public record BenchmarkRun(DateTime CreatedUtc,string Volume,int FileMiB,int Seconds,int Passes,string Profile,string Engine,string Architecture,List<BenchmarkResult> Results,List<BenchmarkProfile>? Workloads=null,bool? ZeroPattern=null);
public static class BenchmarkService
{
    public static readonly BenchmarkProfile[] Standard=[new("SEQ1M Q8T1",1048576,8,1,false),new("SEQ1M Q1T1",1048576,1,1,false),new("RND4K Q32T1",4096,32,1,true),new("RND4K Q1T1",4096,1,1,true)];
    public static async Task<BenchmarkRun> RunAsync(string folder,int fileMiB,int seconds,int passes,string profile,bool mixed,bool zero,IProgress<string>? progress,CancellationToken ct,IReadOnlyList<BenchmarkProfile>? custom=null)
    {
        if(custom!=null)profile="Custom";
        if(fileMiB is <64 or >65536||seconds is <1 or >60||passes is <1 or >5)throw new ArgumentOutOfRangeException(nameof(fileMiB));
        folder=Path.GetFullPath(folder);BackupEngine.RejectExistingAncestorLinks(folder);
        var drive=new DriveInfo(Path.GetPathRoot(folder)!);if(!drive.IsReady)throw new IOException("Drive is not ready.");
        long reserve=Math.Max(256L*1024*1024,drive.TotalSize/20);if(drive.AvailableFreeSpace<fileMiB*1024L*1024+reserve)throw new IOException("Not enough space: leave at least 5% of the volume or 256 MiB free.");
        var engine=Path.Combine(AppContext.BaseDirectory,"Tools","diskspd.exe");if(!File.Exists(engine))throw new IOException("DiskSpd is missing. Reinstall the full application package.");
        var scratch=Path.Combine(folder,".soopadrivetool-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);var file=Path.Combine(scratch,"benchmark.dat");
        var list=new List<BenchmarkResult>();var profiles=custom?.ToArray()??(profile=="Real world"?new[]{Standard[1],Standard[3]}:Standard);ValidateProfiles(profiles);
        try
        {
            // Fully initialize the file; read-only tests must never benchmark sparse/unwritten zeros.
            progress?.Report("Preparing test data…");var buffer=new byte[1024*1024];if(!zero)System.Security.Cryptography.RandomNumberGenerator.Fill(buffer);
            await using(var f=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None,buffer.Length,FileOptions.Asynchronous|FileOptions.WriteThrough))
            {for(int i=0;i<fileMiB;i++){ct.ThrowIfCancellationRequested();await f.WriteAsync(buffer,ct);}f.Flush(true);}
            foreach(var p in profiles)foreach(var mode in p.ReadPercent.HasValue?new[]{p.ReadPercent+"% read"}:mixed?new[]{"Read","Write","Mix 70/30"}:new[]{"Read","Write"})
            {
                var measurements=new List<BenchmarkResult>();
                for(int pass=0;pass<passes;pass++)
                {
                    ct.ThrowIfCancellationRequested();progress?.Report($"{p.Name} · {mode} · pass {pass+1}/{passes}");
                    var start=new ProcessStartInfo(engine){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                    var args=new List<string>{$"-b{p.BlockBytes}",$"-o{p.QueueDepth}",$"-t{p.Threads}",$"-d{seconds}","-W1","-C0","-Sh","-L","-Rxml","-w"+((100-(p.ReadPercent??(mode=="Read"?100:mode=="Write"?0:70))).ToString(CultureInfo.InvariantCulture))};if(p.Random)args.Add("-r4K");else args.Add("-s");if(!zero)args.Add("-Z1M");args.Add(file);foreach(var a in args)start.ArgumentList.Add(a);
                    using var process=Process.Start(start)??throw new IOException("Could not start DiskSpd.");var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
                    using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(seconds+90));
                    try{await process.WaitForExitAsync(deadline.Token);}
                    catch(OperationCanceledException){try{process.Kill(true);using var exitDeadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));await process.WaitForExitAsync(exitDeadline.Token);}catch{}if(ct.IsCancellationRequested)throw;throw new TimeoutException("DiskSpd did not finish within the expected time. Check the storage driver and try a smaller workload.");}
                    var text=await output;var err=await error;if(process.ExitCode!=0)throw new IOException("DiskSpd: "+err+" "+text);
                    measurements.Add(Parse(text,p.Name,mode));
                }
                list.Add(new(p.Name,mode,measurements.Average(x=>x.MBps),measurements.Average(x=>x.Iops),measurements.Average(x=>x.LatencyMs),measurements.Average(x=>x.P99Ms)));
            }
        }
        finally
        {
            // Antivirus and delayed I/O teardown can briefly retain handles after process exit.
            Exception? cleanupError=null;
            for(int attempt=0;attempt<30;attempt++)
            {
                try{if(File.Exists(file))File.Delete(file);if(Directory.Exists(scratch))Directory.Delete(scratch);if(Directory.GetDirectories(folder,Path.GetFileName(scratch)).Length!=0)throw new IOException("Temporary directory deletion is still pending.");cleanupError=null;break;}
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){cleanupError=ex;await Task.Delay(100);}
            }
            if(cleanupError!=null)throw new IOException("Could not remove the temporary benchmark directory: "+scratch,cleanupError);
        }
        var run=new BenchmarkRun(DateTime.UtcNow,drive.Name,fileMiB,seconds,passes,profile,"Microsoft DiskSpd 2.3",System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),list,profiles.ToList(),zero);
        await Data.SaveAsync(Path.Combine(Data.Home,"benchmarks",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".json"),run);return run;
    }
    public static void ValidateProfiles(IReadOnlyList<BenchmarkProfile> profiles)
    {
        if(profiles.Count is <1 or >16)throw new IOException("Choose one to sixteen workloads.");foreach(var p in profiles)if(string.IsNullOrWhiteSpace(p.Name)||p.Name.Length>80||p.BlockBytes is <512 or >16777216||(p.BlockBytes&(p.BlockBytes-1))!=0||p.QueueDepth is <1 or >1024||p.Threads is <1 or >128||p.QueueDepth*p.Threads>4096||p.ReadPercent is <0 or >100)throw new IOException("Invalid workload: use power-of-two block sizes 512 B–16 MiB, queue depth 1–1024, threads 1–128, at most 4096 outstanding requests, and read percentage 0–100.");
    }
    public static BenchmarkResult Parse(string xml,string name,string mode)
    {
        var doc=XDocument.Parse(xml);var span=doc.Descendants("TimeSpan").FirstOrDefault(e=>e.Element("TestTimeSeconds")!=null)??throw new IOException("Invalid DiskSpd XML.");
        double Number(XElement? e)=>double.TryParse(e?.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var v)?v:0;
        var seconds=Number(span.Element("TestTimeSeconds"));if(seconds<=0)throw new IOException("Missing benchmark duration.");
        var targets=span.Descendants("Thread").SelectMany(t=>t.Elements("Target")).ToArray();if(targets.Length==0)throw new IOException("No measured target results.");
        double bytes=targets.Sum(t=>Number(t.Element("BytesCount"))),ios=targets.Sum(t=>Number(t.Element("IOCount")));
        var lat=span.Element("Latency");var average=Number(lat?.Element("AverageTotalMilliseconds"));var bucket=lat?.Elements("Bucket").FirstOrDefault(b=>Number(b.Element("Percentile"))>=99);
        return new(name,mode,bytes/seconds/1_000_000,ios/seconds,average,Number(bucket?.Element("TotalMilliseconds")));
    }
}
