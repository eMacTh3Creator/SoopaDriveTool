using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace SoopaDriveTool.Windows;
internal static class WindowsPrivileges
{
    [StructLayout(LayoutKind.Sequential)]struct Luid{public uint Low;public int High;}
    [StructLayout(LayoutKind.Sequential)]struct Privilege{public uint Count;public Luid Id;public uint Attributes;}
    [DllImport("kernel32.dll")]static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool OpenProcessToken(IntPtr process,uint rights,out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool LookupPrivilegeValue(string? system,string name,out Luid id);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token,bool disable,ref Privilege privileges,uint length,IntPtr previous,IntPtr returned);
    static readonly HashSet<string> enabled=[];
    internal static void Enable(params string[] names)
    {
        lock(enabled){
        if(!OpenProcessToken(GetCurrentProcess(),0x28,out var token))throw new Win32Exception(Marshal.GetLastWin32Error());using(token)foreach(var name in names){if(enabled.Contains(name))continue;if(!LookupPrivilegeValue(null,name,out var id))throw new Win32Exception(Marshal.GetLastWin32Error());var p=new Privilege{Count=1,Id=id,Attributes=2};Marshal.SetLastPInvokeError(0);if(!AdjustTokenPrivileges(token,false,ref p,0,IntPtr.Zero,IntPtr.Zero)||Marshal.GetLastWin32Error()!=0)throw new UnauthorizedAccessException("The current account does not have "+name+". Restart as administrator.");enabled.Add(name);}
        }
    }
}
