# Clone modes, wiping, queues, and SMART in the 0.2 series

Written October 28, 2026. This page describes the features introduced in the 0.2 series, including the 0.2.1 cloning changes. For the current release, use the [user guide](USER-GUIDE.md) and [test report](VALIDATION-0.3.0.md).

## Reference workflows

[EaseUS Disk Copy’s manual](https://www.easeus.com/disk-copy/clone-disk-manual.html) distinguishes layout-preserving cloning, destination resizing and sector-level copying. Its [user guide](https://www.easeus.com/tutorial/disk-copy-user-guide.html) explains why a used-data clone may fit a smaller destination while a sector copy has different capacity requirements. The clone choices therefore distinguish sector copies from file-based and Windows-aware copies.

[Paragon’s documentation](https://www.paragon-software.com/home/hdm-windows/) treats operating-system migration, disk/partition copies, and recovery as separate capabilities. Its [copy workflow example](https://kb.paragon-software.com/article/1598) also shows selective/resize/raw choices followed by preview and Apply. The operation queue supports references to partitions that an earlier step will create.

The Windows-aware implementation follows Microsoft’s [WIM capture/apply workflow](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/capture-and-apply-windows-using-a-single-wim?view=windows-11) and [BCDBoot](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/bcdboot-command-line-options-techref-di?view=windows-11). A specified destination EFI partition keeps BCDBoot from changing the current host’s firmware boot entry.

Full SMART collection uses [smartmontools 7.5](https://github.com/smartmontools/smartmontools/releases/tag/RELEASE_7_5). Its [command reference](https://github.com/smartmontools/smartmontools/blob/main/src/smartctl.8.in) documents Windows physical-disk addressing, JSON/original output and exit-status health flags.

## Clone choices

| Mode | What it copies | Requirements and checks |
|---|---|---|
| Byte-exact whole disk | Every source sector, including unused/deleted-data regions and partition metadata | Equal disk size and logical sector size. Both disks offline. Full target read-back SHA-256 comparison. The source’s partition/disk identifiers remain duplicated, so disconnect it before putting the clone online. |
| Filesystem-aware NTFS volume | Used file resources from one selected NTFS volume; creates fresh GPT/MSR/NTFS layout | Mounted, unencrypted, non-system source. Source temporarily read-only. Native wimlib WIM capture with strict permission preservation and full archive verification, followed by DISM apply with integrity/resource verification. Destination may be smaller than the source disk when the used content and overhead fit. |
| Windows-aware UEFI | Offline Windows volume plus a newly built EFI/MSR/Windows layout; WinRE discovered on the selected source, including registered/hidden recovery locations, copied and hash-verified | Inactive Windows installation on an attached data disk, matching host/source architecture, sufficient target capacity, and WIM scratch space on a third disk. Configures destination EFI/BCD and checks boot-file presence. Does not clone the currently running OS. |

The filesystem/Windows modes use temporary WIM staging rather than trying to relocate opaque filesystem blocks. Original free-space bytes are not copied. Windows imaging exclusions apply (for example paging/hibernation files, recycle-bin content and volume metadata); use byte-exact mode when those bytes must be retained. Current source encryption and the running system disk are blocked. A filesystem-aware volume clone is not a bootable OS clone unless Windows-aware mode is selected and all its checks succeed.

The applied destination SYSTEM hive keeps the source installation’s Windows drive letter: matching source volume identifiers are replaced with the new destination identifier. The host registry is read for identifiers, while writes are confined to a uniquely mounted destination hive, which is unloaded afterward. An ambiguous original letter stops the operation instead of guessing. This follows the Windows [persistent mount-name database](https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/supporting-mount-manager-requests-in-a-storage-class-driver) and [offline hive load](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/reg-load)/[unload](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/reg-unload) mechanisms.

Windows-aware cloning does not guarantee activation, TPM/BitLocker binding, all OEM recovery layouts, dissimilar-hardware boot or every firmware’s behavior. Version 0.2.1 passed independent Windows-aware and byte-exact ARM64 VM cold boots; see the [0.2.1 test results](VALIDATION-0.2.1.md). Each migration still needs a destination boot check. x64 OS boot and physical hardware remain unqualified.

## Wipe choices

- **Quick partition reset:** removes disk partition metadata using Windows Clear-Disk. Existing file contents may remain recoverable. Suitable for quickly preparing a disk for a new layout, not for confidential-data disposal.
- **Zero overwrite:** overwrites every user-addressable sector with zeros and reads the entire disk back for verification.
- **Random overwrite:** writes cryptographically generated random bytes, calculates their SHA-256, and verifies a full read-back hash.
- **Three pass:** random bytes, then ones, then zeros; full read-back verification after each pass. Reports pass/phase, bytes, throughput and remaining-time estimate.

None of these modes is labeled ATA Secure Erase, NVMe Sanitize, cryptographic erase, or a compliance-certified wipe. Addressable-sector overwrites cannot guarantee erasure of remapped/overprovisioned SSD cells. A SMART condition report is not an erasure certificate.

## Dependent action chains

Example: delete partition A → delete partition B → create a new NTFS partition in the combined free region → rename the newly created partition → Apply.

The preview projects the resulting disk layout without writing it. New partitions have temporary references, so later steps can target them before Windows assigns real partition numbers. Removing a step removes that step and all subsequent steps, preserving dependency order.

Apply validates the original disk identity/layout, then validates the last verified state before every step. A successful create is matched by geometry, filesystem and label, and its real partition number resolves the temporary reference. Unexpected external changes stop the queue. Each step’s result is checked before dependent steps proceed.

A user-triggered administrator restart preserves the pending plan in the same user’s private app-data directory. The elevated app validates the disk baselines again and requires another explicit review/apply; it never automatically executes a handoff file. Switching to a different Windows administrator account is not supported for this handoff.

Queue execution is sequential, not transactional. If a later operation fails, earlier completed operations remain applied; later steps are marked skipped. Cloning and raw restoration are terminal steps for the disks they use because their resulting layouts are not projected as ordinary partition edits.

## Live activity

The activity screen shows overall completed-step percentage, the active phase, an exact step percentage where available, bytes copied/verified, throughput and estimated time remaining for raw I/O, completed/current/pending/skipped steps, timestamped events, elapsed time and rotating lighthearted messages/facts.

Windows commands that do not provide progress use an indeterminate bar with an explanation. Overall percentage is based on reviewed steps, not a prediction of total wall-clock time. WIM scanning reports file counts without a guessed percentage. Capture, archive-integrity/resource verification and DISM apply report measured tool progress; raw progress derives from actual bytes processed. Events are throttled and journals are saved after each step and at failure/completion.

Run exports include disk model, serial, identity hash, steps, states, and event details. Reports stay local unless you save or share them.

## Full SMART and resale reports

The collector requests all available SMART attributes and extended logs using `smartctl -x --json=o`. Original JSON and original plaintext are retained; nonzero health/error-log exit flags are decoded instead of being treated as an ordinary process failure.

ATA reports include normalized/current/worst/threshold/raw attributes and failure markers. NVMe reports retain critical warning, temperatures, spare/wear, read/write counters, media/data-integrity errors, unsafe shutdowns and available error/self-test logs. SCSI/SAS counters and defect/error logs are preserved. All extra device-specific fields returned by the collector remain in the complete payload.

Export formats: complete JSON, full-field CSV, plaintext and printable standalone HTML with identity, collection time, health flags, attribute tables, all fields, original text and original JSON. HTML escapes device-provided strings. Serial mismatches between Windows inventory and SMART are flagged for physical-label verification.

SMART access depends on the controller/USB bridge and device firmware. Unavailable access and incomplete results are marked explicitly. Drive-reported failures remain visible. These reports record condition at collection time; they are not warranties or predictions of remaining life.

The collector is a separate GPL executable. The exact upstream corresponding-source archive and GPL license are included with packages. Windows ARM runs the x64 smartctl executable through Windows emulation; the application and DiskSpd remain native ARM64. No smartd service is installed.

## Test scope

Tests use owned disposable VHD files for destructive actions and synthetic ATA/NVMe/SCSI payloads for report coverage. A VM often exposes no physical SMART attributes. Full Windows-aware and byte-exact ARM64 VM boot checks are recorded for 0.2.1. Physical controller coverage, x64 OS boot and dissimilar hardware still require qualification. See the versioned test reports for the checks completed and remaining limits.
