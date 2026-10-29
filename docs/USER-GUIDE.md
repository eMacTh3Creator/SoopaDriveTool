# Using SoopaDriveTool 0.3

Install the ARM64 edition on Windows on ARM, or x64 on Intel/AMD Windows 11. The runtime and native engines are included. Administrator access is needed for drive changes, raw reads, SMART and VSS. Use **Restart as administrator** inside the app; review the restored pending chain before applying it.

## Organize a disk

Open **Disk manager**, select a data disk and partition, then choose an operation. **Advanced partition tools** adds move, split, merge, space redistribution, GPT/MBR, primary/logical, filesystem/allocation-unit and basic/dynamic conversion. The running Windows disk, its boot/recovery partitions and unknown encryption states are protected.

To combine several actions, queue each operation against the projected layout. For example, delete two partitions, create one larger partition, select that pending partition and rename it. Nothing changes until **Review & apply**. Removing a step also removes its dependent successors. After Apply, Activity shows completed, active and pending steps. If a step fails, the chain stops; earlier disk changes remain applied.

Partition movement and conversions need checkpoint storage on another physical disk. Moving a partition copies its bytes with durable overlap checkpoints, then commits verified metadata. An interrupted move is resumed only after an explicit review of its saved checkpoint. Filesystem/allocation-unit migration captures and verifies a WIM before formatting; its recovery archive is retained on failure. Unsupported metadata, file sizes or target capacity stop a conversion before formatting.

Dynamic-to-basic conversion supports contiguous, single-disk NTFS volumes. The converted disk stays offline until Windows restarts and unloads the old dynamic metadata. Multi-disk, mirrored or striped volumes need migration to separate basic storage.

## Clone a drive

Open **Clone & image → Clone & wipe** and choose the source, destination and clone mode. Confirm the destination identity carefully: its contents are replaced.

| Mode | What is copied | Requirements |
|---|---|---|
| Byte-exact | Every source sector, including unused/deleted data | Offline disks, equal disk/sector sizes; complete SHA-256 destination read-back |
| Byte clone to larger disk | Source sectors plus relocated backup GPT | Destination at least as large and equal sector size; partition expansion is a separate operation |
| Filesystem-aware NTFS | Used files through verified WIM capture/apply | Mounted unencrypted NTFS source; scratch on a third disk |
| Windows-aware UEFI | Selected inactive Windows installation, fresh boot layout and available WinRE | Matching Windows architecture, unencrypted NTFS, separate scratch storage |
| Live Windows VSS | Running Windows from a consistent snapshot with writer status | Administrator, unencrypted NTFS; source remains online; separate destination and sufficient scratch |

Windows-aware modes preserve Windows volume mappings and create fresh disk/partition identities. Available WinRE is copied and registered; a one-time helper completes WinRE activation on the destination's first Windows startup and removes its service after success. Its result is retained at `Windows\System32\SoopaDriveToolRecoveryOnce.json`.

Disconnect the original before booting the clone. TPM/Windows Hello, activation, BitLocker and dissimilar hardware require their own checks. A verified copy completes the disk operation; only an independent boot establishes that Windows starts on the intended machine.

## Back up files

In **Backup & restore**, choose source and repository, encryption, policy, exclusions, metadata and retention. New encrypted repositories require a passphrase of at least 12 characters. File names/manifests and content are encrypted; each bounded packet is authenticated. There is no password-recovery backdoor.

Full, incremental and differential points are independently restorable. Incremental/differential points reuse authenticated content chunks. Retention removes old points and only unreferenced objects. Do not edit or copy repository files while a backup, restore or maintenance operation holds its lock.

Use VSS for consistent reads of open local files and application writers. Network repositories use the executing Windows account's existing share access; VSS does not snapshot a remote source. NTFS owner/DACL, streams, links, hardlinks and timestamps are retained when supported. A different target filesystem may not represent all NTFS metadata.

Load restore points, browse their entries, select files/folders or leave the selection empty for a complete restore. Restore into a new/empty folder, then inspect its verified content. Passphrase rotation rewraps the repository key; saved schedules must be updated with the new passphrase.

Schedules support daily, weekly Sunday, hourly and logon execution. Logged-on-user jobs run with that user's access. Installed SYSTEM jobs can run before login, require administrator access and an app in Program Files, and need machine-account access to network shares. Secrets use Windows DPAPI and restricted job folders. The schedule panel runs, disables/enables and removes jobs, and shows their last recorded result.

