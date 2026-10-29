using System.IO;
using System.Text.Json;
using SoopaDriveTool.Core;
using SoopaDriveTool.Windows;
partial class Program
{
    static async Task VerifyScheduledFixture(string response)
    {
        if(Environment.MachineName!="SDTLAB")throw new IOException("Owned spare only.");
        using var data=JsonDocument.Parse(await File.ReadAllTextAsync(response));string repository=data.RootElement.GetProperty("Repository").GetString()!,id=data.RootElement.GetProperty("EncryptedSnapshot").GetString()!;
        const string source=@"C:\SoopaScheduleLab\source",destination=@"C:\SoopaScheduleLab\restored";
        using var vault=await VaultBackup.OpenAsync(repository,"owned schedule qualification passphrase",metadata:new WindowsVaultMetadata(source));await vault.VerifyAsync(id);await vault.RestoreAsync(id,destination);
        string original=Path.Combine(source,"proof.txt"),restored=Path.Combine(destination,"proof.txt");
        Check(await File.ReadAllTextAsync(original)==await File.ReadAllTextAsync(restored)&&await File.ReadAllTextAsync(restored+":Proof")=="Scheduled stream\r\n","Installed SYSTEM VSS backup restores authenticated file content and its NTFS named stream from SMB");
        string before=System.IO.FileSystemAclExtensions.GetAccessControl(new FileInfo(original)).GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.Access|System.Security.AccessControl.AccessControlSections.Owner|System.Security.AccessControl.AccessControlSections.Group),after=System.IO.FileSystemAclExtensions.GetAccessControl(new FileInfo(restored)).GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.Access|System.Security.AccessControl.AccessControlSections.Owner|System.Security.AccessControl.AccessControlSections.Group);
        Check(WindowsVaultMetadata.SecurityEquivalent(before,after),"Scheduled network backup preserves original owner and DACL on restore");
        var disk=(await StorageService.InventoryAsync()).First(d=>d.Name.Contains("dynamic-test"));var report=await SmartService.ReadAsync(disk);Check(report.Raw.ValueKind==JsonValueKind.Object&&report.OriginalText.Length>0,"Actual VM SMART collection retains complete original device output");
        try{string result=await SmartService.SelfTestAsync(disk,"short");Check(result.Contains("submitting a test is not a health pass"),"A submitted firmware self-test is explicitly distinct from a health pass");}catch(IOException ex){Check(ex.Message.Contains("did not accept"),"Actual unsupported VM self-test is rejected with the controller output");}
    }
}
