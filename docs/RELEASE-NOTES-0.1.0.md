# SoopaDriveTool 0.1.0

The first preview provided Windows 11 ARM64 and x64 installers and portable packages. These notes describe version 0.1.0; see the [current guide](USER-GUIDE.md) for later features.

## Included

- Disk and partition maps with capacity, encryption state, and controller reliability information.
- Data-disk partition creation, resizing, formatting, deletion, labels, and drive letters; GPT/MBR initialization; disk state changes; filesystem scans; and TRIM.
- Microsoft DiskSpd sequential, random, and mixed benchmarks with configurable file size, duration, and passes. Results included throughput, IOPS, latency, history, and CSV/JSON export.
- Compressed, deduplicated file snapshots, verified restoration to an empty folder, and daily schedules for the logged-on user.
- Offline raw disk images, equal-size clones/restores, and zero overwrite with complete read-back verification.
- Read-only MBR/GPT inspection and local operation journals.

## Limits in this release

This was an unsigned preview. The running Windows disk and its boot/recovery partitions were protected from changes. Destructive operations needed a separate backup and disposable test data.

Partition moving/merging, smaller-disk Windows migration, VSS, encrypted backups, deleted-file recovery, boot repair, and WinPE media were not yet available. Raw operations required matching disk and sector sizes. File backups were unencrypted and did not preserve all NTFS metadata. Overwrite did not guarantee SSD/NVMe secure erasure. Applied operations could not be automatically rolled back.

## Downloads

Choose the ARM64 installer for Windows on ARM, including Snapdragon PCs and Windows VMs on Apple Silicon. Choose x64 for Intel/AMD Windows. Extract the matching portable ZIP before running it.

The .NET runtime and DiskSpd engine were included. The installer could use emulation on Windows ARM; the installed ARM application and DiskSpd engine ran natively.

Compare the download with `SHA256SUMS.txt` using `Get-FileHash -Algorithm SHA256`. `BUILD-PROVENANCE.json` records the compiled source revision and dependency hash.

[0.1.0 test results](VALIDATION.md) · [Original research](RESEARCH-AND-PLAN.md)
