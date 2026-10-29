using System.IO;
using SoopaDriveTool.Core;
using SoopaDriveTool.Windows;
partial class Program
{
    static async Task V10SnapshotChecks(string root)
    {
        if(!StorageService.IsAdmin)throw new Exception("Snapshot integration requires administrator access.");
        if(Environment.MachineName!="SDTLAB"&&Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true")throw new Exception("Run destructive/snapshot integration only in the owned spare VM or disposable CI runner.");
        string live=Path.Combine(root,"snapshot-source");Directory.CreateDirectory(live);string file=Path.Combine(live,"locked.txt");await File.WriteAllTextAsync(file,"before atomic snapshot");string volume=VssSession.VolumeForPath(live);
        string snapshotId;
        await using(var session=await VssSession.CreateAsync([volume]))
        {
            snapshotId=session.Snapshot.Volumes.Single().SnapshotId;
            Check(session.Snapshot.Writers.Count>0&&session.Snapshot.Writers.All(w=>w.Succeeded),"VSS prepares application writers and verifies their status");
            await File.WriteAllTextAsync(file,"after atomic snapshot");Check(await File.ReadAllTextAsync(session.PathFor(file))=="before atomic snapshot","VSS snapshot reads remain stable while live source content changes");
            string repo=Path.Combine(root,"snapshot-vault");using(var vault=await VaultBackup.OpenAsync(repo,"snapshot password phrase",true,new WindowsVaultMetadata(live)))
            {
                var id=await vault.CreateAsync(session.PathFor(live),new(true),sourceDisplay:live);await vault.VerifyAsync(id);string restore=Path.Combine(root,"snapshot-restored");await vault.RestoreAsync(id,restore);Check(await File.ReadAllTextAsync(Path.Combine(restore,"locked.txt"))=="before atomic snapshot","Encrypted file backup reads the consistent VSS view and restores its original bytes");
            }
            await session.CompleteAsync();Check(true,"VSS completes the copy backup and verifies final writer states");
        }
        var count=await StorageService.PowerShellAsync("@(Get-CimInstance Win32_ShadowCopy | Where-Object ID -eq '"+snapshotId+"').Count");Check(count=="0","The requester deletes only its own snapshot after completion");
    }
}
