# SoopaDriveTool 0.3.2 test results

This update requests Windows administrator approval before opening the interactive application. Background commands retain their existing Task Scheduler account and run level. Declining UAC approval closes the application; disk changes still require review and Apply.

## Checks

- A filtered interactive task in the spare ARM64 VM displayed the Windows UAC prompt before the application window opened. An already-elevated normal launch produced one administrator process without another prompt or relaunch loop. The cancellation and pending-plan paths were also reviewed in source.
- The released ARM64 package completed a backup from a genuinely LIMITED interactive task. The wrapper token was not administrator, the app exited successfully, and the saved manifest and decompressed gzip payload matched the source. No UAC prompt appeared for the background job. The temporary task was removed.
- Both native ARM64/x64 builds passed their behavioral, archive, schedule-permission, and installer install/render/uninstall checks in [CI](https://github.com/eMacTh3Creator/SoopaDriveTool/actions/runs/40902007065).
- The installed ARM64 application matched the portable package. Overview, Activity, and About rendered in both themes, and the installed app rendered Overview and About. The x64 portable package rendered Overview and About under ARM emulation; native x64 installation was checked by CI.

Packages were compiled from `3b5cf2ed697259ce64e27b16716e6e4e51650410`. Later documentation and site edits do not change the compiled application. The startup prompt and already-elevated tests used the same production source; the LIMITED backup check used the released package.

The installers are unsigned. Storage behavior and hardware test limits remain described in the [0.3.0 report](VALIDATION-0.3.0.md).
