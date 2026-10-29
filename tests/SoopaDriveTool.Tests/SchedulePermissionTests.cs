using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using SoopaDriveTool.Windows;
partial class Program
{
    static void SchedulePermissions(string root)
    {
        WindowsPrivileges.Enable("SeRestorePrivilege");
        string jobs=Path.Combine(root,"secure-schedule","SystemJobs");BackupScheduleService.EnsureSystemStorage(jobs);
        foreach(string path in new[]{Path.GetDirectoryName(jobs)!,jobs}){
            var acl=new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner|AccessControlSections.Access);
            Check(acl.AreAccessRulesProtected&&acl.GetOwner(typeof(SecurityIdentifier)).Value=="S-1-5-32-544"&&acl.GetAccessRules(true,true,typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().All(r=>r.IdentityReference.Value is "S-1-5-18" or "S-1-5-32-544"),"SYSTEM schedule directory and parent are created atomically with protected administrator ownership");
        }
        DirectorySecurity Acl(WellKnownSidType owner,bool writable){var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);acl.SetOwner(new SecurityIdentifier(owner,null));foreach(var sid in new[]{WellKnownSidType.LocalSystemSid,WellKnownSidType.BuiltinAdministratorsSid})acl.AddAccessRule(new(new SecurityIdentifier(sid,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));if(writable)acl.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));return acl;}
        string attacker=Path.Combine(root,"preexisting-untrusted-owner");new DirectoryInfo(attacker).Create(Acl(WellKnownSidType.BuiltinUsersSid,true));
        Reject(()=>BackupScheduleService.EnsureSystemStorage(Path.Combine(attacker,"SystemJobs")),"A precreated non-administrator-owned parent cannot become SYSTEM schedule storage");
        Check(!Directory.Exists(Path.Combine(attacker,"SystemJobs"))&&new DirectoryInfo(attacker).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier)).Value=="S-1-5-32-545","Rejecting untrusted schedule storage preserves its contents and ownership");
        string writable=Path.Combine(root,"preexisting-writable-parent");new DirectoryInfo(writable).Create(Acl(WellKnownSidType.BuiltinAdministratorsSid,true));
        Reject(()=>BackupScheduleService.EnsureSystemStorage(Path.Combine(writable,"SystemJobs")),"An administrator-owned parent with user write access is rejected before creating a SYSTEM job");
    }
}
