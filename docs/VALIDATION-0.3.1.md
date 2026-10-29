# SoopaDriveTool 0.3.1 test results

Test date: October 29, 2026. This patch changes interface text, the Overview layout, developer metadata, and version display. The storage engines are unchanged from 0.3.0; the [0.3.0 report](VALIDATION-0.3.0.md) records their clone, restore, recovery, and hardware test coverage.

## Interface

All 11 main screens rendered in light and dark themes using the released ARM64 portable package. The main light-theme screens and dark Overview were visually reviewed. The installed ARM64 application also rendered Overview and About.

The review checked that page names and functional descriptions remain, the Overview banner is removed, the sidebar footer contains only `v0.3.1`, and About shows Ev-Tech Solutions, LLC. The removed slogans were also checked against the released managed assembly.

## Builds and packages

[Native ARM64/x64 CI](https://github.com/eMacTh3Creator/SoopaDriveTool/actions/runs/40897097612) built the installers and portable ZIPs from `ed0361c3e4e3438c403851e496951b65a04d6ace`. Both jobs passed their behavioral checks, archive compatibility checks, SYSTEM schedule-permission checks, and installer install/render/uninstall checks. The x64 job ran the full temporary-VHD integration suite; the hosted ARM64 job used the benchmark subset, as described in the workflow.

In the spare ARM64 VM, the released installer upgraded 0.3.0 to 0.3.1. The installed application matched the portable package. Both the apphost and managed assembly reported Ev-Tech Solutions, LLC, and the Windows uninstall entry registered that publisher and version 0.3.1.

The x64 portable package also launched and rendered Overview and About under Windows ARM emulation. Its company and version metadata were checked separately. Native x64 installation was checked by CI.

Release checksums identify the published files. Later documentation and website edits do not change the compiled application source.

## Limits

The installers remain unsigned. The storage and physical-hardware limits documented for 0.3.0 still apply. Interface renders use labeled sample data; they are not measured drive results.
