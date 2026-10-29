using System.IO;
using System.Text.Json;
using SoopaDriveTool.Core;
using SoopaDriveTool.Windows;
partial class Program
{
    static async Task InventoryFixtureChecks()
    {
        var assembly=typeof(StorageService).Assembly;
        using var resource=assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n=>n.EndsWith(".Inventory.ps1")))!;
        using var reader=new StreamReader(resource);string inventory=await reader.ReadToEndAsync();
        const string mocks="""
        function Get-Disk {
          [CmdletBinding()]param()
          if($scenario -in @('StorageFailure','BothFailure')){throw 'Storage unavailable'}
          if($scenario -eq 'Empty'){return}
          foreach($number in @(4,8,9)) {
            [pscustomobject]@{Number=$number;FriendlyName=('Disk '+$number);UniqueId=('ID'+$number);SerialNumber=('SERIAL'+$number);Size=4GB;LogicalSectorSize=512;PartitionStyle=$(if($number -eq 8){'RAW'}else{'GPT'});BusType='USB';HealthStatus='Healthy';IsBoot=$false;IsSystem=$false;IsOffline=($scenario -eq 'OfflineNoCrypto');IsReadOnly=$false;NumberOfPartitions=$(if($number -eq 8){0}else{1})}
          }
        }
        function Get-Partition {
          [CmdletBinding()]param([int]$DiskNumber)
          if($DiskNumber -eq 9){throw 'Device disconnected during partition enumeration'}
          [pscustomobject]@{PartitionNumber=1;DiskNumber=$DiskNumber;Offset=1MB;Size=3GB;DriveLetter=$(if($scenario -in @('GuidOnly','GuidMissing','NoVolumePath','OfflineNoCrypto')){$null}else{'E'});AccessPaths=$(if($scenario -in @('NoVolumePath','OfflineNoCrypto')){$null}else{@('\\?\Volume{11111111-1111-1111-1111-111111111111}\')});Type='Basic';IsBoot=$false;IsSystem=$false}
        }
        function Get-Volume {
          [CmdletBinding()]param([Parameter(ValueFromPipeline)]$InputObject)
          process {throw 'Volume provider unavailable for this bridge'}
        }
        function Get-CimInstance {
          [CmdletBinding()]param([string]$ClassName,[string]$Namespace,[string]$Filter)
          if($ClassName -eq 'Win32_DiskDrive') {
            if($scenario -in @('CimFailure','BothFailure')){throw 'Win32 unavailable'}
            if($scenario -eq 'Empty'){return}
            foreach($number in @(4,7)) {[pscustomobject]@{Index=$number;Model=$(if($number -eq 4){'External hard drive'}else{'USB flash drive'});PNPDeviceID=('USBSTOR\DEVICE'+$number);InterfaceType='USB';SerialNumber=('SERIAL'+$number);Size=4GB;BytesPerSector=512;MediaType=$(if($number -eq 4){'Fixed hard disk media'}else{'Removable Media'})}}
          } elseif($ClassName -eq 'Win32_LogicalDisk') {
            [pscustomobject]@{DeviceID='E:';VolumeName='External';FileSystem='NTFS';FreeSpace=2GB}
          } elseif($ClassName -eq 'Win32_EncryptableVolume') {
            if($scenario -in @('CryptoMissing','GuidMissing','OfflineNoCrypto')){return};if($scenario -eq 'CryptoProviderFailure'){throw 'Encryption provider unavailable'};[pscustomobject]@{DeviceID='\\?\Volume{11111111-1111-1111-1111-111111111111}\';DriveLetter='E:'}
          } else {throw ('Unexpected CIM query: '+$ClassName)}
        }
        function Invoke-CimMethod {
          [CmdletBinding()]param($InputObject,[string]$MethodName)
          if($MethodName -ne 'GetEncryptionMethod'){throw 'Unexpected encryption method'}
          if($scenario -eq 'CryptoMethodFailure'){return [pscustomobject]@{ReturnValue=5;EncryptionMethod=0}}
          if($scenario -eq 'CryptoMalformed'){return [pscustomobject]@{ReturnValue=0}}
          [pscustomobject]@{ReturnValue=0;EncryptionMethod=$(if($scenario -eq 'BitLocker'){7}else{0})}
        }
        function Get-CimAssociatedInstance {
          [CmdletBinding()]param($InputObject,[string]$Association)
          if($Association -eq 'Win32_DiskDriveToDiskPartition') {
            [pscustomobject]@{Index=0;DiskIndex=$InputObject.Index;StartingOffset=1MB;Size=3GB;Type='Installable File System';BootPartition=$false}
          } elseif($Association -eq 'Win32_LogicalDiskToPartition') {
            [pscustomobject]@{DeviceID='F:';VolumeName='Portable';FileSystem='exFAT';FreeSpace=1GB}
          } else {throw 'Unexpected association'}
        }
        """;
        async Task<List<DiskInfo>> Read(string scenario)=>JsonSerializer.Deserialize<List<DiskInfo>>(await StorageService.PowerShellAsync("$scenario='"+scenario+"';"+mocks+inventory),Data.Json)??[];
        var disks=await Read("Mixed");
        var external=disks.Single(d=>d.Number==4);var removable=disks.Single(d=>d.Number==7);
        Check(disks.Count==4&&disks.Select(d=>d.Number).Distinct().Count()==4,"USB fixed and removable drives retain physical disk numbers without duplicates");
        Check(external.Bus=="USB"&&external.UniqueId=="ID4"&&external.InventoryWarning==""&&external.Partitions.Single().Letter=="E","Provider-supported USB hard drive remains eligible for normal operations");
        Check(external.Partitions.Single().FileSystem=="NTFS"&&external.Partitions.Single().Free==2L*1024*1024*1024,"USB volume details fall back to Win32 when Get-Volume fails");
        Check(removable.Bus=="USB"&&removable.Style=="Unavailable"&&removable.InventoryWarning.Length>0&&removable.Partitions.Single().FileSystem=="exFAT"&&removable.Partitions.Single().Letter=="F","Omitted USB removable drive displays available volume details without being called dynamic");
        Check(disks.Single(d=>d.Number==8).Partitions.Count==0&&disks.Single(d=>d.Number==8).InventoryWarning=="","Empty RAW USB disk does not require a partition-provider result");
        Check(disks.Single(d=>d.Number==9).InventoryWarning.Length>0,"One disconnected or unreadable disk does not hide other disks");
        var fallback=await Read("StorageFailure");Check(fallback.Count==2&&fallback.All(d=>d.InventoryWarning.Length>0),"Win32 devices remain visible when the Storage provider fails");
        var storageOnly=await Read("CimFailure");Check(storageOnly.Count==3&&storageOnly.Single(d=>d.Number==4).InventoryWarning=="","Storage inventory remains available when the legacy provider fails");
        Check((await Read("Empty")).Count==0,"Empty inventory remains a JSON array");
        await RejectAsync(()=>Read("BothFailure"),"Failure of both providers is reported instead of showing an empty inventory");
        Check(external.Partitions.Single().Encryption=="None","Encryption requires a successful GetEncryptionMethod result");
        Check((await Read("GuidOnly")).Single(d=>d.Number==4).Partitions.Single().Encryption=="None","Letterless online volumes can query encryption through their volume GUID path");
        Check((await Read("BitLocker")).Single(d=>d.Number==4).Partitions.Single().Encryption=="BitLocker","Nonzero encryption methods stay protected as BitLocker");
        Check((await Read("CryptoMissing")).Single(d=>d.Number==4).Partitions.Single().Encryption=="None","A successful query with no encryptable-volume record on a mounted drive preserves its eligibility");
        Check((await Read("GuidMissing")).Single(d=>d.Number==4).Partitions.Single().Encryption=="None","A successful query with no encryptable-volume record also supports a mounted volume GUID");
        Check((await Read("NoVolumePath")).Single(d=>d.Number==4).Partitions.Single().Encryption=="Unknown","No drive letter or volume GUID remains unknown");
        Check((await Read("OfflineNoCrypto")).Single(d=>d.Number==4).Partitions.Single().Encryption=="Unknown","An offline disk without a usable volume path requires native cached evidence");
        foreach(string scenario in new[]{"CryptoProviderFailure","CryptoMethodFailure","CryptoMalformed"})
            Check((await Read(scenario)).Single(d=>d.Number==4).Partitions.Single().Encryption=="Unknown","Missing or failed encryption evidence remains unknown: "+scenario);
        var d=Disk();var unavailable=d with{InventoryWarning="Incomplete provider result"};
        Reject(()=>OperationPolicy.Validate(DiskOperation.For(unavailable,OperationKind.ReadWrite),unavailable),"Incomplete inventory cannot bypass write protection by clearing read-only");
        Reject(()=>OperationPolicy.Validate(DiskOperation.For(unavailable,OperationKind.QuickWipeDisk),unavailable),"Incomplete inventory blocks quick wiping");
        Reject(()=>DiskAccess.Open(unavailable,true,false),"Direct disk writes reject incomplete inventory before opening a handle");
        Reject(()=>QueuePlanner.AssertUnchanged(d,unavailable),"Provider failure during an operation stops revalidation");
        var target=d with{Number=9,UniqueId="target",InventoryWarning="Incomplete target"};
        Reject(()=>OperationPolicy.Validate(DiskOperation.For(d,OperationKind.CloneDisk) with{TargetDiskNumber=target.Number,TargetIdentity=target.Identity,TargetLayout=target.Layout},d,target),"Clone destination with incomplete inventory is rejected");
    }
}
