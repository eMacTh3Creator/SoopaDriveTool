# Validation status — 0.1.0 preview

Date: October 28, 2026.

## Verified so far

- Stable .NET 10 SDK native ARM64 build: zero warnings/errors.
- First complete Windows 11 ARM64 VM run: **56 checks passed**. See [VM test output](VM-TEST-RESULTS.txt).
- Integration used owned temporary VHDs, not existing user data disks. Passed creation/formatting/resizing/label/scan, native DiskSpd workloads, full raw image/clone/overwrite/restore read-back, original file-byte recovery, delete/exFAT creation, and final disk-count cleanup.
- System-disk mutation request was rejected by the backend.
- Desktop and 390-pixel mobile website visuals inspected in a hidden browser. Temporary QA harness is not part of the delivered site.

## Final checks in progress

Both architecture installer packaging, install/render/uninstall, final expanded parser tests and hosted architecture CI are in progress. This document will be updated with their actual outcomes before release.

## Not established

Physical ARM or Intel/AMD hardware behavior; actual SSD performance; power-loss/hot-unplug recovery; every USB/NVMe/RAID controller; 4Kn media; full BitLocker-state matrix; Authenticode signing; clean-machine migration/upgrade; interactive UI workflows; screen-reader/high-contrast/high-DPI qualification; live system migration; WinPE boot or bare-metal recovery.

The desktop was not controlled. VM work ran through background guest commands, and application rendering used an offscreen WPF visual without opening a desktop window.
