# SoopaDriveTool 0.3.0

The storage suite adds live Windows migration, protected Windows-image recovery and advanced partition workflows to the ARM64/x64 drive workspace.

- Clone a running Windows installation through VSS, clone inactive Windows, copy used files or copy every sector. Restore encrypted Windows/raw images through native WinPE media.
- Move, split, merge and redistribute partitions; convert supported nonempty layouts and filesystems with verified recovery checkpoints. Queue dependent changes and review/apply them as one chain.
- Create encrypted full, incremental and differential backups with retention, exclusions, selective restore, metadata preservation and managed user/SYSTEM schedules.
- Recover supported lost partitions/deleted-file candidates, inspect/edit reviewed sectors, read surface maps and storage topology, request eligible firmware erasure, or use quick reset and verified overwrites.
- Export complete available SMART data, save custom benchmark profiles, compare runs and follow measured/indeterminate activity in light/dark/system themes.

ARM64 Windows live cloning and protected-image restoration passed independent Secure Boot cold boots with the source disconnected. Hosted ARM64/x64 builds, actual VHD/metadata checks, scheduled SMB backup/restore and install/upgrade/uninstall checks are documented in [validation](VALIDATION-0.3.0.md). Counts include shared baselines.

The installers and portable ZIPs include the runtime, engines, notices and smartmontools/wimlib corresponding source. Microsoft WinPE boot files come from your own installed ADK. SHA-256 checksums are attached.

This is an unsigned preview. Physical firmware-erase coverage, x64 recovery boots, USB/4Kn/power-loss behavior and complete accessibility qualification remain hardware/release limits. Read the [user guide](USER-GUIDE.md) and [feature conditions](COMPLETION-MATRIX.md).
