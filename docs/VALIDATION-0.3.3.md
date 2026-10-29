# SoopaDriveTool 0.3.3 test results

This update adds optional automatic disk refresh and improves USB and external-drive discovery.

Packages were compiled from `ab97a94aff758c54277e991d9083f88380ae0a6e`. [Build and installer checks](https://github.com/eMacTh3Creator/SoopaDriveTool/actions/runs/40910688936) run on native ARM64 and x64 Windows. Later documentation and website edits do not change the application.

## Checks

- Both native architecture builds passed their behavioral, archive, schedule-permission, and installer install/render/uninstall checks in CI. The x64 run also passed the full disposable-disk integration suite.

- The spare ARM64 Windows VM passed 70 Core checks and 110 behavioral checks. New coverage includes disk-number reuse, changed and disconnected queued drives, incomplete device information, retained clone settings and partition selections, refresh intervals, and timer shutdown. These totals include existing shared checks.
- A separate live test exercised the production five-second DispatcherTimer without opening a window or using manual Refresh. It detected an attached empty 128 MiB VHD and its removal. All six checks passed, including fixture removal and restoration of the previous preferences; the scenario took about 17 seconds.
- Ten read-only PowerShell inventory fixtures covered fixed and removable USB drives, fallback filesystem details, partial provider failures, empty RAW disks, and errors affecting one drive. Live inventory also enumerated the spare VM's existing disks without warnings.

- The released ARM64 installer upgraded the spare VM from 0.3.2 to 0.3.3. The installed application matched the portable package. Overview, Disk manager, Settings, Clone & image, Health, and Activity rendered in both themes; installed Overview and About also rendered. The Settings screen was visually checked in light and dark themes. Version and Ev-Tech Solutions, LLC metadata matched.

No physical USB drive was attached to the VM. USB discovery coverage is fixture-based; adapter-specific SMART passthrough, firmware commands, and physical USB hardware still need qualification. A device that Windows cannot describe completely is shown with a warning and cannot be used for disk changes.

The installers are unsigned. Earlier storage and hardware test limits remain described in the [0.3.0 report](VALIDATION-0.3.0.md).
