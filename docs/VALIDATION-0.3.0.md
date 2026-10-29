# SoopaDriveTool 0.3.0 validation

Qualification is in progress on `feature/complete-storage-suite`; public packages remain 0.2.1 until final release. This document records observed results and will be finalized with release receipts. Tests use owned disposable VMs/disks. The original Windows VM is not a destructive-test target.

## Completed engine checks

- 53 cross-platform core checks: authenticated/encrypted backup format, independent restore points, retention, passphrase rotation, packet/path validation, GPT/MBR/EBR, overlapping move fault injection and resume, recovery/carving/sector guards, surface behavior and protected raw images.
- 85 Windows baseline/advanced checks: retained raw handles; real overlapping NTFS move; nonempty GPT/MBR and primary/logical round trips; owner/DACL, ADS and hardlink restoration; native encrypted decoding; actual deleted MFT record; encrypted raw capture → overwrite → restore with complete read-back; allocation-unit migration.
- Migration checks preserve real files through split/merge/space redistribution and NTFS-to-exFAT conversion. ADS blocks an unrepresentable filesystem conversion before format. Read-only VHD browsing and a real custom DiskSpd workload pass 76 focused baseline/browse checks, including Unicode VHD paths.
- 77 native-format checks verify managed encrypted/plain repositories with empty, tiny, multi-chunk and stream resources, and reject wrong passwords/tampered authenticated objects.
- 80 baseline/dynamic checks verify real free-space overwrite without touching allocated data, surface timing, VM firmware-erase rejection, live topology, Windows basic-to-dynamic conversion, native extent inventory, named-stream preservation and reviewed dynamic-to-basic GPT conversion. Final remount is pending a Windows restart.

Counts include shared baseline checks and must not be summed as unique tests. Existing VSS checks verify stable snapshot reads, writer status and cleanup; the final refresh is pending.

## Independent boot evidence

The existing 0.2.1 inactive-Windows and exact-sector clones independently booted twice on ARM64 with their source disconnected, as documented in [0.2.1 validation](VALIDATION-0.2.1.md).

The new live VSS clone copied a running 256 GiB Windows source to a 192 GiB destination. Capture used a two-volume VSS set with 11 writers. After full capture/apply verification, the destination booted independently twice as the sole attached disk with Secure Boot enabled. Both boots checked actual Windows and partition identities, binary/document hashes, NTFS named stream and ACL, local account SID, password logon/read access, profile, HKLM/HKCU markers, installed runtime hash, application render and WinRE image hash. Its initial WinRE activation state is being addressed by a constrained first-startup helper.

The ARM64 recovery ISO built from Microsoft ADK/WinPE 26100 booted under Secure Boot in a RAM-only spare VM. With only an 8 GiB repository disk and a blank 2 GiB destination attached, the native GUI authenticated, restored and fully read-back verified an encrypted protected raw image. After shutdown, an independent host SHA-256 of the complete destination matched the source. The source disk was absent.

Protected Windows-image capture/verification and native WinPE Windows restore/independent boot checks are still in progress. A captured image is not counted as boot-qualified.

## Release checks still in progress

Final one-time WinRE registration checks; Windows restore boots; installed SYSTEM scheduling/network repository exercise; refreshed UI/theme and integration checks; final native ARM64/x64 installer/portable builds; clean install, upgrade and uninstall; hosted architecture CI; versioned documentation, source and website publication; release download checksum verification.

## Test environment and limits

Windows ARM64 runs in isolated owned Parallels VMs. A temporary process-specific Defender exception was used for the owned WIM qualification executables to make repeated full-OS I/O tests practical; it is tracked for removal. No global antivirus policy is disabled. Functional timings/performance in this environment do not establish physical SSD throughput.

VM checks cannot establish firmware erase coverage, SSD remapped-cell erasure, physical disconnect/power-loss resilience, dissimilar-hardware migration, 4Kn/controller/USB bridge behavior, or x64 recovery-boot coverage. Native x64 compilation/execution and hosted x64 integration are separate checks. Recovery-media USB writing needs intended-device boot qualification. Full screen-reader/high-contrast/high-DPI coverage and Authenticode signing remain additional release qualification. The release is unsigned and does not claim certification or exact parity with every commercial-suite feature.
