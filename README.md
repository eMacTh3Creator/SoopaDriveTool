# SoopaDriveTool

[![Windows builds](https://github.com/eMacTh3Creator/SoopaDriveTool/actions/workflows/build.yml/badge.svg)](https://github.com/eMacTh3Creator/SoopaDriveTool/actions/workflows/build.yml)

SoopaDriveTool is an open-source disk utility for Windows 11. It manages partitions, clones drives, creates backups, runs storage benchmarks, and exports SMART reports. Builds are available for ARM64 and x64. Developed by Ev-Tech Solutions, LLC.

[Website](https://emacth3creator.github.io/SoopaDriveTool/) · [User guide](docs/USER-GUIDE.md) · [Release notes](docs/RELEASE-NOTES-0.3.3.md) · [Test results](docs/VALIDATION-0.3.3.md)

## Download

Version **0.3.3** is an unsigned preview. Windows may show a publisher or SmartScreen warning.

| Windows PC | Installer | Portable |
|---|---|---|
| ARM64, including Snapdragon PCs and Windows VMs on Apple Silicon | [ARM64 installer](https://github.com/eMacTh3Creator/SoopaDriveTool/releases/download/v0.3.3/SoopaDriveTool-0.3.3-arm64-Setup.exe) | [ARM64 ZIP](https://github.com/eMacTh3Creator/SoopaDriveTool/releases/download/v0.3.3/SoopaDriveTool-0.3.3-arm64-Portable.zip) |
| 64-bit Intel or AMD | [x64 installer](https://github.com/eMacTh3Creator/SoopaDriveTool/releases/download/v0.3.3/SoopaDriveTool-0.3.3-x64-Setup.exe) | [x64 ZIP](https://github.com/eMacTh3Creator/SoopaDriveTool/releases/download/v0.3.3/SoopaDriveTool-0.3.3-x64-Portable.zip) |

The .NET runtime is included. Extract the portable ZIP before running it. [Release checksums](https://github.com/eMacTh3Creator/SoopaDriveTool/releases/download/v0.3.3/SHA256SUMS.txt) are available for each download.

## Features

- **Disk detection:** view internal disks, USB drives, and external hard drives. Enable automatic refresh and choose an interval in Settings.
- **Partitions:** create, delete, format, rename, resize, move, split, and merge partitions. Redistribute free space and convert supported partition tables, volume types, filesystems, and allocation sizes.
- **Queued changes:** prepare several operations against a preview of the disk layout, then review and apply them together. Later steps can refer to a partition created earlier in the queue.
- **Cloning:** copy every sector, copy used NTFS files, or migrate a Windows UEFI installation. Windows cloning supports inactive installations and the running system through Volume Shadow Copy Service (VSS).
- **Backups and images:** create encrypted full, incremental, or differential backups. Set exclusions, retention rules, and schedules; restore selected files or complete disk and Windows images.
- **Recovery:** build WinPE recovery media, restore images, repair UEFI boot files, find lost partitions, and recover supported deleted-file candidates. Recovery tools include file previews and a sector editor.
- **Wiping:** reset partition metadata, overwrite a disk with zeroes or random data, run a three-pass overwrite, or overwrite free space. Full-disk overwrites include read-back verification. Firmware erase is available on eligible devices.
- **Drive health:** view the SMART attributes, error logs, wear counters, and self-test history exposed by the drive. Export JSON, CSV, text, or printable HTML reports for maintenance and resale records.
- **Benchmarks:** run Microsoft DiskSpd workloads, configure access patterns and queue depth, compare saved runs, and export throughput, IOPS, and latency results.

The activity screen shows the current operation, completed and remaining steps, elapsed time, and progress when the engine provides it. Light, dark, and system themes are available. The interface is in English.

See [supported operations and test coverage](docs/COMPLETION-MATRIX.md) for the conditions attached to each feature.

## Before changing a disk

On launch, SoopaDriveTool requests Windows administrator approval (UAC) before opening its window, unless it is already elevated. If you decline, the app closes. Disk changes still require review and Apply.

Background backup jobs keep their configured Windows account and task run level. VSS backups and SYSTEM jobs still require administrator access.

Keep a separate, verified backup before changing partitions or replacing a disk. If a queued step fails, later steps stop; changes already made are not automatically undone. Quick reset removes partition metadata and leaves file contents recoverable.

The running Windows disk is protected from partition changes and raw writes. Live Windows cloning reads it through VSS. BitLocker and unknown encryption states block partition changes. File-based and Windows cloning require scratch space on a separate disk. Disconnect the source before booting its clone.

Recovery-media creation requires Microsoft's Windows ADK Deployment Tools and the matching WinPE add-on. Microsoft boot files are supplied by your ADK installation.

ARM64 Windows clones, image restores, and boot repair have passed independent Secure Boot tests in spare VMs. Physical firmware erasure, x64 recovery boots, USB/4Kn hardware, power interruption, and migration between different hardware still need testing. VM benchmark results do not measure physical-drive performance. See the [validation report](docs/VALIDATION-0.3.3.md) for the full scope and remaining limits.

## Build from source

Use a Windows host with .NET 10, Visual Studio C++ Build Tools and the Windows SDK, and Inno Setup 6.7 or later. The build uses 7-Zip to extract smartmontools when it has not already been staged.

```powershell
./scripts/build.ps1 -Runtime win-arm64
./scripts/build.ps1 -Runtime win-x64
./scripts/test.ps1 -Runtime win-arm64
# Run as administrator. This creates and deletes temporary test VHDs.
./scripts/test.ps1 -Runtime win-arm64 -Integration
```

Dependency versions and SHA-256 hashes are recorded in `scripts/dependencies.json`. The native components use the static MSVC runtime. The app, DiskSpd, VSS requester, and recovery host run natively on the selected architecture; the SMART collector uses x64 emulation on ARM64.

GitHub Actions builds and checks both architectures. A separate workflow deploys the website to GitHub Pages. Publishing a release is a manual step.

## Report a problem

Use [GitHub Issues](https://github.com/eMacTh3Creator/SoopaDriveTool/issues) for bugs and feature requests. Include the app version, Windows version, architecture, operation, and relevant log output. Remove serial numbers, personal paths, and credentials before posting. For vulnerabilities, see [SECURITY.md](SECURITY.md).

## License

The application source is [MIT-licensed](LICENSE). Bundled components have their own licenses, listed in [Third-party notices](THIRD-PARTY-NOTICES.txt). Packages include the corresponding smartmontools and wimlib source archives.
