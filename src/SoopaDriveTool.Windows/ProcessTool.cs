using System.Diagnostics;
using System.Text;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public record ToolResult(int ExitCode,string Output,string Error);
public static class ProcessTool
{
    public static async Task<ToolResult> RunAsync(string executable,IEnumerable<string> arguments,string phase="",IProgress<DriveProgress>? progress=null,double start=0,double span=100,CancellationToken ct=default)
    {
        var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};foreach(var a in arguments)info.ArgumentList.Add(a);
        using var process=Process.Start(info)??throw new IOException("Could not start "+Path.GetFileName(executable));
        var output=new StringBuilder();var error=process.StandardError.ReadToEndAsync();var parser=new ToolProgressReader(Path.GetFileName(executable).Equals("wimlib-imagex.exe",StringComparison.OrdinalIgnoreCase),phase,progress,start,span);
        var read=Read();
        async Task Read()
        {
            var buffer=new char[1024];int n;
            while((n=await process.StandardOutput.ReadAsync(buffer))>0)
            {
                string part=new(buffer,0,n);if(output.Length<16*1024*1024)output.Append(part);parser.Append(part);
            }
            parser.Append("\n");
        }
        try{await process.WaitForExitAsync(ct);await read;return new(process.ExitCode,output.ToString(),await error);}
        catch{try{process.Kill(true);await process.WaitForExitAsync();}catch{}throw;}
    }
}
