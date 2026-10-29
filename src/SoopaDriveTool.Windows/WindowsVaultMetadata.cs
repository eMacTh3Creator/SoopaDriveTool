using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public sealed class WindowsVaultMetadata(string originalRoot):IVaultMetadata
{
    [StructLayout(LayoutKind.Sequential)]struct FileInformation{public uint Attributes;public System.Runtime.InteropServices.ComTypes.FILETIME Creation,Access,Write;public uint Serial,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct StreamInfo{public long Size;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=296)]public string Name;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string path,uint access,uint sharing,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetFileInformationByHandle(SafeFileHandle handle,out FileInformation info);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr FindFirstStreamW(string path,int level,out StreamInfo data,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool FindNextStreamW(IntPtr handle,out StreamInfo data);
    [DllImport("kernel32.dll")]static extern bool FindClose(IntPtr handle);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CreateHardLink(string path,string existing,IntPtr attributes);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool SetKernelObjectSecurity(SafeFileHandle handle,uint information,byte[] descriptor);
    const AccessControlSections Sections=AccessControlSections.Access|AccessControlSections.Owner|AccessControlSections.Group;
    public VaultMetadata Read(string path,bool directory,string root)
    {
        if(StorageService.IsAdmin)WindowsPrivileges.Enable("SeBackupPrivilege","SeRestorePrivilege");
        var attr=File.GetAttributes(path);FileSystemInfo info=directory?new DirectoryInfo(path):new FileInfo(path);
        if((attr&FileAttributes.Encrypted)!=0)throw new IOException("Use a full NTFS/WIM image for EFS-encrypted files: "+path);
        if((attr&FileAttributes.ReparsePoint)!=0)
        {
            string target=info.LinkTarget??throw new IOException("Unsupported reparse-point type: "+path);var liveParent=Path.GetDirectoryName(Path.Combine(originalRoot,Path.GetRelativePath(root,path)))!;target=Path.GetFullPath(Path.IsPathRooted(target)?target:Path.Combine(liveParent,target));
            if(!BackupEngine.IsInside(target,originalRoot)||target.Equals(originalRoot,StringComparison.OrdinalIgnoreCase))throw new IOException("A link leaves the selected source. Exclude it or choose a source containing its target: "+path);
            return new(null,(int)attr,info.CreationTimeUtc,Path.GetRelativePath(originalRoot,target).Replace('\\','/'),directory);
        }
        string sddl=directory?new DirectoryInfo(path).GetAccessControl(Sections).GetSecurityDescriptorSddlForm(Sections):new FileInfo(path).GetAccessControl(Sections).GetSecurityDescriptorSddlForm(Sections);string? group=null;
        if(!directory){using var handle=CreateFile(path,0x80,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero);if(handle.IsInvalid||!GetFileInformationByHandle(handle,out var native))throw new IOException("Cannot read stable NTFS file identity.");if(native.Links>1)group=$"{native.Serial:x8}:{native.IndexHigh:x8}{native.IndexLow:x8}";}
        return new(sddl,(int)attr,info.CreationTimeUtc,null,false,group);
    }
    public IEnumerable<(string Name,string Path)> Streams(string path)
    {
        var result=new List<(string,string)>();IntPtr handle=FindFirstStreamW(path,0,out var item,0);if(handle==new IntPtr(-1)){int error=Marshal.GetLastWin32Error();if(error is 38 or 87 or 1)return result;throw new IOException("Cannot enumerate NTFS streams: "+error);}
        try{do{if(item.Name!="::$DATA"&&item.Name.StartsWith(':')&&item.Name.EndsWith(":$DATA",StringComparison.Ordinal)){string name=item.Name[1..^6];result.Add((name,path+":"+name));}}while(FindNextStreamW(handle,out item));int error=Marshal.GetLastWin32Error();if(error!=38)throw new IOException("Alternate-stream enumeration failed: "+error);}finally{FindClose(handle);}return result;
    }
    public void Restore(string path,VaultMetadata metadata,bool directory)
    {
        if(StorageService.IsAdmin)WindowsPrivileges.Enable("SeRestorePrivilege");
        if(metadata.CreatedUtc.HasValue){if(directory)Directory.SetCreationTimeUtc(path,metadata.CreatedUtc.Value);else File.SetCreationTimeUtc(path,metadata.CreatedUtc.Value);}
        if(metadata.Security!=null)
        {
            // Restore the complete recorded descriptor on the retained object.
            // SetNamedSecurityInfo/.NET ACL setters recalculate inherited ACEs
            // from the new parent, silently dropping permissions from the backup.
            var security=new FileSecurity();security.SetSecurityDescriptorSddlForm(metadata.Security,Sections);using var handle=CreateFile(path,0x000c0000,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero);
            if(handle.IsInvalid||!SetKernelObjectSecurity(handle,7,security.GetSecurityDescriptorBinaryForm()))throw new IOException("Cannot restore the recorded owner, group and DACL: "+Marshal.GetLastWin32Error());
        }
        var attrs=(FileAttributes)metadata.Attributes&~(FileAttributes.ReparsePoint|FileAttributes.Encrypted|FileAttributes.Compressed|FileAttributes.SparseFile|FileAttributes.Device);File.SetAttributes(path,attrs==0?FileAttributes.Normal:attrs);
    }
    public static bool SecurityEquivalent(string expected,string actual)
    {
        RawSecurityDescriptor a=new(expected),b=new(actual);byte[] Acl(RawAcl? acl){if(acl==null)return [];var bytes=new byte[acl.BinaryLength];acl.GetBinaryForm(bytes,0);return bytes;}
        // Windows may normalize SE_DACL_AUTO_INHERITED during a raw descriptor
        // restore. Compare the actual ordered ACEs, protection, owner and group.
        return Equals(a.Owner,b.Owner)&&Equals(a.Group,b.Group)&&(a.ControlFlags&ControlFlags.DiscretionaryAclProtected)==(b.ControlFlags&ControlFlags.DiscretionaryAclProtected)&&Acl(a.DiscretionaryAcl).SequenceEqual(Acl(b.DiscretionaryAcl));
    }
    public void MakeLink(string path,VaultMetadata metadata,string destination)
    {
        if(StorageService.IsAdmin)WindowsPrivileges.Enable("SeCreateSymbolicLinkPrivilege");
        BackupEngine.ValidateRelative(metadata.LinkTarget!);string target=Path.GetFullPath(Path.Combine(destination,metadata.LinkTarget!.Replace('/',Path.DirectorySeparatorChar)));if(!BackupEngine.IsInside(target,destination))throw new IOException("Unsafe restored link.");if(metadata.DirectoryLink)Directory.CreateSymbolicLink(path,target);else File.CreateSymbolicLink(path,target);
    }
    public void MakeHardlink(string path,string existing){if(!CreateHardLink(path,existing,IntPtr.Zero))throw new IOException("Cannot restore NTFS hardlink: "+Marshal.GetLastWin32Error());}
}
