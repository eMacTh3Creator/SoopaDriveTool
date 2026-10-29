# SoopaDriveTool 0.3.0

Version 0.3.0 adds live Windows cloning, encrypted Windows image recovery, and more partition operations. ARM64 and x64 installers and portable ZIPs are available.

## Added

- Clone the running Windows installation through VSS. Other modes copy an inactive Windows installation, used NTFS files, or every sector.
- Restore encrypted disk and Windows images from WinPE recovery media.
- Move, split, merge, and redistribute partitions. Convert supported partition tables and filesystems using verified recovery checkpoints.
- Create encrypted full, incremental, and differential backups with exclusions, retention, selective restore, and user or SYSTEM schedules.
- Find supported lost partitions and deleted files, inspect sectors, view surface maps, and use quick reset or verified disk overwrites. Firmware erase is available for eligible devices.
- Save custom benchmark profiles, compare runs, and export the SMART information exposed by the drive. Activity shows the current phase and remaining steps in light, dark, or system theme.

## Fixes and safeguards

- Windows restore now checks the destination's state before partitioning and verifies retained temporary archives before reusing them.
- UEFI repair restores WinRE registration after rebuilding boot files. Repeating the temporary setup helper preserves unrelated pending file operations.
- SYSTEM schedules reject job folders with untrusted ownership or permissions.

## Testing

Live Windows cloning, full Windows image restoration, and EFI repair passed independent ARM64 Secure Boot tests with the source disconnected. ARM64/x64 builds, temporary-disk integration tests, scheduled SMB backup/restore, and installer checks also passed. The [validation report](VALIDATION-0.3.0.md) identifies the test revisions and remaining limits.

The installers include the .NET runtime, required engines, license notices, and corresponding smartmontools/wimlib source. Recovery media uses Microsoft boot files from your installed ADK. SHA-256 checksums accompany the downloads.

## Known limits

This is an unsigned preview. Physical firmware erasure, x64 recovery boots, USB/4Kn hardware, power interruption, and complete accessibility support still need testing. See the [user guide](USER-GUIDE.md) and [supported operations](COMPLETION-MATRIX.md) before using a destructive operation.
