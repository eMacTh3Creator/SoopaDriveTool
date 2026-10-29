SoopaDriveTool
Version @APP_VERSION@ - unsigned preview
Windows 11 ARM64 and x64

Start SoopaDriveTool.exe. Windows requests administrator approval (UAC) before
the window opens, unless the process is already elevated. Declining closes the
app. The .NET runtime is included. Review queued operations, then Apply.
If a step fails, later steps stop; completed disk changes are not rolled back.
Click a graphical partition to select it. Right-click for Move, Shrink, Expand,
Format, Erase contents, Rename, and Delete. Apply opens a Yes/No review; No is the
default answer. No typed phrase is required.

Connected disks
Internal disks, USB flash drives and external hard drives appear in the disk list.
Enable Auto refresh in the header; Settings offers 5, 10, 30 and 60 second checks.
The choice is saved. Refresh pauses during operations and dialogs. A disconnected
or changed queued disk disables Apply until the queue is cleared and reviewed.
Incomplete Windows device information is shown with a warning; disk changes are
blocked until full information is available. USB SMART depends on the adapter.

Partition management
Create, delete, rename, format, resize, move, split, merge and redistribute data
partitions. Convert nonempty GPT/MBR, primary/logical and eligible simple dynamic
NTFS volumes. Filesystem and allocation-unit migration uses a verified WIM
checkpoint on separate storage. Review the supported conditions in each dialog.
Move/conversion recovery checkpoints are retained when needed after interruption.

Clone modes
- Byte-exact: equal disk/sector sizes, offline source and destination, complete
  target SHA-256 read-back. The larger-target mode relocates backup GPT metadata.
- Filesystem-aware: copies used NTFS files through a verified WIM archive.
- Windows-aware UEFI: fresh GPT/EFI/MSR/Windows layout, preserved Windows volume
  mapping, boot files and available WinRE. Choose an inactive Windows installation
  or the live VSS mode for the running system. Architecture must match the host.
Used-file and Windows modes need scratch storage on another physical disk.
Disconnect the source before independently boot-testing the destination.
BitLocker, TPM/Windows Hello, activation and dissimilar hardware need separate
qualification. The running Windows disk cannot be overwritten or repartitioned.

Encrypted backups and images
AES-256-GCM authenticated encryption, compressed/deduplicated chunks, encrypted
file names, full/incremental/differential restore points, retention, exclusions,
passphrase rotation and selective restore. NTFS permissions, streams, hardlinks,
links and timestamps are preserved where the destination supports them.
VSS backs up open local files and the running Windows volume consistently.
Network repositories use the Windows account's existing share permissions.
Daily, weekly, hourly and logon schedules support logged-on-user or installed
SYSTEM execution; schedule secrets use Windows DPAPI and restricted job folders.
Background jobs keep the account and run level configured in Task Scheduler.
They do not show the app's startup UAC prompt. VSS tasks require elevation;
SYSTEM jobs require an installed application in Program Files.
Keep passphrases separately: the application cannot recover a lost passphrase.

Protected raw images support verified desktop restore and read-only VHD browsing
(512-byte images up to 2040 GiB). Export WIM resources for selective extraction.
Windows image restoration runs from architecture-matched recovery media.

Recovery media
Install Microsoft's Windows ADK Deployment Tools and matching WinPE add-on.
Use Recovery media to build an ISO, optionally include signed drivers, then
write a selected USB drive. Microsoft boot files are supplied by your ADK,
not bundled in the installer. Boot-test media with your storage before relying
on it. The native recovery app restores protected raw/Windows images, clones
whole disks, verifies overwrites, requests eligible ATA erasure and repairs
UEFI boot files while retaining an EFI backup.

Recovery and diagnostics
Scan for lost partitions, rebuild from backup GPT, inspect recoverable deleted
NTFS/FAT files, preview/export carved PNG/JPEG/PDF files, and review sector edits
with original-byte backups. Recover to a different physical disk. Fragmented
carving, encrypted/compressed NTFS recovery and multi-disk dynamic reconstruction
are outside the supported recovery modes.

Quick wipe resets partition metadata and leaves file data recoverable. Thorough
zero/random/three-pass overwrites verify every pass. Free-space overwrite keeps
allocated files and a reserve. Controller NVMe/ATA erasure depends on eligible
hardware/firmware; a completion report is not a certified purge.

SMART exports retain all device-exposed attributes, errors and self-test history
in JSON, CSV, text and printable HTML. Missing data remains unavailable. A
submitted self-test is not a health pass. Benchmarks run native DiskSpd
workloads, including custom block/queue/thread/read-mix profiles and history.

Application source is MIT licensed. Third-party components retain their own
licenses; see THIRD-PARTY-NOTICES.txt and Licenses/. Standalone wimlib-imagex
1.14.5 is GPLv3+; upstream labels ARM64 builds experimental. Complete wimlib
and smartmontools corresponding source archives are included.

Source, user guide and test results:
https://github.com/eMacTh3Creator/SoopaDriveTool
https://github.com/eMacTh3Creator/SoopaDriveTool/blob/main/docs/VALIDATION-0.3.0.md
https://github.com/eMacTh3Creator/SoopaDriveTool/releases
https://emacth3creator.github.io/SoopaDriveTool/
