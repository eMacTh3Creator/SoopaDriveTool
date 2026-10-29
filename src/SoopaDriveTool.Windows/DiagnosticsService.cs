using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using SoopaDriveTool.Core;
namespace SoopaDriveTool.Windows;
public record TemperaturePoint(DateTime Utc,int Disk,string Identity,double Celsius);
public static class DiagnosticsService
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint create,uint flags,IntPtr template);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr FindFirstVolume(System.Text.StringBuilder name,int size);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool FindNextVolume(IntPtr search,System.Text.StringBuilder name,int size);
    [DllImport("kernel32.dll")]static extern bool FindVolumeClose(IntPtr search);
    static void Ioctl(SafeFileHandle h,uint code,byte[]? input=null){if(!DiskAccess.DeviceIoControl(h,code,input,input?.Length??0,null,0,out _,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());}
    public static async Task<string> NativeEraseAsync(DiskInfo disk,string method,IProgress<DriveProgress>? progress=null)
    {
        if(method is not ("CryptoErase" or "BlockErase"))throw new IOException("Choose crypto erase or block erase.");if(disk.Bus!="NVMe")throw new IOException("Windows NVMe sanitize requires a directly attached NVMe device. ATA security erase is offered from recovery media; USB/RAID bridges may not pass these commands.");
        var current=(await StorageService.InventoryAsync()).Single(d=>d.Number==disk.Number);QueuePlanner.AssertUnchanged(disk,current);using var raw=DiskAccess.Open(disk,true,false);var held=new List<SafeFileHandle>();
        try
        {
            var name=new System.Text.StringBuilder(1024);IntPtr search=FindFirstVolume(name,name.Capacity);
            if(search==new IntPtr(-1))throw new Win32Exception(Marshal.GetLastWin32Error());
            try{do{using var probe=CreateFile(name.ToString().TrimEnd('\\'),0x80000000,3,IntPtr.Zero,3,0,IntPtr.Zero);if(probe.IsInvalid)continue;var ext=new byte[65536];if(!DiskAccess.DeviceIoControl(probe,0x560000,null,0,ext,ext.Length,out int got,IntPtr.Zero))continue;int count=BitConverter.ToInt32(ext);if(count<1||count>1024||got<8+24*count)throw new IOException("Invalid volume extents.");bool selected=false;for(int i=0;i<count;i++)selected|=BitConverter.ToInt32(ext,8+i*24)==disk.Number;if(!selected)continue;if(count!=1)throw new IOException("Native erase refuses a volume spanning more than one disk.");var volume=CreateFile(name.ToString().TrimEnd('\\'),0xc0000000,3,IntPtr.Zero,3,0,IntPtr.Zero);if(volume.IsInvalid){volume.Dispose();throw new Win32Exception(Marshal.GetLastWin32Error());}try{Ioctl(volume,0x90018);Ioctl(volume,0x90020);held.Add(volume);}catch{volume.Dispose();throw;}}while(FindNextVolume(search,name,name.Capacity));}finally{FindVolumeClose(search);}
            // STORAGE_SANITIZE_METHOD: Default=0, BlockErase=1, CryptoErase=2.
            // These are the Windows enum values, not NVMe command action codes.
            byte[] options=new byte[16];BitConverter.GetBytes(16u).CopyTo(options,0);BitConverter.GetBytes(16u).CopyTo(options,4);BitConverter.GetBytes(86400u).CopyTo(options,8);BitConverter.GetBytes(method=="CryptoErase"?2u:1u).CopyTo(options,12);
            progress?.Report(new("Controller erase in progress",null,"Windows and the device control this operation. A percentage is unavailable; do not disconnect power."));
            await Task.Run(()=>Ioctl(raw.Handle,0x2d9640,options));progress?.Report(new("Controller command completed",100,"The storage driver reported command completion. Device-specific remapped-cell guarantees require the manufacturer's documentation."));
            await StorageService.PowerShellAsync("Update-HostStorageCache");return method+" completed through Windows IOCTL_STORAGE_REINITIALIZE_MEDIA. This is a controller completion report, not an independently certified purge.";
        }
        finally{foreach(var h in held){try{Ioctl(h,0x9001c);}catch{}h.Dispose();}}
    }
    public static async Task FreeSpaceWipeAsync(DiskInfo disk,PartitionInfo partition,bool thorough,IProgress<DriveProgress>? p,CancellationToken ct)
    {
        if(disk.Protected||partition.Protected||partition.Encryption!="None"||partition.Letter.Length!=1||disk.Offline)throw new IOException("Choose a mounted unencrypted data volume.");var now=(await StorageService.InventoryAsync(ct)).Single(d=>d.Number==disk.Number);QueuePlanner.AssertUnchanged(disk,now);string root=partition.Letter+@":\",folder=Path.Combine(root,".soopa-free-space-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var files=new List<string>();long reserve=256L*1024*1024,total=Math.Max(0,new DriveInfo(root).AvailableFreeSpace-reserve),done=0;byte[] buffer=new byte[1024*1024];int passes=thorough?3:1;
        try
        {
            // Keep each file below FAT32's maximum and preserve a free-space reserve.
            while(done<total){ct.ThrowIfCancellationRequested();long size=Math.Min(1024L*1024*1024,total-done);if(new DriveInfo(root).AvailableFreeSpace<size+reserve)break;string file=Path.Combine(folder,files.Count+".tmp");files.Add(file);await using var output=new FileStream(file,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,buffer.Length,FileOptions.WriteThrough);output.SetLength(size);for(int pass=0;pass<passes;pass++){output.Position=0;long written=0;while(written<size){ct.ThrowIfCancellationRequested();int n=(int)Math.Min(buffer.Length,size-written);if(thorough&&pass==0)System.Security.Cryptography.RandomNumberGenerator.Fill(buffer);else Array.Fill(buffer,thorough&&pass==1?(byte)255:(byte)0);await output.WriteAsync(buffer.AsMemory(0,n),ct);written+=n;p?.Report(new("Overwriting free space",100.0*(done+(pass+written/(double)size)*size/passes)/Math.Max(1,total),$"File {files.Count} · pass {pass+1}/{passes} · {Data.Size(done+written)}"));}output.Flush(true);}done+=size;}
        }
        finally{foreach(var file in files)if(File.Exists(file))File.Delete(file);if(Directory.Exists(folder))Directory.Delete(folder);}
        p?.Report(new("Free-space overwrite completed",100,$"Processed {Data.Size(done)} while retaining a {Data.Size(reserve)} reserve. Allocated files remain; filesystem slack and SSD remapped cells are outside this operation."));
    }
    public static async Task SaveTemperatureAsync(DiskInfo disk,SmartReport report)
    {
        var field=report.Fields.FirstOrDefault(f=>f.Name.EndsWith("temperature.current",StringComparison.OrdinalIgnoreCase)||f.Name.EndsWith("temperature",StringComparison.OrdinalIgnoreCase));if(field==null||!double.TryParse(field.Value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double c)||c is <-100 or >250)return;
        string dir=Path.Combine(Data.Home,"temperatures");Directory.CreateDirectory(dir);string path=Path.Combine(dir,disk.Identity+".json");var rows=File.Exists(path)?JsonSerializer.Deserialize<List<TemperaturePoint>>(await File.ReadAllTextAsync(path),Data.Json)??[]:[];rows.Add(new(DateTime.UtcNow,disk.Number,disk.Identity,c));await Data.SaveAsync(path,rows.TakeLast(10000).ToList());
    }
    public static Task<string> TopologyAsync()=>StorageService.PowerShellAsync("$r=[ordered]@{Disks=@(Get-CimInstance Win32_DiskDrive|Select-Object Index,Model,SerialNumber,InterfaceType,PNPDeviceID,Size);Volumes=@(Get-CimInstance Win32_Volume|Select-Object DriveLetter,Label,DeviceID,FileSystem,Capacity,FreeSpace);PhysicalDisks=@(Get-PhysicalDisk|Select-Object DeviceId,FriendlyName,SerialNumber,BusType,MediaType,HealthStatus,OperationalStatus,Size);Pools=@(Get-StoragePool|Select-Object FriendlyName,IsPrimordial,HealthStatus,Size,AllocatedSize);VirtualDisks=@(Get-VirtualDisk|Select-Object FriendlyName,ResiliencySettingName,HealthStatus,OperationalStatus,Size,FootprintOnPool)};ConvertTo-Json -InputObject $r -Depth 8");
}
