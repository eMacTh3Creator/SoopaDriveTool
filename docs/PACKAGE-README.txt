SoopaDriveTool
Version @APP_VERSION@ - unsigned development preview
Windows 11 ARM64 and x64

Start SoopaDriveTool.exe. The .NET runtime is included. Administrator access
is required to apply disk changes. Review the queued actions before Apply.
Completed actions are not automatically rolled back if a later action fails.

Clone modes
- Byte-exact: matching disk and sector sizes; both disks must be offline.
  All copied sectors receive complete SHA-256 target read-back verification.
- Filesystem-aware: one inactive, unencrypted NTFS volume, a separate target,
  and local scratch storage on a third disk. Copies used files through a
  verified WIM archive; unused sectors and Windows imaging exclusions are
  not copied.
- Windows-aware UEFI: an inactive Windows installation of the host architecture.
  Builds fresh GPT/EFI/MSR/Windows layout, remaps the Windows drive letter,
  configures destination boot files, and copies/registers available WinRE.
  The currently running OS cannot be cloned. No WinPE environment is included.

Disconnect the source before boot-testing the destination. Byte-exact clones
retain duplicate disk identifiers. Boot qualification, hardware binding,
BitLocker, TPM/Windows Hello and activation vary by configuration. Check the
latest validation report before relying on a migration.

Quick wipe resets metadata and leaves data recoverable. Thorough overwrite
verifies each pass, but does not replace firmware SSD sanitize. File snapshots
are unencrypted and do not preserve all NTFS metadata. SMART exports contain
all information the drive/controller exposes; unsupported data is identified.
Keep independent backups before making disk changes.

Application source is MIT licensed. Third-party components retain their own
licenses; see THIRD-PARTY-NOTICES.txt, runtime license files and Licenses/.
Unmodified standalone wimlib-imagex 1.14.5 is GPLv3+; upstream labels ARM64
builds experimental. Full wimlib and smartmontools source archives are included.

Source and current validation:
https://github.com/eMacTh3Creator/SoopaDriveTool
https://github.com/eMacTh3Creator/SoopaDriveTool/blob/main/docs/VALIDATION-0.2.1.md

Downloads and release notes:
https://github.com/eMacTh3Creator/SoopaDriveTool/releases
https://emacth3creator.github.io/SoopaDriveTool/
