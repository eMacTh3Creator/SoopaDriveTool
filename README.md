# SoopaDriveTool

[![Windows builds](https://github.com/eMacTh3Creator/SoopaDriveTool/actions/workflows/build.yml/badge.svg)](https://github.com/eMacTh3Creator/SoopaDriveTool/actions/workflows/build.yml)

A native Windows 11 drive workspace for **ARM64 and x64**, with partition management, Windows migration, encrypted backup, bootable recovery, real storage benchmarks and full available SMART reports.

**0.3.0 storage suite preview — unsigned.** Prepare operations, review the projected layout and targets, then Apply. Activity follows each step through verification, with percentages when the engine can measure progress, throughput, elapsed time, next steps and optional waiting facts.

[Download](https://github.com/eMacTh3Creator/SoopaDriveTool/releases) · [Website](https://emacth3creator.github.io/SoopaDriveTool/) · [User guide](docs/USER-GUIDE.md) · [Validation](docs/VALIDATION-0.3.0.md) · [Feature coverage](docs/COMPLETION-MATRIX.md)

## Features

- **Partition management:** initialize, create, delete, rename, format, resize, move, split, merge and redistribute space; convert nonempty GPT/MBR, primary/logical, eligible basic/dynamic simple NTFS volumes, filesystems and allocation units. Stage dependent chains such as delete → delete → create → rename before applying.
- **Cloning:** byte-exact, larger-target GPT relocation, used-file NTFS, inactive Windows UEFI and live VSS Windows migration. Windows modes create fresh boot partitions, preserve Windows drive mappings and configure available recovery images.
- **Protected backups:** AES-256-GCM encrypted content and manifests, compressed/deduplicated chunks, full/incremental/differential points, exclusions, retention, passphrase rotation, selective restore and NTFS metadata. VSS reads open local files consistently. User/SYSTEM schedules use DPAPI-protected credentials.
- **Disk and Windows images:** protected versioned raw and VSS Windows images, full resource verification and disk read-back; read-only VHD browsing and selective extraction from exported WIM images.
- **Recovery media:** build architecture-matched WinPE ISO/USB media using an installed Microsoft ADK; optional signed drivers. The native recovery app restores images, clones disks, verifies overwrites, requests eligible ATA erasure and repairs UEFI boot files with an EFI backup.
- **Recovery:** backup-GPT reconstruction, lost partition discovery, supported NTFS/FAT deleted-file recovery, PNG/JPEG/PDF carving, byte preview/export and reviewed sector editing with original-byte backups.
- **Drive preparation:** quick partition reset; full zero/random or three-pass overwrite with complete read-back; free-space overwrite; eligible controller-native NVMe/ATA erase requests.
- **Diagnostics:** full device-exposed SMART attributes, errors, wear and self-test history; JSON/CSV/text/printable HTML exports, self-test requests, temperature history, surface maps and storage topology.
- **Benchmarks:** native Microsoft DiskSpd, standard or custom block/queue/thread/read-mix workloads, latency/IOPS/throughput, saved profiles, history comparisons and exports.
- Light/dark/system themes, compact layout, accessible input labels, local journals and interrupted-operation detection. Interface instructions are English.

## Supported conditions

Windows 11 on Snapdragon/Apple Silicon uses ARM64; Intel/AMD Windows uses x64. The runtime is bundled. Start normally for benchmarks and file backups; restart as administrator within the app for disk changes. Pending chains survive that restart and must be reviewed again.

The running Windows disk is protected from partition changes and raw writes; live migration reads it through VSS. BitLocker/unknown encryption blocks partition changes. Used-file/Windows migration needs separate scratch storage. Byte-exact mode requires matching geometry; the explicit larger-target mode relocates GPT metadata. Disconnect the original before independently booting its clone.

Conversions enforce filesystem and layout constraints. Dynamic-to-basic preserves single contiguous NTFS volumes and requires a restart; spanned/striped/mirrored layouts require migration. Read-only VHD browsing supports 512-byte raw images up to 2040 GiB. Deleted-file recovery supports the documented filesystem cases; fragmented carving, compressed/EFS NTFS recovery and multi-disk reconstruction are not claimed.

Recovery-media creation requires Microsoft's Windows ADK Deployment Tools plus matching WinPE add-on. Microsoft boot files are supplied by that installation. Physical firmware erase, dissimilar-hardware migration, x64 OS recovery boots, USB bridges and high-DPI/accessibility coverage require further hardware qualification. A SMART report or controller completion is not a warranty or certified purge. See the exact [test evidence and limits](docs/VALIDATION-0.3.0.md).

## Build

Use a Windows host with stable .NET 10, Visual Studio C++ Build Tools/Windows SDK, and Inno Setup 6.7+. 7-Zip is used to extract pinned smartmontools when it is not already staged.

```powershell
./scripts/build.ps1 -Runtime win-arm64
./scripts/build.ps1 -Runtime win-x64
./scripts/test.ps1 -Runtime win-arm64
# Administrator; creates/deletes its own disposable VHDs:
./scripts/test.ps1 -Runtime win-arm64 -Integration
```

Native storage/recovery hosts link the static MSVC runtime. Build dependencies and standalone engines are pinned by SHA-256 in `scripts/dependencies.json`. Complete smartmontools/wimlib corresponding source and licenses ship with packages. The SMART collector uses x64 emulation on ARM; the app, DiskSpd, VSS requester and recovery host are native. No persistent service, account or telemetry is required.

GitHub Actions builds/tests both architectures and deploys the buildless GitHub Pages website. Releases are published explicitly. [Security](SECURITY.md) · [Third-party notices](THIRD-PARTY-NOTICES.txt) · [Original research](docs/RESEARCH-AND-PLAN.md)
