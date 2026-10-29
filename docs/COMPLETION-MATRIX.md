# Supported operations and test coverage

This page summarizes version 0.3.4. The [user guide](USER-GUIDE.md) describes how to use each tool. The [latest validation report](VALIDATION-0.3.4.md) and [test record](QUALIFICATION-0.3.0.json) document the checks completed for this release.

## Available operations

| Area | Operations | Requirements and limits |
|---|---|---|
| Disk detection | Internal, USB, and external drives; optional automatic refresh with saved intervals; selection and queued-disk identity checks | Available details depend on the Windows provider and controller. Incomplete inventory is displayed with disk changes disabled. Physical USB bridges still need hardware testing. |
| File backup | Encrypted full, incremental, and differential backups; exclusions, retention, selective restore, passphrase changes, NTFS metadata, VSS, and user/SYSTEM schedules | AES-256-GCM encryption with Brotli compression. Scheduled credentials use Windows DPAPI. Target filesystems must support the metadata being restored. |
| Disk and Windows images | Compressed encrypted raw images, versioned restore points, verification, Windows WIM capture through VSS, read-only VHD browsing, and selective WIM extraction | Raw restore includes full read-back verification. VHD browsing supports 512-byte raw images up to 2040 GiB. Full Windows restoration runs from WinPE. |
| Cloning | Inactive or live Windows UEFI cloning, used-file NTFS cloning, exact-sector cloning, and copying to a larger disk with GPT relocation | Windows modes create new identifiers, preserve drive mappings, and configure available WinRE images. Source, destination, sector size, and scratch-space requirements depend on the mode. |
| Partition management | Create, resize, move, split, merge, and redistribute space; GPT/MBR, primary/logical, basic/dynamic, filesystem, and allocation-size conversion | Moves use saved checkpoints and require review before resuming. Filesystem changes use a verified WIM checkpoint. Basic/dynamic conversion supports eligible single contiguous NTFS volumes; dynamic-to-basic requires a restart. |
| File and partition recovery | Rebuild primary GPT from its backup; scan lost NTFS/exFAT/FAT32 partitions; recover supported NTFS MFT/data-run and FAT32 deleted entries; carve contiguous PNG/JPEG/PDF files; preview/export bytes; edit reviewed sectors | Fragmented carving, compressed/EFS NTFS file recovery, and multi-disk dynamic reconstruction are unsupported. Recovery depends on the source data still being present. Sector edits retain original bytes and check the preview hash. |
| Diagnostics | SMART attributes and logs, device self-tests, temperature history, read-surface maps, and volume/pool/virtual-disk topology | Reports contain only the data exposed by the device and controller. Unavailable data is marked explicitly. A submitted self-test must be checked later for its result. |
| Wiping | Verified selected-partition zero/random/three-pass erasure, quick partition reset, zero/random/three-pass disk overwrite, free-space overwrite, NVMe erase in Windows, and ATA erase in WinPE | Full-disk overwrites verify each pass. Firmware operations require an eligible device and driver support. ATA checks include bus, frozen, locked, and security state. Quick reset leaves file data recoverable; no mode provides erasure certification. |
| Recovery media | ARM64/x64 recovery applications, encrypted raw restore, Windows WIM restore, UEFI boot repair, disk clone, verified overwrite, ISO/USB creation, and signed-driver injection | Requires Microsoft's installed ADK and matching WinPE add-on. Microsoft boot files are not bundled. Physical USB and x64 recovery boots still need testing. |
| Benchmarks | Standard/custom DiskSpd workloads, random/zero data, saved profiles, throughput/IOPS/latency exports, run comparisons, and temperature history | Configure block size, queue depth, threads, and read percentage. VM results describe the virtual storage path. |
| Interface | Selectable disk maps and partition context menus, No-default Yes/No review, dependent operation queues, activity logs, measured or indeterminate progress, optional waiting facts, interrupted-run records, light/dark/system themes, and compact layout | Interface text is English. Inputs have labels; complete screen-reader and high-DPI testing remains outstanding. |

## Tests completed

- File backups, encrypted archives, VSS, metadata restoration, and installed SYSTEM schedules were tested. An encrypted VSS backup to an authenticated SMB share restored file content, named streams, ownership, and permissions.
- Partition checks covered overlapping NTFS moves, split/merge, redistribution, GPT/MBR and primary/logical conversion, filesystem changes, and basic → dynamic → basic conversion. Files and named streams survived the dynamic conversion and restart.
- Raw image capture, overwrite, and restore passed complete read-back checks. A WinPE raw restore also matched an independent hash of the entire destination disk. Deleted NTFS records and their data runs were recovered in the Windows suite.
- A live 256 GiB Windows installation cloned to a 192 GiB disk and booted twice under Secure Boot with the source disconnected. A full Windows image restored to a separate disk also booted twice. EFI repair, WinRE activation, and temporary-helper cleanup passed further boot checks.
- Both architecture builds and installer checks passed. All 10 main screens rendered; light and dark overview/benchmark screenshots were visually checked. The upgrade test retained a legacy backup, preferences, 35 history files, and a SYSTEM job that ran after upgrade. Uninstall removed its task.

Test counts include shared setup checks; they should not be added together as a unique test total. The validation report identifies the source revisions used for each test.

## Still to test

Physical firmware erasure, USB bridges, 4Kn disks, power interruption, migration to different hardware, x64 recovery boots, and complete assistive-technology coverage remain unverified. The installers are unsigned.
