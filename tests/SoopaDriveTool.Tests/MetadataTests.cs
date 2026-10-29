using System.IO;
using System.Security.AccessControl;
using SoopaDriveTool.Core;
using SoopaDriveTool.Windows;
partial class Program
{
    static async Task MetadataAclCheck(string root)
    {
        string source=Path.Combine(root,"different-acl-source"),destination=Path.Combine(root,"different-acl-destination");Directory.CreateDirectory(source);var security=new DirectorySecurity();security.SetSecurityDescriptorSddlForm("O:BAG:SYD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;GR;;;BU)");new DirectoryInfo(source).SetAccessControl(security);string file=Path.Combine(source,"payload.txt");await File.WriteAllTextAsync(file,"Permission test");string expected=new FileInfo(file).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access|AccessControlSections.Owner|AccessControlSections.Group);using var vault=await VaultBackup.OpenAsync(Path.Combine(root,"acl-vault"),"security passphrase test",true,new WindowsVaultMetadata(source));string id=await vault.CreateAsync(source,new(true));await vault.RestoreAsync(id,destination);string actual=new FileInfo(Path.Combine(destination,"payload.txt")).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access|AccessControlSections.Owner|AccessControlSections.Group);Console.WriteLine("Source ACL: "+expected);Console.WriteLine("Restored ACL: "+actual);Check(WindowsVaultMetadata.SecurityEquivalent(expected,actual),"Restored file retains its original owner and DACL across parents with different permissions");
    }
}
