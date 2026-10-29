# SoopaDriveTool 0.2.1 test results

This is a historical test report. See [0.3.0 test results](VALIDATION-0.3.0.md) for the current release.

Application sources: [`105dc5e6af742803f77ddc774728225b04862a0d`](https://github.com/eMacTh3Creator/SoopaDriveTool/commit/105dc5e6af742803f77ddc774728225b04862a0d). This remains an unsigned development preview. Documentation and site changes after that commit do not change the compiled application inputs.

## Windows clone boot tests

**Both Windows-aware UEFI and byte-exact clones booted independently on Windows 11 ARM64. Each destination passed two distinct cold boots with only its own cloned disk attached.**

The source was a privately prepared, independently booted Windows 11 Pro for Workstations ARM64 installation, build 26200.9711, on a 256 GiB disk. Controlled fixtures covered a local account, profile document, NTFS alternate stream and security descriptor, registry marker, binary payload and installed SoopaDriveTool 0.2.0. Repeated temporary installer extractions and development caches were removed from this disposable source before the final capture; its used allocation was approximately 127.3 GiB. Windows, installed applications, user profiles and the controlled fixtures remained. No working-user disk was used as a clone destination.

The source disk identifiers were made distinct from the lab host and its Windows mapping/boot configuration was repaired and boot-checked as fixture preparation. A fixed VHD containing the source sectors was then attached through Windows' native virtual-disk driver. This preparation is not counted as a SoopaDriveTool clone. The destination disks were created by the production `QueueRunner`, `FileSystemCloneEngine` and `RawDiskEngine` through the guarded qualification harness.

Windows-aware mode captured/applied the full selected NTFS volume, created fresh GPT identifiers and EFI/MSR/Windows/recovery layout, remapped the destination SYSTEM hive, and built destination boot files. Byte-exact mode copied all 256 GiB and passed complete target read-back SHA-256 verification. The raw disk and partition identifiers intentionally remained duplicated.

Each destination was booted alone in a headless Parallels UEFI VM with Secure Boot, four CPUs and 8 GiB RAM. Guest checks confirmed a new kernel boot after cloning, the expected destination image, one attached disk, C: mapping, fixture and profile-document hashes, alternate stream, NTFS owner/security descriptor, local account SID, successful password authentication and document access under that account, registry marker, installed executable hash and successful application rendering. The recovery image matched the source SHA-256. Windows-aware EFI/recovery access letters were removed and the source's original read-only state was restored.

To prevent real-time antivirus scanning from dominating this disposable lab test, temporary exclusions covered only the lab test source/backing VHD, WIM staging/capture tool and owned destination. They were removed after cloning. No production security settings were changed. Capture timing is not a general performance benchmark.

The full-OS harness used self-contained .NET 10.0.12 and the committed engine sources above. Released packages include .NET 10.0.14 and passed native CI integration/component, installer and packaged runtime checks. This distinguishes the source-level full-OS qualification from validation of the exact released packages.

See the [sanitized clone and boot receipt](CLONE-BOOT-CHECK-0.2.1.json) for timestamps, per-mode results, source-code hashes and recovery state. Credentials, user SIDs, private paths, disk images and diagnostic traces are excluded.

The final ARM64 installer also upgraded the independently booted clone from 0.2.0 to 0.2.1. Installed application/engine files and the replacement release guide matched the final portable package hashes. Its installer hash is recorded in the clone/boot receipt.

## Changes in this patch

- Discover Windows RE on the selected source's registered or hidden recovery partition, including installations without a staged `Windows\System32\Recovery\Winre.wim`.
- Read recovery registration with external XML entities disabled, reject escaping paths and ambiguous images, and limit discovery to the selected source disk.
- Reserve recovery space from the image size, with at least 1 GiB and an additional 300 MiB allowance over larger images. Apply the required/no-default-letter GPT attributes and verify the copied image hash.
- Replace the slow DISM capture scan with pinned native wimlib 1.14.5, strict security-descriptor capture and complete archive verification before destination writes; retain Microsoft DISM apply with integrity/resource verification. Include the upstream licenses and full source archive. Upstream labels its ARM64 build experimental.
- Report scanning file counts, capture, integrity-table and decompressed-resource verification progress without finishing a phase early.
- Remove temporary recovery and EFI letters after cloning. Show initial WIM enumeration as an indeterminate scanning phase instead of a misleading fixed percentage.

The recovery sizing follows Microsoft's [Windows RE guidance](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/deploy-windows-re?view=windows-11). EFI boot preparation follows [BCDBoot's explicit-target workflow](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/bcdboot-command-line-options-techref-di?view=windows-11).

## Other tests

| Environment | Result | Scope |
|---|---|---|
| Local Windows 11 ARM64 VM | **123 passed** | Full safety, backup, parser, benchmark and disposable-VHD integration suite, including hidden recovery discovery and offline hive/EFI checks. |
| Native x64 CI | **123 passed; installer passed** | Full integration suite; install, render and uninstall. |
| Native ARM64 CI | **75 passed; installer passed** | Core, report/parser and live native benchmark/cancellation checks; install, render and uninstall. |
| Actual ARM64 release portable package | **Passed** | Nine screen renders; background backup success, snapshot verification and failure reporting; all tracked build inputs matched. |
| Website | **Passed locally** | Hidden-browser 1280px and 390px checks, no horizontal overflow, all six images loaded, no console warnings/errors. Publication is verified separately. |

Packages are the exact artifacts from [native CI run 40810726511](https://github.com/eMacTh3Creator/SoopaDriveTool/actions/runs/40810726511). Bundled engine/source checksums, framework versions and build provenance accompany the release.

## Evidence

- [Full ARM integration results](VM-ARM64-TEST-RESULTS-0.2.1.txt)
- [Native ARM64](CI-ARM64-TEST-RESULTS-0.2.1.txt) and [native x64](CI-X64-TEST-RESULTS-0.2.1.txt) results; [CI summary](CI-SUMMARY-0.2.1.json)
- [ARM64 installer](INSTALLER-ARM64-CHECK-0.2.1.json) and [x64 installer](INSTALLER-X64-CHECK-0.2.1.json)
- [Source input comparison](SOURCE-BUILD-CHECK-0.2.1.json), [packaged UI](UI-RENDER-CHECKS-0.2.1.json), [background backup](BACKUP-JOB-CHECK-0.2.1.json)
- [Build provenance](BUILD-PROVENANCE-0.2.1.json), [site checks](SITE-CHECK-0.2.1.json), and [publication checks](https://github.com/eMacTh3Creator/SoopaDriveTool/blob/main/docs/PUBLICATION-CHECK-0.2.1.json)

## Not tested for this release

Full OS boot qualification is **ARM64 on Parallels UEFI**. Native x64 integration tests do not establish x64 OS boot. Physical controllers/devices, 4Kn, USB bridges, hot-unplug/power failure, activation, Windows Hello/TPM binding, encrypted sources and dissimilar hardware remain unqualified. A matching recovery-image hash alone does not establish a separate Windows RE boot. The currently running OS cannot be cloned, and no WinPE recovery environment is shipped.

WIM modes require inactive, mounted, unencrypted NTFS sources and separate scratch storage; Windows imaging exclusions apply. Byte-exact mode requires matching disk and sector sizes, and the source must be disconnected before its identical clone is brought online. Applied queue steps are not automatically rolled back after a later failure. SMART reports reflect only what the drive/controller exposes; VM tests are not physical SMART qualification.
