# SoopaDriveTool 0.1.0 test results

This is a historical test report. See [0.3.0 test results](VALIDATION-0.3.0.md) for the current release.

Date: October 28, 2026. The release is unsigned and intended for evaluation on disposable data disks.

## Windows ARM64 VM

Stable .NET 10.0.401, native ARM64 Windows 11. Full suite: **62 checks passed**. [Detailed output](VM-TEST-RESULTS.txt).

Coverage includes identity/layout drift and protected-system-disk denial; BitLocker/unknown encryption guards; snapshot integrity/deduplication/restore/corruption/path safety; MBR/GPT parsing and CRCs; DiskSpd workloads and cancellation cleanup; GPT initialization, aligned NTFS/exFAT creation, resize with retained file bytes, rename and filesystem scan; raw image, clone, zero-overwrite and restore with full read-back; original file recovery; delete; VHD cleanup and original disk-count restoration.

All destructive integration work used newly created temporary VHD files. Existing user data disks were not modified.

## Installers and interface

All 31 tracked build inputs were hash-matched between the local committed source and the VM build checkout. [Source/build check](SOURCE-BUILD-CHECK.json). Both self-contained packages were built in the Windows ARM VM. The ARM package was launched natively; the x64 package was launched under Windows ARM emulation. Both passed silent install, installed-app offscreen rendering, app/engine PE machine validation, and silent uninstall. See [ARM check](INSTALLER-CHECK-win-arm64.json) and [x64 check](INSTALLER-CHECK-win-x64.json).

The expected machine codes are `0xaa64` (ARM64) and `0x8664` (x64) for both apphost and DiskSpd. The Inno Setup bootstrapper itself can use emulation on ARM; the installed ARM app/engine are native.

All nine application screens rendered successfully without a desktop window. Screenshots for overview, disk management, benchmarking and file backup were visually inspected, including the final rounded selectors. [Render results](UI-RENDER-CHECKS.json).

The background file-backup entrypoint created and verified a snapshot, returned a successful exit code, and recorded an explicit failure for an invalid source. [Background-job check](BACKUP-JOB-CHECK.json). Daily trigger timing and logged-on-user scheduling remain unqualified.

## Hosted CI

[Final native ARM64/x64 CI run](https://github.com/eMacTh3Creator/SoopaDriveTool/actions/runs/40764168975) validates source commit `87675ba9a4e37ac03e3e2a5c2840ad932a3e7070`. ARM64: **43 checks passed**; x64: **62 checks passed**. Both architecture jobs also passed installer install/render/uninstall. [ARM64 output](CI-ARM64-TEST-RESULTS.txt) · [x64 output](CI-X64-TEST-RESULTS.txt).

The native x64 job runs the full disposable-VHD integration suite. The native ARM job runs core/backup/parser and live benchmark/cancellation checks plus installer install/render/uninstall. Full ARM VHD testing is covered by the local native ARM VM above.

An earlier Azure ARM job showed a 43-second initial storage inventory and roughly two minutes for each mutation plus repeated identity/layout validation. It was cancelled after successful initialization/create/resize checks, rather than reported as a full pass. The hosted ARM job therefore uses the faster checks; full ARM integration remains available through `scripts/test.ps1 -Runtime win-arm64 -Integration`.

A native x64 cancellation-cleanup failure was detected during validation, fixed in the process/cleanup handling, and included in subsequent regression checks. No failing build artifacts are used for the release.

## Website and publication

Desktop and 390-pixel mobile layouts were visually inspected in a hidden browser. Desktop document width matches the 1280-pixel viewport after decorative overflow was fixed. Page assets loaded successfully. GitHub Pages returned HTTP 200 and contained the product identity and both architecture download links. The release includes architecture-specific installers/portable ZIPs, a combined SHA-256 file, and build provenance. Download endpoints and GitHub-reported asset digests are checked during publication.

## Not tested for this release

Physical ARM/Intel/AMD hardware; actual SSD performance; hot-unplug/power interruption; the USB/NVMe/RAID and 4Kn matrix; full BitLocker-state coverage; Authenticode signing; clean-machine upgrade/migration; interactive workflows; keyboard/screen-reader/high-contrast/high-DPI testing; live OS migration; WinPE boot and bare-metal recovery are not established.

VM tests used guest commands and offscreen rendering. Browser checks ran in a background tab.
