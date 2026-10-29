# Initial research and development plan

This document records the research and plan for version 0.1.0. It is kept for project history; the planned work below does not describe the current release. For version 0.3.0, see [supported operations](COMPLETION-MATRIX.md), the [user guide](USER-GUIDE.md), and [test results](VALIDATION-0.3.0.md). The [0.2-series notes](DRIVE-WORKFLOWS-0.2.md) cover the first clone modes, wipe options, operation queues, and SMART reports.

Research date: October 28, 2026. Target: Windows 11 ARM64 and x64. Project owner: Everett Jenkins. The initial release was an unsigned 0.1.0 preview.

## Scope

Build a Windows application for partition management, drive benchmarks, backup, cloning, and recovery. CrystalDiskMark, EaseUS Partition Master, and Paragon Hard Disk Manager were references for the expected tasks. The application uses its own interface and code.

Users inspect a disk, choose an operation, review its target and expected changes, and apply it. The engine then verifies the result and saves a local log. The overview does not make changes automatically.

## Primary-source findings

- [CrystalDiskMark](https://crystalmark.info/en/software/crystaldiskmark/) measures sequential, random and mixed read/write workloads, offers multiple profiles, and supports ARM64 and x64. Its documentation distinguishes decimal throughput from binary file sizes and cautions that different settings and major versions affect comparability.
- [EaseUS Partition Master](https://www.easeus.com/partition-manager/epm-pro.html) groups partition resizing, disk cloning, conversion, recovery and boot utilities around a visible disk layout and a preview/apply workflow. Filesystem relocation and bootable Windows migration require more than partition-table edits.
- [Paragon Hard Disk Manager](https://www.paragon-software.com/home/hdm-windows/) combines disk organization with image backup, migration and recovery environments. Its published Windows support includes ARM64; architecture support must extend through the engine, installer and recovery tools, not just the interface.
- [Microsoft DiskSpd](https://github.com/microsoft/diskspd) supplies a maintained storage workload engine and native ARM64/x64 binaries. [Its command reference](https://github.com/microsoft/diskspd/wiki/Command-line-and-parameters) documents file targets, queue/thread controls, cache policy, latency and XML output.
- [Microsoft Storage PowerShell](https://learn.microsoft.com/en-us/powershell/module/storage/?view=windowsserver2025-ps) provides supported partition/device operations. [Resize-Partition](https://learn.microsoft.com/en-us/powershell/module/storage/resize-partition?view=windowsserver2025-ps) works within filesystem and contiguous-space limits. [Get-Disk](https://learn.microsoft.com/en-us/powershell/module/storage/get-disk?view=windowsserver2025-ps) does not inventory dynamic disk volumes as ordinary basic disks; no dynamic-disk support is claimed.
- [VSS](https://learn.microsoft.com/en-us/windows/win32/vss/volume-shadow-copy-service-portal) is the required consistency framework for live application-aware Windows backup. Copying a live physical disk without it would not provide the same guarantee.
- [WinPE media](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/download-winpe--windows-pe?view=windows-11) depends on the ADK plus its WinPE add-on. A WPF desktop executable is not, by itself, a supported WinPE application. A recovery host must be separately designed and qualified on each architecture.
- [.NET 10](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview) is an LTS release. [WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/) supplies a native Windows UI with scalable rendering and standard control/accessibility infrastructure. [Inno Setup architecture rules](https://jrsoftware.org/ishelp/topic_archidentifiers.htm) support architecture-specific installation without an end-user .NET prerequisite when publishing self-contained.

The initial implementation uses supported Windows services and does not need a custom kernel driver.

## Version 0.1.0 scope

| Area | Available in 0.1.0 | Planned for later releases |
|---|---|---|
| Inventory | Physical/basic disk identity, bus, size, partition layout, mounted free space, protection/encryption state | Dynamic disks, richer Storage Spaces/RAID topology, detailed NVMe/ATA passthrough |
| Partitioning | GPT/MBR initialization of RAW disks; aligned NTFS/exFAT/FAT32 creation; data-partition resize within Windows-supported limits; format, delete, label, letter/hide-by-letter; online/offline/read-only state | Move partitions; merge/split with data migration; nonadjacent space redistribution; cluster-size changes; logical/primary conversion |
| Conversion | New RAW disks may be initialized GPT or MBR | Nonempty GPT↔MBR, dynamic↔basic, filesystem conversion with preserved content |
| Benchmarking | 1 MiB sequential Q8/Q1; 4 KiB random Q32/Q1; read/write plus optional 70/30 mix; duration, file size, passes, random/zero pattern; MB/s, IOPS, mean/p99 latency; cancellation, JSON/CSV, history | User-defined workload editor, thermal time-series, comparisons/charts, more languages/themes |
| File backup | Compressed content-addressed snapshots, per-file SHA-256, deduplication, restore to empty folder, validation, daily logged-on-user task scheduling | Encryption, retention/garbage collection, differential policy UI, exclusions, network qualification, open-file VSS consistency |
| Disk image | Full raw offline data-disk image, size/sector metadata, image verification, equal-size restore with read-back | Used-block/sparse/compressed images, encrypted incremental images, selective restore/mounting, live system VSS |
| Clone | Equal-size/sector-size offline data-disk copy, full read-back verification, distinct source/target validation | Smaller-target clone, live OS migration, automatic GPT relocation/boot repair, dissimilar hardware restore |
| Recovery | Read-only MBR/GPT image inspection; header/table CRCs and bounds checks | Backup-GPT reconstruction, deleted partition recovery, filesystem-aware file recovery, sector editor |
| Maintenance | Data-volume filesystem scan and TRIM; controller-exposed health/reliability data | Surface scan map, NVMe self-tests/sanitize, ATA secure erase, free-space wipe, firmware-specific validation |
| Erase | Offline data-disk one-pass zero overwrite, complete read-back | Device-native secure erase and sanitize; verified compliance erasure; SSD overprovisioned-cell handling |
| Recovery environment | Design and prerequisites documented | Architecture-specific bootable recovery host, ADK packaging, USB/ISO creation, signed boot chain, boot repair |

Version 0.1.0 enabled operations only where the engine supported them. The website used application screenshots with labeled sample data.

## Architecture

### Components

1. **SoopaDriveTool.Core**: disk/partition/request models, fingerprinting and operation policy, backup object store, safe restore validation, MBR/GPT image parser. No UI or external packages.
2. **SoopaDriveTool.Windows**: WPF application, embedded fixed PowerShell providers, DiskSpd runner, raw-disk streaming engine, local journal, background scheduled backup entrypoint.
3. **SoopaDriveTool.Tests**: dependency-free assertion runner for policy/parser/backup behavior; optional Windows integration suite using only disposable VHD files. Includes engine-level system-disk denial tests.
4. **Build/release tooling**: pinned engine download with SHA-256, self-contained RID publishing, architecture-aware installer, portable ZIP, release checksums, CI and Pages deployment.

### Privilege and operation model

The app starts with the current user token. Restart as administrator is a user-triggered UAC elevation, with no persistent service. Pending operations are in memory; restarting discards them. No generic elevated shell or arbitrary script execution endpoint is exposed.

Embedded PowerShell scripts receive base64-encoded JSON **data**. User-controlled labels/paths are never interpolated into executable PowerShell. Numeric parameters and allowlisted enums/filesystems bound the operation set. Source scripts are compiled into the application, not loaded from writable loose files.

Before apply, the engine re-enumerates disks and compares disk number plus identity and layout fingerprints. It rejects the running system/boot disk, protected partitions, missing/unknown device identity, changed layouts, and encrypted or unknown-encryption partitions. The PowerShell mutation provider independently rechecks identity/state/layout and protected flags immediately before a command. Only one pending operation may use a particular source/target disk.

This protects against common stale-selection errors, but is not a formal guarantee against every hot-unplug race or malicious local administrator. Device-handle identity hardening remains a production gate.

Raw operations require offline data disks. Destinations are checked for a local drive, path ancestry/reparse points, free space, and separation from the selected raw disk. Image restore verifies source length, sector size and SHA-256 before opening the destination for write. Writes are followed by complete read-back verification. Source/target GPT identities are intentionally not rewritten; clones stay offline until the user disconnects the original.

### Failure behavior

A queue is not an atomic transaction. Clear/undo is available before apply only. If an operation fails, the queue stops, records the error, and does not claim rollback. Format can fail after partition creation; the message explicitly requests a refresh. Losing power or unplugging a disk during raw writing may leave the target incomplete. Interrupted file snapshots are not committed; already written content objects can remain unreferenced.

The journal records Started/Completed/Failed. A Started record after a crash must be treated as an indeterminate operation, not resumed automatically. No automatic reboot, boot configuration modification or background disk mutation occurs.

## Benchmark methodology

Default standard profile: sequential 1 MiB Q8T1 and Q1T1; random 4 KiB Q32T1 and Q1T1. Real world uses Q1 profiles. Each runs read and write; optionally add 70% read/30% write. Multiple passes report arithmetic means, including latency metrics.

Each run creates a uniquely named temporary directory on the selected volume. The file is fully initialized before reads. DiskSpd uses unbuffered/write-through cache policy (`-Sh`), XML results (`-Rxml`), latency (`-L`), one-second warm-up, and selected measurement duration. The shipped defaults use a 256 MiB file, 3-second measured duration and one pass for a quick check; sustained device qualification should use larger/longer workloads.

“Random data” uses a 1 MiB random pattern/write buffer, not an assurance that every byte across the whole file is unique. Zero-fill is explicitly selectable. The engine counts decimal MB/s and binary MiB for file sizes. VM results are functional validation, not physical SSD performance claims. Results are not branded as equivalent to CrystalDiskMark scores.

Temporary data is removed after cancellation/completion. A process crash or power loss can leave a `.soopadrivetool-*` directory; manual cleanup must not target arbitrary user directories.

## Backup format

A repository contains `objects/<sha256>.gz` and `snapshots/<timestamp>-<id>.json`. A snapshot records relative path, length, last-write time and object hash. Each backup hashes source bytes as it compresses them. Content already present is reused. The UI verifies the completed snapshot before reporting success.

Restores validate every path, reject traversal, streams, reserved device names and existing reparse-point ancestors, and require an empty destination. Each file is written to a temporary sibling, hash/length checked, then renamed. Corrupt objects fail explicitly. This is a file-content backup; NTFS ACLs, alternate streams, hardlinks, empty directories and application-consistent open files are not preserved. The object store has no encryption or automatic pruning yet.

Daily scheduling uses Task Scheduler under the current logged-on user without storing a password. It is not an unattended pre-login service. The scheduled entrypoint records success/failure locally. Task management remains available through Windows Task Scheduler.

## Interface specification

- Navy navigation rail, cool white content surface, indigo primary actions, mint positive states. Original drive/platter/lightning logo.
- Nine workspaces: overview, disk manager, benchmark, file backup, cloning/images, partition inspection, health, activity, about/roadmap.
- Overview: compact capacity summary, actual detected disk maps, no synthetic performance or health ratings.
- Disk manager: explicit disk/partition selection and supported operation buttons, staged queue, review dialog and typed destructive-target confirmation.
- Benchmark: configurable workload controls, clear running status/cancel control, tabular results and export/history.
- Backup/clone: source/destination labels written in plain language; conditions visible before preparing changes.
- Layout minimum 1060×700; scrollable main content; scalable WPF rendering, standard tab-focus controls. Full screen-reader, high-contrast and high-DPI testing remains a release gate.

## Original development phases

### 0.1 — initial release

Native ARM64/x64 builds; supported data-disk partition operations; DiskSpd benchmarks; file snapshots; verified offline raw images/clones/overwrite; read-only table inspection; local journal; installers; public repository and website. Exit: compile both RIDs, integration-test ARM in VM, compile/test x64 in CI, render UI, verify installer install/uninstall and published checksums. Physical x64, removable-storage power-loss qualification, and signing remain separate gates.

### 0.2 — operation checks and diagnostics

Refactor UI into view models; common operation-result verification for every command; device-handle identity checks; structured progress; recovery from interrupted jobs; manifest/object locking; disk health charts; benchmark comparison; schedule management; retention; accessibility/localization. Test on SATA/NVMe/USB, 512e/4Kn, BitLocker, removable-device reconnect and standard-user denial.

### 0.3 — encrypted image backup

Implement a VSS requester with writer status and cancellation cleanup; used-block imaging; versioned image container with authenticated encryption; encrypted key storage, retention and verification. Prove restore on independently provisioned machines, not just the originating VM. Establish third-party container/engine licensing if reusing components.

### 0.4 — offline recovery host

Build a recovery application supported in WinPE rather than assuming desktop WPF can run there. Install ADK components explicitly; package architecture-specific drivers/tools; create ISO before offering USB writing. Validate firmware boot, Secure Boot compatibility, keyboard/network/storage access, and bare-metal restoration on ARM and x64. No recovery promise before boot-and-restore tests pass.

### 0.5 — filesystem relocation and migration

Implement or license NTFS/exFAT engines for offline relocation and used-block cloning. Add overlap-safe copy, GPT/MBR update validation, NTFS boot metadata updates, crash journal/checkpointing, BCD/EFI repair and restart recovery. Smaller-target OS migration requires proof of filesystem constraints and bootability after cloning. No online system-disk move until the recovery environment and rollback/recovery design are qualified.

### 0.6 / 1.0 — recovery and stable-release testing

Partition candidate discovery, filesystem-aware deleted-file recovery, read-only preview/export, native sanitize/secure erase with controller allowlists, security review, fuzzing, fault injection, signing certificate procurement, clean-machine installation tests, upgrade/migration tests, and public stable release. A stable release depends on the test results, not a target date.

## Test strategy

Unit/security: identity/layout drift; protected boot disk; encrypted/unknown partitions; unsupported enum/size/filesystem; snapshot dedup/restore/corruption; path traversal/reserved names; MBR/GPT bounds and checksums; benchmark XML/unit handling.

Integration: attach owned disposable VHDs only; initialize/create/resize/label/check/delete; preserve known file payload through resize/image/clone/wipe/restore; full-byte hash checks; cleanup/detach in finally. Real system disk is queried only and explicit attempted mutation must be denied before backend write.

Packaging: self-contained x64/ARM64 output, native PE machine inspection, bundled correct-architecture DiskSpd, installer silent install to an isolated directory, render the installed app without opening a desktop window, uninstall and inspect exit codes. CI should repeat these on x64 and retain logs/artifacts.

Production tests still required: physical ARM/x64 devices, USB bridges, disconnect/power interruption, 4Kn, BitLocker states, low-space/errors, antivirus/SmartScreen behavior, signed update delivery, high DPI/accessibility and actual interactive workflow testing with user permission.

## Initial release status

Version 0.1.0 was an unsigned preview with the operations listed above. Its download page, release notes, and About screen needed to describe the same limits. A successful build alone did not establish physical-device reliability, Windows migration, or recovery-media support.
