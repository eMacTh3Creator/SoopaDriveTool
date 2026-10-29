# SoopaDriveTool 0.3.4 test results

This update adds selectable partition maps, context menus, partition-content erasure, and Yes/No confirmation with No as the default. Typed destination phrases have been removed from the Windows and recovery interfaces.

Compiled application source: `8762ba916a11fd021e888150a6bf1d7a2f285e00`. Final [native ARM64/x64 build and installer checks](https://github.com/eMacTh3Creator/SoopaDriveTool/actions/runs/40923746424) include the corrected visual fixture and disk-selection regression check. Later documentation and website edits do not alter the application.

## Checks

- Both final native ARM64/x64 jobs passed all checks, including selected-partition erase/reformat, installer installation, rendering, and uninstall. Twelve interface captures show the map, real context menu, and No-default confirmation in both themes on both architectures. The map remains selected through queued changes and restores after clearing them.

- UI checks cover left/right-click selection, selection highlighting, keyboard focus, context-menu actions, protected and unavailable partitions, unchanged queued baselines, and rejection of replaced disks. Confirmation checks cover the default No button, cancellation without losing the queue, immutable reviewed steps, and rejection of changes made after review.
- Core erasure tests cover zero, random, and three-pass overwrites; exact partition bounds; read-back verification; cancellation; and failures that must not report completion.
- Native Windows tests use a new owned 128 MiB VHD containing two 32 MiB NTFS partitions. The selected partition is overwritten and verified; every byte outside its extent is compared with the original, including the retained GPT table. The disk is then brought online and formatted through the normal queue. A file on the neighboring partition must remain unchanged. The fixture is detached and deleted afterward.
- The offline-encryption fix is checked against changed primary and backup boot records, changed identity or geometry, cold offline disks, known BitLocker and unknown states, controller errors, and stale inventory generations. It reuses an online result only while validated native filesystem metadata still matches. It does not classify an unknown cold offline disk as unencrypted.

The first clean ARM64/x64 run passed all ten VHD erase/reformat checks on each architecture; its separate visual fixture needed a sample-selection correction. The local spare VM also passed 162 interface and behavioral checks before the encryption-evidence fix. Its shared-folder service then stalled, so final validation uses clean Windows build VMs. The original Windows VM was not changed.

Physical-drive firmware behavior, USB bridges, and power interruption have not been newly qualified. Native recovery's No-default confirmation is compiled and reviewed; recovery boots were not repeated for this interface update. The installers are unsigned. Earlier hardware limits remain described in the [0.3.0 report](VALIDATION-0.3.0.md).

## Implementation references

Windows encryption queries use the documented [Win32_EncryptableVolume](https://learn.microsoft.com/en-us/windows/win32/secprov/win32-encryptablevolume) interface and [GetEncryptionMethod](https://learn.microsoft.com/en-us/windows/win32/secprov/getencryptionmethod-win32-encryptablevolume) result. Provider errors remain unknown.
