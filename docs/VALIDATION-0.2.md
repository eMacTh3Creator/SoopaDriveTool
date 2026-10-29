# SoopaDriveTool 0.2.0 test results

This is a historical test report. See [0.3.0 test results](VALIDATION-0.3.0.md) for the current release.

The drive-workflow implementation is at source commit `0de6fe5272df55d48f2eb11043b6f0a682d14ca0`. This remains an unsigned development preview. Later documentation and website commits do not change the compiled application inputs.

## Test results

| Environment | Result | Scope |
|---|---|---|
| Windows 11 ARM64, local Parallels VM | **108 checks passed** | Full policy, backup, partition parser, benchmark, disposable-disk mutation, raw imaging/cloning/restoration, wipes, dependent action chains, DISM capture/apply and offline-Windows components. |
| Native ARM64 GitHub Actions | **66 checks passed; installer passed** | Policy, backup/parser/SMART export tests; native DiskSpd and cancellation/cleanup; native installer installation, render and uninstallation. |
| Native x64 GitHub Actions | **108 checks passed; installer passed** | Full expanded integration suite, including offline-hive remapping and EFI/BCD checks; native installer installation, render and uninstallation. |
| Packaged ARM64 app in the local VM | **Passed** | Nine screens rendered from the release portable package; background backup success, verified snapshot and failure reporting. No desktop window/input was used. |
| Website | **Passed locally and deployed** | Desktop 1280px and phone 390px checks; viewport/scroll widths matched; all six images loaded; activity and SMART sections reviewed; no browser console warnings/errors. Live v0.2.0 page, CSS and screenshots matched the repository; all seven release downloads returned HTTP 200 and server digests/sizes matched local files. |

Native packages come directly from [the architecture-matrix CI run](https://github.com/eMacTh3Creator/SoopaDriveTool/actions/runs/40774366489). The local full ARM suite used stable .NET SDK 10.0.401/runtime 10.0.12. The packaged self-contained runtimes are recorded in `BUILD-PROVENANCE.json`; release-package checks used those included runtimes. The bundled license/notices were compared with official runtime/WPF v10.0.14 files and have identical normalized contents.

## Integration test coverage

Destructive checks operate exclusively on owned disposable VHD files. The harness checks their bus/identity, blocks the running system disk, and detaches them afterward, confirming the original disk count is restored.

- Initialize GPT, create/format NTFS and exFAT, resize while preserving file bytes, rename, and scan the filesystem.
- Execute delete → delete → create → dependent rename through the production queue runner. Check activity events and verify the final partition geometry/label.
- Reject a changed baseline before writing and record the preflight failure in activity.
- Copy a whole raw disk, verify full destination read-back, save/restore a raw disk image and recover the original file bytes.
- Apply quick metadata reset and full zero/random/three-pass overwrites, including full read-back verification for every thorough pass.
- Run DISM capture/apply of a source NTFS fixture, verify destination file bytes, restore source read-only state, and receive actual DISM bracketed percentages.
- Reject a non-Windows volume before clearing the clone target.
- Build a GPT/EFI/MSR/NTFS layout on a separate VHD. Use a synthetic saved SYSTEM-hive fixture to verify destination drive-letter/volume-alias remapping, preserve unrelated mappings, leave the source fixture and live host MountedDevices unchanged, and unload the temporary hive.
- Run BCDBoot against an explicit disposable EFI partition, check the BCD store, and compare host firmware entries before/after. No firmware boot entry changes occur in this check.
- Invoke the bundled SMART collector read-only. A virtual/controller-hidden device returns an explicit device/unavailable report rather than a healthy assertion.

Synthetic ATA, NVMe and SCSI fixtures cover raw attributes/counters/logs, failure/partial/unavailable flags, original JSON/text retention, HTML escaping and CSV formula neutralization. They validate parsing/export behavior, not physical-drive health coverage.

## Evidence

- [Full ARM VM results](V02-VM-TEST-RESULTS.txt)
- [Source input comparison](SOURCE-BUILD-CHECK-0.2.json)
- [Packaged screen rendering](UI-RENDER-CHECKS-0.2.json)
- [Packaged background backup](BACKUP-JOB-CHECK-0.2.json)
- [ARM64 installer checks](INSTALLER-ARM64-CHECK-0.2.json) and [x64 installer checks](INSTALLER-X64-CHECK-0.2.json)
- [Native ARM64 results](CI-ARM64-TEST-RESULTS-0.2.txt) and [native x64 results](CI-X64-TEST-RESULTS-0.2.txt)
- [Native CI job summary](CI-SUMMARY-0.2.json)
- [Website checks](SITE-CHECK-0.2.json) and [published asset verification](PUBLICATION-CHECK-0.2.json)

## Not tested for this release

The Windows-aware mode captures/applies an attached inactive Windows volume and configures its destination mappings and EFI/BCD files. **An entire independently booted Windows clone has not been qualified.** Component/layout/fixture tests are not represented as a successful full OS boot. The currently running OS cannot be cloned in this release. WinRE variants, activation, TPM/BitLocker binding, OEM layouts and dissimilar hardware need further qualification.

Physical SATA/NVMe/SAS devices and USB bridge/controller combinations, power-loss/hot-unplug behavior, interactive UAC/accessibility, firmware erase/sanitize and SSD remapped-cell erasure remain outside the executed checks. Quick reset removes partition metadata and leaves file data recoverable. SMART coverage is limited to information the drive/controller exposes. Applied queue steps are not automatically rolled back after a later failure.
