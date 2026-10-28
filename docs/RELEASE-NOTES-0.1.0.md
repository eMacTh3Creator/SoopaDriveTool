# SoopaDriveTool 0.1.0 — development preview

Native Windows 11 **ARM64** and **x64** installers and portable packages, with an original modern interface and logo.

## Included

- Disk and partition maps, capacity, encryption/protection state and controller reliability data.
- Windows-supported data-disk partition creation/resizing/format/delete/label/letter management, RAW GPT/MBR initialization, online/offline/read-only state, filesystem scan and TRIM.
- Real native Microsoft DiskSpd sequential/random/mixed benchmarks; configurable file size, duration and passes; MB/s, IOPS and latency; history and CSV/JSON export.
- Compressed content-addressed file snapshots, deduplication, verified empty-folder restore and logged-on-user daily scheduling.
- Offline raw data-disk images, equal-size clones/restores and zero-overwrite with full read-back verification.
- Read-only raw-image MBR/GPT inspection and local operation journals.

## Read before installing

**Unsigned early preview.** This is a working foundation, not complete parity with the referenced commercial disk suites. The running system/boot disk and EFI/reserved/recovery partitions are protected. Evaluate destructive operations on disposable data disks and retain independent backups.

Partition moving/merging, smaller-target OS migration, VSS-consistent live system backup, encrypted backup containers, deleted-file recovery, boot repair and WinPE recovery media remain planned. Raw clones/restores require equal disk and logical-sector sizes. File backups do not preserve all NTFS metadata and are not encrypted. A zero-overwrite does not guarantee SSD/NVMe secure erasure. Applied operations do not offer automatic rollback.

## Choose a download

- **arm64-Setup.exe**: Windows on ARM / Snapdragon / Windows on Apple Silicon.
- **x64-Setup.exe**: 64-bit Intel/AMD Windows.
- **Portable.zip**: matching-architecture self-contained folder; extract before running.

The runtime and correct native DiskSpd engine are included. The setup bootstrapper may use emulation on Windows ARM; the installed application and DiskSpd binaries are native ARM64 in the ARM package.

Compare the downloaded file’s SHA-256 with `SHA256SUMS.txt` using `Get-FileHash -Algorithm SHA256`. Check `BUILD-PROVENANCE.json` for the exact source-code commit and dependency hash.

See [validation](https://github.com/eMacTh3Creator/SoopaDriveTool/blob/main/docs/VALIDATION.md), [research and plan](https://github.com/eMacTh3Creator/SoopaDriveTool/blob/main/docs/RESEARCH-AND-PLAN.md), and [open roadmap issues](https://github.com/eMacTh3Creator/SoopaDriveTool/issues).
