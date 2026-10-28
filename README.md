# SoopaDriveTool

[![Windows builds](https://github.com/eMacTh3Creator/SoopaDriveTool/actions/workflows/build.yml/badge.svg)](https://github.com/eMacTh3Creator/SoopaDriveTool/actions/workflows/build.yml)

An original Windows drive workspace for **ARM64 and x64**. Modern WPF interface, native Microsoft DiskSpd benchmarks, data-disk partition operations, verified disk images/clones, and compressed versioned file backups.

**0.2.0 development preview — unsigned.** Adds clone modes, quick/thorough wiping, dependent action chains, live activity and full available SMART exports. Advanced commercial-suite parity is still in progress. Evaluate destructive operations on disposable data disks. The running system disk is protected.

[Download installers and portable builds](https://github.com/eMacTh3Creator/SoopaDriveTool/releases) · [Website](https://emacth3creator.github.io/SoopaDriveTool/) · [Deep research and plan](docs/RESEARCH-AND-PLAN.md) · [Report an issue](https://github.com/eMacTh3Creator/SoopaDriveTool/issues)

## Working features

- Disk/partition maps, mounted capacity, encryption/protection state and controller reliability data.
- Initialize RAW disks as GPT/MBR; create, resize within Windows limits, format/delete/rename data partitions; drive-letter changes; online/offline and read-only state; scan/TRIM.
- Sequential/random read/write/mixed benchmarks with file size, duration, passes, profiles, mean/p99 latency, history and JSON/CSV export.
- Compressed content-addressed file snapshots, deduplication, SHA-256 verification, empty-folder restore and logged-on daily scheduling.
- Clone modes: byte-exact whole disk, filesystem-aware NTFS volume (used files), and Windows-aware UEFI migration of an attached inactive installation. WIM modes need scratch space on a third disk.
- Quick partition reset, full zero/random overwrite, or three-pass random/ones/zeros with full read-back verification. Quick reset does not erase file contents.
- Dependent action chains, including delete → delete → create → rename, projected layouts, verified partition-reference resolution, and one Apply button.
- Live activity with phases, percentages where available, bytes/throughput/ETA, next steps, event journals, and rotating facts.
- Full device-exposed ATA/NVMe/SCSI SMART attributes/logs through smartctl; JSON, CSV, text and printable HTML exports.
- Read-only raw-image MBR/GPT inspection and local operation journal.

## Boundaries

No partition moving/merging, cloning of the currently running OS, dynamic disks, deleted-file recovery, VSS system backups, encrypted backup container, or bootable WinPE environment yet. Windows-aware cloning configures destination EFI/BCD for an inactive installation; independent boot/hardware qualification remains outstanding. Byte-exact clones/restores require equal disk and sector sizes. File backups preserve content and timestamps, not full NTFS metadata; links and locked files stop the backup. Zero-overwrite is not SSD secure erase. See the [feature matrix and release gates](docs/RESEARCH-AND-PLAN.md).

## Run

Windows 11 ARM64 (Snapdragon or Windows on Apple Silicon) → ARM64 installer. Windows 11 Intel/AMD → x64 installer. The .NET runtime is bundled. Start normally for benchmarks/backups; restart as administrator within the app for disk changes. Administrator restart preserves the pending chain, revalidates its original disk state and requires review/apply again.

## Build

Windows build host, stable .NET 10 SDK, Inno Setup 6.7+, and 7-Zip for pinned smartmontools extraction:

```powershell
./scripts/build.ps1 -Runtime win-arm64
./scripts/build.ps1 -Runtime win-x64
./scripts/test.ps1 -Runtime win-arm64
# Administrator; creates/deletes its own temporary VHDs only:
./scripts/test.ps1 -Runtime win-arm64 -Integration
```

Engine binaries are fetched from pinned Microsoft/smartmontools releases and verified against `scripts/dependencies.json`. smartctl is a separate GPL executable; its license and complete corresponding source archive are included in packages. The x64 SMART collector runs under emulation on Windows ARM; the app and DiskSpd are native. No service is installed. No credentials are needed to build. See [validation](docs/VALIDATION.md), [security](SECURITY.md), and [third-party licenses](THIRD-PARTY-NOTICES.txt).

The website uses buildless HTML/CSS/JS. GitHub Actions builds both architectures and deploys GitHub Pages. Release creation is explicit; CI does not publish a release on every push.

[Clone/wipe/queue/SMART behavior and research](docs/DRIVE-WORKFLOWS-0.2.md) · [0.2 validation](docs/VALIDATION-0.2.md)
