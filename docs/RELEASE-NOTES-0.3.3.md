# SoopaDriveTool 0.3.3

- Added an optional automatic disk refresh setting, with 5-, 10-, 30-, and 60-second intervals. The header switch and selected interval are saved between launches.
- Refresh updates connected disk lists without clearing form entries or changing unchanged selections. It pauses during active operations and dialogs.
- Disconnected or changed disks cannot silently transfer a selection or a queued operation to another drive. Affected queues stay visible with Apply disabled until cleared and reviewed again.
- Improved USB and external-drive discovery by combining the Windows Storage and Win32 providers. A failed volume lookup or one unavailable device no longer hides other drives.
- Incomplete device information stays visible with a warning. Disk changes remain blocked until Windows exposes complete information. SMART availability still depends on the drive and USB bridge.

The installers are unsigned. See [test results](VALIDATION-0.3.3.md) for verified coverage and remaining hardware limits.
