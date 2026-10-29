using SoopaDriveTool.Core;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
partial class Program
{
    static int checks;
    static void Check(bool value,string description){if(!value)throw new Exception("FAIL: "+description);checks++;Console.WriteLine("PASS: "+description);}
    static async Task Reject(Func<Task> action,string description){try{await action();}catch{Check(true,description);return;}throw new Exception("FAIL: accepted "+description);}
    static async Task<int> Main()
    {
        string root=Path.Combine(Path.GetTempPath(),"SoopaCore-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{await VaultChecks(root);await TableChecks(root);
        await MoveChecks(root);await RecoveryChecks(root);DiskRefreshChecks();Console.WriteLine($"ALL {checks} CORE CHECKS PASSED");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{Directory.Delete(root,true);}
    }
    static async Task VaultChecks(string root)
    {
        string source=Path.Combine(root,"source"),repository=Path.Combine(root,"protected"),plain=Path.Combine(root,"plain");Directory.CreateDirectory(Path.Combine(source,"empty"));Directory.CreateDirectory(Path.Combine(source,"excluded"));
        byte[] data=RandomNumberGenerator.GetBytes(5*1024*1024+31);await File.WriteAllBytesAsync(Path.Combine(source,"payload.bin"),data);await File.WriteAllTextAsync(Path.Combine(source,"secret-file-name.txt"),"content that should not appear outside authenticated ciphertext");await File.WriteAllTextAsync(Path.Combine(source,"excluded","skip.tmp"),"skip");
        const string password="correct horse reliable storage";string full,incremental,diff;
        using(var vault=await VaultBackup.OpenAsync(repository,password,true))
        {
            Check(vault.Config.Encrypted,"Vault encryption is configured without persisting a password");
            await Reject(async()=>{using var other=await VaultBackup.OpenAsync(repository,password);},"Repository locking rejects a concurrent operation");
            full=await vault.CreateAsync(source,new(true,["excluded/**","excluded"],"Full"));await vault.VerifyAsync(full);var m=await vault.LoadAsync(full);
            Check(m.Entries.Any(e=>e.Kind=="Directory"&&e.Path=="empty")&&m.Entries.All(e=>!e.Path.StartsWith("excluded")),"Empty directories are retained and excluded trees are skipped");
            Check(m.Entries.Single(e=>e.Path=="payload.bin").Resources[0].Chunks.Count==2,"Large files use bounded stable chunks");
            var objects=Directory.GetFiles(repository,"*.svo",SearchOption.AllDirectories).Length;incremental=await vault.CreateAsync(source,new(true,["excluded/**","excluded"],"Incremental"));
            Check(Directory.GetFiles(repository,"*.svo",SearchOption.AllDirectories).Length==objects,"An unchanged incremental writes no new data chunks");
            await File.WriteAllTextAsync(Path.Combine(source,"secret-file-name.txt"),"new content");diff=await vault.CreateAsync(source,new(true,["excluded/**","excluded"],"Differential"));var d=await vault.LoadAsync(diff);
            Check(d.BaseSnapshot==full,"Differential snapshot is based on the full restore point");
            var next=await vault.CreateAsync(source,new(true,["excluded/**","excluded"],"Differential"));var nextM=await vault.LoadAsync(next);
            Check(d.Entries.Single(e=>e.Path=="secret-file-name.txt").Resources[0].Chunks[0]!=nextM.Entries.Single(e=>e.Path=="secret-file-name.txt").Resources[0].Chunks[0],"Separate differential points own changed data independently of prior differentials");
            var restore=Path.Combine(root,"restored");await vault.RestoreAsync(full,restore);Check((await File.ReadAllBytesAsync(Path.Combine(restore,"payload.bin"))).SequenceEqual(data)&&Directory.Exists(Path.Combine(restore,"empty")),"Encrypted restore preserves binary content and empty folders");
            var selective=Path.Combine(root,"selected");await vault.RestoreAsync(full,selective,["secret-file-name.txt"]);Check(File.Exists(Path.Combine(selective,"secret-file-name.txt"))&&!File.Exists(Path.Combine(selective,"payload.bin")),"Selective restore exports only the selected files");
            await Reject(()=>vault.RestoreAsync(full,selective),"Restore refuses to overwrite an existing destination");
            var before=Directory.GetFiles(repository,"*.svo",SearchOption.AllDirectories).Length;await File.WriteAllTextAsync(Path.Combine(source,"secret-file-name.txt"),"third version");var last=await vault.CreateAsync(source,new(true,["excluded/**","excluded"],"Incremental"));var pruned=await vault.PruneAsync(1);Check(pruned.SnapshotsRemoved>=4&&pruned.ObjectsRemoved>0,"Retention removes expired restore points and their unreferenced chunks");await vault.VerifyAsync(last);Check(true,"The retained incremental remains complete after its original base manifest is pruned");
            var chunk=Directory.GetFiles(repository,"*.svo",SearchOption.AllDirectories)[0];var bytes=await File.ReadAllBytesAsync(chunk);bytes[^1]^=0x40;await File.WriteAllBytesAsync(chunk,bytes);await Reject(()=>vault.VerifyAsync(last),"Authenticated verification rejects corrupted data packets");
        }
        using(var vault=await VaultBackup.OpenAsync(repository,password))await vault.ChangePassphraseAsync("new strong recovery passphrase");
        await Reject(async()=>{using var vault=await VaultBackup.OpenAsync(repository,password);},"Passphrase rotation rejects the old passphrase without rewriting data");
        using(var vault=await VaultBackup.OpenAsync(repository,"new strong recovery passphrase"))Check((await vault.ListAsync()).Count==1,"A rotated passphrase opens the retained restore point");
        var manifest=Directory.GetFiles(repository,"*.svm",SearchOption.AllDirectories)[0];Check(!Encoding.UTF8.GetString(await File.ReadAllBytesAsync(manifest)).Contains("secret-file-name"),"Encrypted manifests conceal file names and source metadata");
        Check(!File.ReadAllText(Path.Combine(repository,"vault.json")).Contains(password),"Repository configuration contains no plaintext passphrase");
        await Reject(async()=>{using var vault=await VaultBackup.OpenAsync(repository,"wrong password here");},"Wrong passphrases are rejected before repository access");
        using(var vault=await VaultBackup.OpenAsync(plain,null,true)){var id=await vault.CreateAsync(source);await vault.VerifyAsync(id);Check(!vault.Config.Encrypted,"Unencrypted compressed repositories remain an explicit supported choice");await Reject(()=>vault.CreateAsync(plain),"Backup rejects source/repository tree overlap");}
    }
}
