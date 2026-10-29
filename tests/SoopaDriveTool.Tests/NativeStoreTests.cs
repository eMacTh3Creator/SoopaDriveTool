using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SoopaDriveTool.Core;
using SoopaDriveTool.Windows;
partial class Program
{
    static async Task NativeStoreCheck(string root)
    {
        string source=Path.Combine(root,"native-source"),repo=Path.Combine(root,"native-vault"),report=Path.Combine(root,"native-report.json");Directory.CreateDirectory(source);await File.WriteAllTextAsync(Path.Combine(source,"hello.txt"),"A");await File.WriteAllBytesAsync(Path.Combine(source,"compressible.bin"),new byte[5*1024*1024]);await File.WriteAllBytesAsync(Path.Combine(source,"empty.bin"),[]);await File.WriteAllTextAsync(Path.Combine(source,"hello.txt")+":Proof","tiny");Directory.CreateDirectory(Path.Combine(source,"empty"));await StorageService.PowerShellAsync($"fsutil.exe hardlink create '{Path.Combine(source,"hardlink.txt")}' '{Path.Combine(source,"hello.txt")}'");
        string id;using(var vault=await VaultBackup.OpenAsync(repo,"native test passphrase",true,new WindowsVaultMetadata(source))){id=await vault.CreateAsync(source,new(true));await vault.VerifyAsync(id);}
        async Task<bool> Verify(string repository,string snapshot,string password)
        {
            if(File.Exists(report))File.Delete(report);var start=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"Tools","SoopaRecovery.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true};foreach(var arg in new[]{"--verify",repository,snapshot,report})start.ArgumentList.Add(arg);using var process=Process.Start(start)!;await process.StandardInput.WriteLineAsync(password);process.StandardInput.Close();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));await process.WaitForExitAsync(timeout.Token);string result=File.Exists(report)?await File.ReadAllTextAsync(report):"{}";Console.WriteLine("Native decoder: "+result);return process.ExitCode==0&&JsonDocument.Parse(result).RootElement.GetProperty("Verified").GetBoolean();
        }
        Check(await Verify(repo,id,"native test passphrase"),"Native recovery independently verifies tiny, empty, compressed multi-chunk files, streams and hardlinks");Check(!await Verify(repo,id,"wrong passphrase"),"Native recovery rejects incorrect passphrases before exporting data");string chunk=Directory.GetFiles(Path.Combine(repo,"objects"),"*.svo",SearchOption.AllDirectories).First();byte[] original=await File.ReadAllBytesAsync(chunk),damaged=(byte[])original.Clone();damaged[^1]^=1;await File.WriteAllBytesAsync(chunk,damaged);Check(!await Verify(repo,id,"native test passphrase"),"Native recovery rejects a tampered AES-GCM object");await File.WriteAllBytesAsync(chunk,original);
        string plain=Path.Combine(root,"native-plain");using(var vault=await VaultBackup.OpenAsync(plain,create:true)){id=await vault.CreateAsync(source,new(PreserveMetadata:false));}Check(await Verify(plain,id,""),"Native recovery also verifies explicitly unencrypted compressed repositories");
    }
}
