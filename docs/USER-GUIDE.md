# SoopaDriveTool user guide

This guide covers version 0.3.0 on Windows 11. Install ARM64 on Windows on ARM, or x64 on Intel/AMD PCs. The .NET runtime and required engines are included.

Drive changes, raw reads, SMART, and VSS need administrator access. Choose **Restart as administrator** inside the app. Pending operations are retained, but you must review them again before applying.

## Manage partitions

Open **Disk manager**, select a data disk and partition, then choose an operation. **Advanced partition tools** includes moving, splitting, merging, space redistribution, and conversion of partition tables, volume types, filesystems, and allocation sizes.

The running Windows disk, its boot/recovery partitions, and partitions with unknown encryption states are protected from ordinary changes.

### Queue several changes

Prepare each operation against the previewed disk layout. For example:

1. Delete two partitions.
2. Create a larger partition in the resulting free space.
3. Select the pending partition and rename it.
4. Choose **Review & apply**, check the disk and operations, then Apply.

No disk changes are made while preparing the queue. Removing a step also removes every step after it. Activity shows completed, active, and pending steps. If a step fails, the queue stops; earlier changes remain applied.

### Moves and conversions

Partition moves and conversions need checkpoint storage on another physical disk. A move saves checkpoints while copying, then commits the verified metadata. After an interruption, you must review the saved checkpoint before resuming.

Filesystem and allocation-size changes capture and verify a WIM archive before formatting. The archive is retained if the operation fails. Unsupported metadata, file sizes, or insufficient destination capacity stop the conversion before formatting.

Dynamic-to-basic conversion supports contiguous, single-disk NTFS volumes. The disk stays offline until Windows restarts and unloads the old dynamic metadata. Multi-disk, mirrored, or striped volumes require migration to separate basic storage.

## Clone a drive

Open **Clone & image → Clone & wipe**. Choose the source, destination, and clone mode. The destination's contents will be replaced.

| Mode | What it copies | Requirements |
|---|---|---|
| Byte-exact | Every source sector, including unused and deleted-data areas | Offline disks with equal disk and sector sizes. The entire destination is read back and checked with SHA-256. |
| Byte clone to larger disk | Source sectors, with backup GPT metadata relocated | Destination at least as large as the source, with the same sector size. Expanding partitions is a separate operation. |
| Filesystem-aware NTFS | Used files through WIM capture and apply | Mounted, unencrypted NTFS source and scratch space on a third disk. |
| Windows-aware UEFI | An inactive Windows installation with a new boot layout and available WinRE image | Matching Windows architecture, unencrypted NTFS, and separate scratch storage. |
| Live Windows VSS | The running Windows installation from a VSS snapshot, with writer status | Administrator access, unencrypted NTFS, a separate destination, and sufficient scratch space. The source stays online. |

Windows-aware modes preserve drive mappings and create new disk/partition identifiers. If WinRE is available, it is copied and registered. A temporary helper completes WinRE activation on the destination's first startup, then removes its service. Its result is saved to `Windows\System32\SoopaDriveToolRecoveryOnce.json`.

Disconnect the source before booting the clone. Check password sign-in, applications, and files on the destination. Activation, Windows Hello/TPM binding, BitLocker, and different hardware may need additional setup. Copy verification alone does not confirm that Windows will boot on the intended PC.

## Back up files

In **Backup & restore**, choose the source, repository, encryption settings, policy, exclusions, metadata options, and retention.

New encrypted repositories need a passphrase of at least 12 characters. File contents, filenames, and manifests are encrypted, and every packet is authenticated. Keep the passphrase separately; the application cannot recover it.

Full, incremental, and differential restore points can each be restored independently. Incremental and differential points reuse authenticated content chunks. Retention removes old points and objects no longer referenced by the remaining points. Do not edit or copy repository files while an operation holds the repository lock.

Use VSS to read open local files and coordinate application writers. VSS does not snapshot a remote source. A network repository uses the executing Windows account's share access.

NTFS ownership, permissions, named streams, links, hardlinks, and timestamps are retained where supported. A different destination filesystem may not support all of that metadata.

### Restore files

Load the restore points and browse their entries. Select files/folders, or leave the selection empty to restore everything. Restore to a new or empty folder and inspect the result.

Changing a repository passphrase rewraps its key. Update any saved schedules with the new passphrase.

### Schedule a backup

Schedules can run daily, every Sunday, hourly, or at logon. A logged-on-user task runs with that user's access. An installed SYSTEM task can run before login and requires administrator access, an executable in Program Files, and suitable machine-account access to network shares.

Secrets are protected with Windows DPAPI and restricted job folders. The schedule panel can run, enable, disable, and remove jobs, and shows their last recorded result.

