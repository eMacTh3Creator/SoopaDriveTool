$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
function Encryption($letter) {
  if (!$letter) { return 'None' }
  try {
    $b=Get-CimInstance -Namespace 'root/CIMV2/Security/MicrosoftVolumeEncryption' -ClassName Win32_EncryptableVolume -Filter "DriveLetter='$($letter):'" -ErrorAction Stop
    if (!$b -or $b.EncryptionMethod -eq 0) { return 'None' }
    return 'BitLocker'
  } catch { return 'Unknown' }
}
$disks=@(Get-Disk | ForEach-Object {
 $d=$_
 $parts=@(Get-Partition -DiskNumber $d.Number -ErrorAction SilentlyContinue | ForEach-Object {
   $p=$_; $v=$p | Get-Volume -ErrorAction SilentlyContinue | Select-Object -First 1
   [ordered]@{ Number=[int]$p.PartitionNumber;Offset=[long]$p.Offset;Size=[long]$p.Size;Letter=[string]$p.DriveLetter;Label=[string]$v.FileSystemLabel;FileSystem=[string]$v.FileSystem;Free=[long]$v.SizeRemaining;Type=[string]$p.Type;IsBoot=[bool]$p.IsBoot;IsSystem=[bool]$p.IsSystem;Encryption=(Encryption $p.DriveLetter) }
 })
 [ordered]@{Number=[int]$d.Number;Name=[string]$d.FriendlyName;UniqueId=[string]$d.UniqueId;Serial=[string]$d.SerialNumber;Size=[long]$d.Size;SectorSize=[int]$d.LogicalSectorSize;Style=[string]$d.PartitionStyle;Bus=[string]$d.BusType;Health=[string]$d.HealthStatus;IsBoot=[bool]$d.IsBoot;IsSystem=[bool]$d.IsSystem;Offline=[bool]$d.IsOffline;ReadOnly=[bool]$d.IsReadOnly;Partitions=$parts}
})
ConvertTo-Json -InputObject $disks -Depth 8 -Compress
