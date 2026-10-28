$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
function Report-Drive($phase,$percent,$message) {
 [Console]::Out.WriteLine('__SDT_PROGRESS__'+(@{Phase=$phase;Percent=$percent;Message=$message} | ConvertTo-Json -Compress))
}
function Wait-StorageJob($job,$phase) {
 try {
  while($job.State -in @('Running','NotStarted','Blocked')) {
   $r=@($job.Progress) | Select-Object -Last 1
   $percent=$null;if($r -and $r.PercentComplete -ge 0){$percent=[double]$r.PercentComplete}
   Report-Drive $phase $percent 'Windows storage provider is working. Percentage is shown only when the provider reports one.'
   Start-Sleep -Milliseconds 400
  }
  Receive-Job -Job $job -Wait -ErrorAction Stop
 } finally { Remove-Job -Job $job -Force -ErrorAction SilentlyContinue }
}
Report-Drive 'Preflight' $null 'Validating the selected disk and partition again.'
# Request is JSON data, never executable user text. Script is an embedded resource.
$o=$request.Operation; $expected=$request.Disk
$d=Get-Disk -Number $o.DiskNumber
if ($d.IsBoot -or $d.IsSystem) { throw 'The running boot/system disk is protected.' }
if ([string]$d.UniqueId -cne $expected.UniqueId -or [string]$d.SerialNumber -cne $expected.Serial -or [long]$d.Size -ne $expected.Size -or [int]$d.LogicalSectorSize -ne $expected.SectorSize) { throw 'Disk identity changed.' }
$parts=@(Get-Partition -DiskNumber $d.Number -ErrorAction SilentlyContinue)
if (@($parts | Where-Object {$_.IsBoot -or $_.IsSystem}).Count -gt 0) { throw 'A boot/system partition is protected.' }
if ($parts.Count -ne @($expected.Partitions).Count -or [string]$d.PartitionStyle -ne $expected.Style -or [bool]$d.IsOffline -ne $expected.Offline -or [bool]$d.IsReadOnly -ne $expected.ReadOnly) { throw 'Disk layout/state changed.' }
foreach ($e in $expected.Partitions) {
 $current=$parts | Where-Object PartitionNumber -eq $e.Number
 if (!$current -or [long]$current.Offset -ne $e.Offset -or [long]$current.Size -ne $e.Size -or [string]$current.DriveLetter -cne $e.Letter) { throw 'Partition layout changed.' }
}
$p=$null
if ($o.PartitionNumber -gt 0) {
 $p=Get-Partition -DiskNumber $d.Number -PartitionNumber $o.PartitionNumber
 if ([string]$p.Type -match 'System|Reserved|Recovery') { throw 'Protected partition type.' }
 if ($p.DriveLetter) {
  $b=Get-CimInstance -Namespace 'root/CIMV2/Security/MicrosoftVolumeEncryption' -ClassName Win32_EncryptableVolume -Filter "DriveLetter='$($p.DriveLetter):'" -ErrorAction Stop
  if ($b -and $b.EncryptionMethod -ne 0) { throw 'BitLocker-protected partition.' }
 }
}
switch ([string]$o.KindName) {
 'InitializeGpt' { if($d.PartitionStyle -ne 'RAW' -or $parts.Count -gt 0){throw 'Requires an empty RAW disk'}; Initialize-Disk -Number $d.Number -PartitionStyle GPT | Out-Null }
 'InitializeMbr' { if($d.PartitionStyle -ne 'RAW' -or $parts.Count -gt 0){throw 'Requires an empty RAW disk'}; Initialize-Disk -Number $d.Number -PartitionStyle MBR | Out-Null }
 'CreatePartition' {
   if ($o.Bytes -lt 16MB -or $o.FileSystem -notin @('NTFS','exFAT','FAT32')) {throw 'Invalid partition parameters'}
   $offset=[long]$o.Offset; if($offset -le 0){$new=New-Partition -DiskNumber $d.Number -Size ([long]$o.Bytes) -Alignment 1MB -AssignDriveLetter}else{$new=New-Partition -DiskNumber $d.Number -Size ([long]$o.Bytes) -Offset $offset -AssignDriveLetter}
   try { Wait-StorageJob ($new | Format-Volume -FileSystem $o.FileSystem -NewFileSystemLabel $o.Value -Confirm:$false -AsJob) 'Formatting' | Out-Null }
   catch { throw "Partition was created but formatting failed. Refresh the disk before proceeding. $($_.Exception.Message)" }
 }
 'ResizePartition' { $s=$p | Get-PartitionSupportedSize; if($o.Bytes -lt $s.SizeMin -or $o.Bytes -gt $s.SizeMax){throw 'Requested size is outside Windows supported limits'}; Wait-StorageJob ($p | Resize-Partition -Size ([long]$o.Bytes) -AsJob) 'Resizing' }
 'FormatPartition' { if($o.FileSystem -notin @('NTFS','exFAT','FAT32')){throw 'Invalid filesystem'}; Wait-StorageJob ($p | Format-Volume -FileSystem $o.FileSystem -NewFileSystemLabel $o.Value -Confirm:$false -AsJob) 'Formatting' | Out-Null }
 'DeletePartition' { $p | Remove-Partition -Confirm:$false }
 'SetLabel' { $p | Get-Volume | Set-Volume -NewFileSystemLabel $o.Value }
 'SetLetter' { if($o.Value -cnotmatch '^[D-Z]$'){throw 'Invalid drive letter'}; if(Get-Volume -DriveLetter $o.Value -ErrorAction SilentlyContinue){throw 'Drive letter already in use'}; $p | Set-Partition -NewDriveLetter $o.Value }
 'RemoveLetter' { if(!$p.DriveLetter){throw 'Partition has no letter'}; $p | Remove-PartitionAccessPath -AccessPath "$($p.DriveLetter):\" }
 'Offline' { Set-Disk -Number $d.Number -IsOffline $true }
 'Online' { Set-Disk -Number $d.Number -IsOffline $false }
 'ReadOnly' { Set-Disk -Number $d.Number -IsReadOnly $true }
 'ReadWrite' { Set-Disk -Number $d.Number -IsReadOnly $false }
 'ReTrim' { if(!$p.DriveLetter){throw 'Requires a mounted volume'}; Optimize-Volume -DriveLetter $p.DriveLetter -ReTrim }
 'CheckFileSystem' { if(!$p.DriveLetter){throw 'Requires a mounted volume'}; Repair-Volume -DriveLetter $p.DriveLetter -Scan | Out-String | Write-Output }
 'QuickWipeDisk' { Clear-Disk -Number $d.Number -RemoveData -RemoveOEM -Confirm:$false; Update-HostStorageCache }
 default { throw 'Unknown storage operation.' }
}
'Completed.'
