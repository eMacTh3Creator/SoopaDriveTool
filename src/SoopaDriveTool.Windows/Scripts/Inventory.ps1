$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
function Encryption($partition,$volume,$offline) {
  try {
    $instances=@();$queried=$false
    if ($partition.DriveLetter) {
      $queried=$true
      $instances=@(Get-CimInstance -Namespace 'root/CIMV2/Security/MicrosoftVolumeEncryption' -ClassName Win32_EncryptableVolume -Filter "DriveLetter='$($partition.DriveLetter):'" -ErrorAction Stop)
    } else {
      $ids=@($partition.AccessPaths)+@($volume.Path,$volume.UniqueId)
      foreach ($id in @($ids | Where-Object { $_ -match '^\\\\\?\\Volume\{' } | Select-Object -Unique)) {
        $queried=$true
        $escaped=([string]$id).Replace('\','\\').Replace("'","\'")
        $instances+=@(Get-CimInstance -Namespace 'root/CIMV2/Security/MicrosoftVolumeEncryption' -ClassName Win32_EncryptableVolume -Filter ("DeviceID='"+$escaped+"'") -ErrorAction Stop)
      }
    }
    if ($instances.Count -eq 0 -and $queried -and !$offline) { return 'None' }
    if ($instances.Count -ne 1) { return 'Unknown' }
    $state=Invoke-CimMethod -InputObject $instances[0] -MethodName GetEncryptionMethod -ErrorAction Stop
    if ($null -eq $state.ReturnValue -or [uint32]$state.ReturnValue -ne 0 -or $null -eq $state.EncryptionMethod) { return 'Unknown' }
    if ([uint32]$state.EncryptionMethod -eq 0) { return 'None' }
    return 'BitLocker'
  } catch { return 'Unknown' }
}
function Usb-Bus($storage,$legacy) {
  if ([string]$storage -eq 'USB' -or [string]$legacy.InterfaceType -eq 'USB' -or [string]$legacy.PNPDeviceID -match '^USB(STOR)?\\') { return 'USB' }
  if ($storage -and [string]$storage -notin @('Unknown','0')) { return [string]$storage }
  if ($legacy.InterfaceType) { return [string]$legacy.InterfaceType }
  return 'Unknown'
}
function Volume-Details($partition) {
  try { $v=$partition | Get-Volume -ErrorAction Stop | Select-Object -First 1;if ($v) { return $v } } catch {}
  # Some removable-media controllers expose their mounted filesystem only
  # through the Win32 provider. Keep its letter and filesystem visible.
  if ($partition.DriveLetter) {
    try {
      $v=Get-CimInstance -ClassName Win32_LogicalDisk -Filter "DeviceID='$($partition.DriveLetter):'" -ErrorAction Stop | Select-Object -First 1
      if ($v) { return [pscustomobject]@{FileSystemLabel=$v.VolumeName;FileSystem=$v.FileSystem;SizeRemaining=$v.FreeSpace} }
    } catch {}
  }
  return $null
}
$storage=New-Object 'System.Collections.Generic.List[object]'
$legacy=New-Object 'System.Collections.Generic.List[object]'
$storageError=$null;$legacyError=$null
try { Get-Disk -ErrorAction Stop | ForEach-Object { $storage.Add($_) } } catch { $storageError=$_.Exception.Message }
try { Get-CimInstance -ClassName Win32_DiskDrive -ErrorAction Stop | ForEach-Object { $legacy.Add($_) } } catch { $legacyError=$_.Exception.Message }
if ($storage.Count -eq 0 -and $legacy.Count -eq 0 -and ($storageError -or $legacyError)) { throw 'Windows could not enumerate connected disks. Check the storage controller and reconnect the drive, then refresh.' }
$disks=New-Object 'System.Collections.Generic.List[object]'
foreach ($d in $storage) {
  $physical=$legacy | Where-Object { [int]$_.Index -eq [int]$d.Number } | Select-Object -First 1
  $warning='';$parts=@();$providerParts=@()
  try {
    # A RAW or empty disk can legitimately have no MSFT_Partition objects.
    if ($null -eq $d.NumberOfPartitions -or [int]$d.NumberOfPartitions -gt 0) { $providerParts=@(Get-Partition -DiskNumber $d.Number -ErrorAction Stop) }
    if ($null -ne $d.NumberOfPartitions -and $providerParts.Count -ne [int]$d.NumberOfPartitions) { $warning='Windows returned an incomplete partition list. Disk changes are disabled until a refresh succeeds.' }
  } catch { $warning='Windows could not read the partition list. Disk changes are disabled until a refresh succeeds.' }
  foreach ($p in $providerParts) {
    $v=Volume-Details $p
    $parts+=[ordered]@{Number=[int]$p.PartitionNumber;Offset=[long]$p.Offset;Size=[long]$p.Size;Letter=[string]$p.DriveLetter;Label=[string]$v.FileSystemLabel;FileSystem=[string]$v.FileSystem;Free=[long]$v.SizeRemaining;Type=[string]$p.Type;IsBoot=[bool]$p.IsBoot;IsSystem=[bool]$p.IsSystem;Encryption=(Encryption $p $v $d.IsOffline)}
  }
  $name=[string]$d.FriendlyName;if (!$name) { $name=[string]$physical.Model };if (!$name) { $name='Disk '+$d.Number }
  $disks.Add([ordered]@{Number=[int]$d.Number;Name=$name;UniqueId=[string]$d.UniqueId;Serial=[string]$d.SerialNumber;Size=[long]$d.Size;SectorSize=[int]$d.LogicalSectorSize;Style=[string]$d.PartitionStyle;Bus=(Usb-Bus $d.BusType $physical);Health=[string]$d.HealthStatus;IsBoot=[bool]$d.IsBoot;IsSystem=[bool]$d.IsSystem;Offline=[bool]$d.IsOffline;ReadOnly=[bool]$d.IsReadOnly;InventoryWarning=$warning;Partitions=$parts})
}
$known=@($disks | ForEach-Object {$_.Number})
foreach ($d in $legacy) {
  if ($null -eq $d.Index -or [int]$d.Index -in $known) { continue }
  # Never assume a disk omitted by Get-Disk is dynamic: removable USB media
  # and controllers with incomplete Storage support also appear here.
  $parts=@()
  try {
    foreach ($p in @(Get-CimAssociatedInstance -InputObject $d -Association Win32_DiskDriveToDiskPartition -ErrorAction Stop)) {
      $volumes=@();try { $volumes=@(Get-CimAssociatedInstance -InputObject $p -Association Win32_LogicalDiskToPartition -ErrorAction Stop) } catch {}
      $v=$volumes | Select-Object -First 1;$letter=([string]$v.DeviceID).TrimEnd(':')
      $parts+=[ordered]@{Number=([int]$p.Index+1);Offset=[long]$p.StartingOffset;Size=[long]$p.Size;Letter=$letter;Label=[string]$v.VolumeName;FileSystem=[string]$v.FileSystem;Free=[long]$v.FreeSpace;Type=[string]$p.Type;IsBoot=($letter -eq ([string]$env:SystemDrive).TrimEnd(':') -and $letter -ne '');IsSystem=[bool]$p.BootPartition;Encryption='Unknown'}
    }
  } catch {}
  $name=[string]$d.Model;if (!$name) { $name='Disk '+$d.Index }
  $sector=[int]$d.BytesPerSector;if ($sector -le 0) { $sector=512 }
  $disks.Add([ordered]@{Number=[int]$d.Index;Name=$name;UniqueId=[string]$d.PNPDeviceID;Serial=[string]$d.SerialNumber;Size=[long]$d.Size;SectorSize=$sector;Style='Unavailable';Bus=(Usb-Bus '' $d);Health='Unknown';IsBoot=(@($parts | Where-Object {$_.IsBoot}).Count -gt 0);IsSystem=(@($parts | Where-Object {$_.IsSystem}).Count -gt 0);Offline=$false;ReadOnly=$false;InventoryWarning='Windows Storage does not expose this disk. Available device and volume details are shown; disk changes are disabled.';Partitions=$parts})
}
ConvertTo-Json -InputObject @($disks | Sort-Object { $_.Number }) -Depth 8 -Compress