Uninstall removes the installed application's scheduled tasks and retains backup/preferences data. Recreate schedules after reinstalling.

Automation can create a schedule with `SoopaDriveTool.exe --schedule-create request.json result.json`. The request fields are `Source`, `Repository`, `Options` (`Encrypted`, `Exclusions`, `Policy`, `KeepLatest`, `PreserveMetadata`), `Vss`, `SystemAccount`, `Frequency`, `Time` (`HH:mm`) and `Passphrase`. Protect and remove the plaintext request after use. The result identifies the restricted job file; it does not contain the passphrase.

## Back up or restore Windows and disk images

In **Clone & image → Protected images**, select offline raw capture or live VSS Windows capture. Use a repository and Windows-image scratch folder on another physical disk. Images share the protected repository format and can use full/incremental/differential restore points.

Raw restore verifies the archive before opening the destination for writes, then reads back all restored sectors. The selected destination must be offline and fit the image. Read-only browsing makes a separate temporary VHD with fresh identifiers, verifies its bytes, and reattaches it read-only. This supports 512-byte raw images up to 2040 GiB. Detach the VHD before deleting it.

Export a Windows image's verified resources to an empty folder before extracting an individual relative path from its WIM. Full Windows restoration uses the native recovery environment.

## Create and use recovery media

Install Microsoft's Windows ADK Deployment Tools and matching WinPE add-on. In **Recovery media**, choose an ISO destination and any signed storage/network driver folders. Build the ISO, then optionally write a selected USB data disk. The USB operation replaces that disk and verifies every copied file. Microsoft boot files come from your ADK installation.

Boot architecture-matched media and test that it sees the required disks. Keep images/passphrases on separate storage. The native app supports protected raw restore, Windows image restore to a fresh GPT/EFI/MSR/Windows/recovery layout, exact disk clone, verified overwrite, eligible ATA erase and UEFI boot repair. Review the destination and type its exact **DISK N** phrase. During a disk operation, shutdown is blocked.

Windows restoration verifies/decrypts its resources before preparing the destination. Its `restore-<snapshot ID>` staging folder contains plaintext Windows/recovery archives, even when the repository is encrypted. A failed restore retains this folder for retry; the next attempt checks every retained archive against the authenticated manifest before touching the destination. Incomplete or damaged archives require removing that staging folder and retrying. Successful restoration removes the staging folder. Treat retained archives as sensitive data and use suitably protected storage.

UEFI repair first copies and hashes the original EFI files to an owned backup folder on the chosen Windows volume. When it finds a standard Windows RE image on that disk, it registers the image (or preserves an already registered location) and stages one-time activation on the next startup. Multiple recovery images require resolving the ambiguous layout first; a missing recovery image is reported explicitly. Shut down and independently boot the restored destination with the source disconnected, then verify Windows RE status.

## Recover partitions and files

Open **Recovery inspector** and choose a physical disk or raw image. Lost-partition discovery reads NTFS/exFAT/FAT signatures and geometry. Applying a candidate or rebuilding primary GPT requires review and saves original table sectors. Sector editing retains original bytes and rejects a changed preview hash.

Deleted NTFS recovery reads supported MFT records and data runs; FAT32 recovery reads supported deleted entries. Signature carving finds contiguous PNG/JPEG/PDF candidates. Preview/export to another disk and inspect the results. Reused clusters, fragmentation, compression and encryption can prevent recovery; a candidate is not a recovery guarantee.

## Measure, diagnose and erase

**Benchmark** runs standard or custom DiskSpd profiles with the chosen block size, queue depth, threads, access pattern and read mix. Compare like settings. VM speeds describe the virtual storage path and are not physical-drive performance claims.

**Health** reads full available SMART attributes, error and self-test logs. Export JSON, CSV, text or printable HTML for reconditioning/resale records. Missing values stay unavailable; failing flags remain visible. Device self-tests run in firmware, can affect performance and must be checked later for actual results. Temperature history, read-surface maps and topology are separate views.

Quick reset removes partition metadata and leaves file data recoverable. Thorough overwrite verifies every pass. Free-space overwrite retains allocated files and a 256 MiB reserve. NVMe crypto/block erase and WinPE ATA erase require eligible direct devices and driver/firmware support. Controller completion does not establish certified erasure of remapped cells.

Activity journals and interrupted-run records are local. Review checkpoints and the actual disk after an interruption; disk writes never resume automatically. See [validation and hardware limits](VALIDATION-0.3.0.md).