Uninstall removes the app's scheduled tasks and retains backup and preference data. Recreate schedules after reinstalling.

For automation, run `SoopaDriveTool.exe --schedule-create request.json result.json`. The request accepts `Source`, `Repository`, `Options` (`Encrypted`, `Exclusions`, `Policy`, `KeepLatest`, `PreserveMetadata`), `Vss`, `SystemAccount`, `Frequency`, `Time` (`HH:mm`), and `Passphrase`. The request contains plaintext secrets: protect it and remove it after use. The result identifies the restricted job file and does not contain the passphrase.

## Create or restore disk images

Open **Clone & image → Protected images**. Choose offline raw capture or live Windows capture through VSS. Keep the repository and Windows-image scratch folder on another physical disk. Images use the encrypted repository format and support full, incremental, and differential restore points.

Raw restore verifies the archive before opening the destination for writing, then reads back every restored sector. The destination must be offline and large enough for the image.

Read-only browsing creates a separate temporary VHD with new identifiers, verifies it, and reattaches it read-only. It supports 512-byte raw images up to 2040 GiB. Detach the VHD before deleting it.

To extract individual files from a Windows image, export its verified resources to an empty folder, then select a relative path from the WIM. Restoring a complete Windows installation requires the recovery environment.

## Create recovery media

Install Microsoft's Windows ADK Deployment Tools and the matching WinPE add-on. In **Recovery media**, choose an ISO destination and any signed storage/network driver folders. Build the ISO, then optionally write it to a selected USB data disk. This replaces the USB disk's contents and verifies every copied file.

Microsoft boot files come from your ADK installation. Use media that matches the Windows architecture and test that it boots and can access the required disks. Keep images and passphrases on separate storage.

The recovery app supports encrypted raw restore, Windows image restore, exact disk clone, verified overwrite, eligible ATA erase, and UEFI boot repair. Review the destination and type its exact **DISK N** phrase. Shutdown is blocked while a disk operation is running.

### Restore Windows

Windows restoration creates a new GPT/EFI/MSR/Windows/recovery layout. It verifies and decrypts the image resources before preparing the destination.

The repository's `restore-<snapshot ID>` staging folder contains **unencrypted Windows and recovery archives**, even for an encrypted backup. A failed restore retains this folder for another attempt. Before touching the destination, the next attempt checks every retained archive against the authenticated manifest. If an archive is incomplete or damaged, remove the staging folder and retry.

Successful restoration removes the staging folder. Treat retained archives as sensitive data and protect the repository storage accordingly.

### Repair UEFI boot files

Repair first copies the original EFI files to a backup folder on the chosen Windows volume and verifies each copy against its source.

If a standard WinRE image is found on the same disk, repair registers it or keeps its existing registration, then prepares activation for the next startup. Multiple possible recovery images must be resolved first; a missing image is reported.

After restoration or repair, shut down and boot the destination independently with the source disconnected. Check Windows RE status as well as normal Windows startup.

## Recover partitions and files

Open **Recovery inspector** and select a physical disk or raw image. Lost-partition scanning reads NTFS, exFAT, and FAT signatures and geometry. Adding a candidate or rebuilding primary GPT requires review and saves the original partition-table sectors.

Sector edits save the original bytes and reject a preview whose hash has changed.

Deleted-file recovery reads supported NTFS MFT records/data runs and FAT32 directory entries. Signature carving finds contiguous PNG, JPEG, and PDF candidates. Preview and export results to another disk. Reused clusters, fragmentation, compression, and encryption can prevent recovery.

## Run benchmarks

**Benchmark** runs standard or custom DiskSpd profiles. Choose the block size, queue depth, thread count, access pattern, and read percentage. Use matching settings when comparing runs.

VM results describe the virtual storage path; they do not establish physical-drive performance.

## Check drive health

**Health** reads the SMART attributes, error logs, and self-test history exposed by the device. Export JSON, CSV, text, or printable HTML for maintenance or resale records. Missing data is marked unavailable, and reported failures remain visible.

Device self-tests run in firmware and may affect performance. Check the later result to confirm completion. Temperature history, read-surface maps, and storage topology are separate views.

## Wipe a disk or free space

Quick reset removes partition metadata and leaves file contents recoverable. Disk overwrite modes verify each pass. Free-space overwrite preserves allocated files and leaves a 256 MiB reserve.

NVMe crypto/block erase and WinPE ATA erase require an eligible direct device and driver/firmware support. A controller's successful response does not certify erasure of remapped cells.

## Review interrupted operations

Activity journals and interruption records stay local. After an interruption, inspect the actual disk and any saved checkpoints. Disk writes never resume automatically. See [test results and remaining limits](VALIDATION-0.3.0.md).
