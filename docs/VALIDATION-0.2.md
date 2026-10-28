# SoopaDriveTool 0.2 validation

The first expanded Windows ARM64 VM run passed **97 checks**, including the real dependent partition chain, DISM filesystem clone round trip, all wipe modes, SMART parser/export cases and a live bundled-collector invocation. The latter returns an explicit unavailable/device report when the VM hides SMART attributes.

Final expanded checks, native architecture CI, installer packaging and rendered-screen inspection are in progress. This report will be updated with their actual results before publication.

Windows-aware mode is implemented for an attached inactive Windows installation using verified WIM capture/apply plus destination EFI/BCD setup. A full independently booted Windows clone has not been qualified. Physical drive/controller SMART coverage, firmware erase, power-loss and hot-unplug qualification are also outstanding. No live running-OS cloning is claimed.
