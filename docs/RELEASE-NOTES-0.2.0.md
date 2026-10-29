# SoopaDriveTool 0.2.0

Version 0.2.0 added clone modes, disk wiping, queued partition changes, activity logs, and SMART reports. These notes describe that release; see the [current guide](USER-GUIDE.md) for later changes.

## Added

- **Clone modes:** exact-sector disk copy, used-file NTFS copy, and Windows UEFI cloning from an attached inactive installation. File-based modes use scratch space on a third disk and verify WIM resources during capture and apply. Windows mode preserves the installation's drive mapping and creates destination EFI/BCD files.
- **Wiping:** quick partition reset, zero overwrite, random overwrite, and a three-pass random/ones/zeroes overwrite. Full-disk overwrites verify the entire destination after each pass.
- **Queued operations:** prepare a delete → delete → create → rename sequence, review the layout, then Apply. Later operations can reference a newly created partition. Identity/layout changes stop the queue.
- **Activity:** completed, current, upcoming, and skipped steps; tool-reported progress; throughput and ETA for raw I/O; elapsed time; journals; exports; and optional waiting messages.
- **SMART:** available ATA/NVMe/SCSI attributes, counters, and logs, plus original JSON/text. Exports include JSON, CSV, text, and printable HTML. Reports distinguish unavailable, partial, and failing results.

The app and DiskSpd are native ARM64/x64. The separate smartctl collector uses x64 emulation on ARM. Its GPL license and corresponding source are included; no smartd service is installed.

## Limits in this release

The installers were unsigned. Live Windows cloning and independent clone boot tests were not yet completed. Physical controllers, firmware erasure, power interruption, and hot-unplug behavior were untested. Quick reset left file contents recoverable; overwrites did not establish erasure of remapped SSD cells. Applied queue steps could not be automatically rolled back.

[Clone and wipe details](DRIVE-WORKFLOWS-0.2.md) · [0.2.0 test results](VALIDATION-0.2.md)
