# SoopaDriveTool 0.3.0 test results

Test date: October 29, 2026. Destructive tests used disposable VMs and disks. [Supported operations](COMPLETION-MATRIX.md) lists the requirements for each mode. The [test record](QUALIFICATION-0.3.0.json) contains results without private device or account data.

## Engine and filesystem checks

- **53 cross-platform core checks**: authenticated/encrypted backup packets and manifests; independent restore points, retention and passphrase rotation; traversal/length validation; GPT/MBR/EBR; left/right overlapping move fault injection and reviewed resume; recovery, carving, sector guards and protected raw images.
- **125 final Windows ARM64 integration checks**: temporary VHD inventory, partition create/resize/format/rename/delete, raw image/clone/wipe/restore with full read-back, native DiskSpd workloads/cancellation/cleanup, delete → delete → create → dependent rename, stale-plan rejection, activity phases, WIM/DISM metadata preservation, quick reset and verified random/three-pass overwrites, EFI/BCD creation and host-firmware preservation.
- **85 advanced Windows checks**: retained raw handles, overlapping NTFS move, nonempty GPT/MBR and primary/logical round trips, original owner/DACL, ADS/hardlink restore, deleted NTFS MFT/data-run recovery, encrypted raw capture → overwrite → restore with complete read-back and allocation-unit migration.
- **81 final migration checks**: NTFS split/merge, shrink/move/grow redistribution, NTFS-to-exFAT content preservation through a verified checkpoint, refusal to discard unrepresentable named streams, read-only VHD browsing (including Unicode paths) and a custom DiskSpd workload.
- **77 native archive checks**: independently decode managed encrypted/plain repositories, empty/tiny/multi-chunk files, streams and hardlinks; reject wrong passphrases and altered authenticated objects.
- **80 dynamic/storage preparation checks plus 74 post-restart checks**: free-space overwrite without allocated-file damage, surface timings, topology, firmware-erase rejection on a VM, basic → dynamic → basic conversion and NTFS/named-stream preservation after restart.
- **78 final VSS checks**: application-writer preparation/final status, stable snapshot reads while live content changes, encrypted backup and restore, and deletion of only the requester's own snapshot.

Counts include shared baseline checks and must not be summed as unique tests. A controller reporting no SMART access is explicitly unavailable/partial, never a healthy result. A VM SMART report retained its original device output; the unsupported device self-test was rejected with the controller's response.

## Installed scheduled backup and upgrade

An installed SYSTEM daily task created an encrypted VSS backup through an authenticated loopback SMB share in a host-only isolated spare VM. Its secret was DPAPI-protected in a restricted job folder. Enable/disable and Run Now worked. Restoring through the current engine preserved file bytes, the named stream, original owner and DACL (77 focused baseline/schedule checks).

The released 0.2.1 ARM64 installer was installed, created a legacy backup, then upgraded to the 0.3.0 candidate. The current engine verified and restored that legacy backup. Preferences, 35 existing history files and the protected SYSTEM job were preserved. The retained job ran after upgrade. Uninstall removed the installed executable and its scheduled task while retaining backup/preferences data. This upgrade test used source `35c4f40`; subsequent source changes carry/normalize native license notices in generated recovery media, correct native boot-repair WinRE registration and harden SYSTEM job storage ownership. Final installer checks are recorded separately for the released package hashes.

An additional 78 focused permission checks verify atomic protected directory/parent creation and refusal of precreated untrusted storage. An ordinary-user test account could not edit the installed SYSTEM job; a user-owned precreated parent was refused before task creation. A fresh protected installation completed an encrypted VSS task and removed its task successfully.

## Independent Windows boots

The existing 0.2.1 inactive-Windows and exact-sector clones independently booted twice on ARM64 with the source disconnected, as documented in [0.2.1 validation](VALIDATION-0.2.1.md).

The live VSS Windows clone copied a running **256 GiB** source to a **192 GiB** destination using a two-volume VSS set with 11 writers. The destination independently cold-booted twice as its VM's sole disk with Secure Boot enabled. Checks included disk/partition identities, binary/document hashes, NTFS named stream and ACL, account SID, password logon/profile read, HKLM/HKCU markers, installed runtime hash, application rendering and the WinRE image hash. A production one-time helper subsequently enabled WinRE and removed its service; another independent boot passed those checks.

A protected full Windows image captured **109.74 GiB** of resources from live VSS and passed full archive verification. In a RAM-only ARM64 WinPE VM, with the original Windows source absent, the native GUI authenticated/decrypted the image, prepared fresh GPT/EFI/MSR/Windows/recovery partitions, applied Windows with integrity verification, remapped the offline Windows volume, rebuilt UEFI fallback/BCD files and staged first-startup WinRE activation. The first test exposed an unnecessary DiskPart read-only attribute command; the corrected path queries disk state, releases its raw handle before partitioning and verifies that the created volumes belong to the reviewed destination. Retained retry resources are fully hashed against the authenticated manifest before destination writes.

That restored **192 GiB** disk then cold-booted **twice** in a separate VM as its sole disk, with both source and repository disconnected and Secure Boot enabled. Both boots passed the same login/profile/data/stream/ACL/registry/runtime/application checks. WinRE enabled automatically; its one-time service was absent after the first boot and its temporary executable was absent after the second. No manual boot repair was used to obtain these restore boots. The restore engine was `d407f0a`; later native changes add visible phase percentages/indeterminate progress and preserve the same restore commands.

## Recovery media and boot repair

An ARM64 Microsoft ADK/WinPE 26100 ISO booted under Secure Boot. The native GUI restored an encrypted raw image from a separate repository onto a blank 2 GiB disk, with the source absent. It fully read-back verified the destination; after shutdown, an independent host SHA-256 of all 2 GiB matched the source.

The final-media test deliberately removed both EFI launchers only from the twice-boot-qualified spare disk. The initial repair rebuilt boot files and Windows booted, but WinRE became disabled because BCDBoot replaces per-loader values. The repair path now discovers the selected disk's recovery image, accepts the specific offline already-registered response, and stages the constrained one-time activation helper. Repeated staging cancels only this helper's old pending deletion while preserving unrelated pending file operations. The corrected release recovery ISO passed two further independent Secure Boot cold boots: WinRE enabled, the one-time service removed itself, its temporary file disappeared on the second boot, and a separate pending file rename completed normally. The saved BCD backup was independently readable and contained the Windows loader/boot-manager entries; both rebuilt signed launchers matched their original hashes. The native recovery app verified each EFI copy against its live source before BCDBoot. A binary match to the earlier pre-WinPE BCD snapshot was not established: BCD hive bytes changed across Windows processing, including a direct query. The audit reads a disposable copy of the backup store. The boot image's two native hosts and JSON/Brotli notices were independently extracted and matched against the released package byte for byte.

## Interface and package checks

All 10 main screens rendered offscreen. Light/dark overview and benchmark renders were visually checked; screenshots clearly label illustrative demo data. VHD queue execution verifies the activity state/phases and dependent operations. Standard inputs have labels. The site uses new 0.3.0 application renders.

Both hosted ARM64/x64 jobs passed for compiled source `f641913` ([CI run](https://github.com/eMacTh3Creator/SoopaDriveTool/actions/runs/40865256351)). Final installers passed local install/render/uninstall and portable-resource comparison; [package hashes and qualification](QUALIFICATION-0.3.0.json) record those artifacts. Publication and Pages checks are recorded separately. Installers include the self-contained runtime, architecture-correct native app/DiskSpd/WIM/snapshot/recovery components, dependency notices and complete smartmontools/wimlib corresponding source. The SMART collector uses x64 emulation on ARM64.

## Environment and limits

Windows ARM64 qualification used isolated Parallels VMs. The temporary host-only NIC had no external default route; the owned SMB share, mapping and task metadata are removed after qualification. Process-specific Defender exceptions used for repeated owned WIM tests were removed from both source and restored spare VMs; no global antivirus policy was disabled.

VM results do not establish physical SSD throughput, firmware-erase coverage, remapped-cell erasure, disconnect/power-loss resilience, dissimilar-hardware migration, 4Kn/controller/USB-bridge behavior or x64 recovery boots. Physical USB media needs an intended-device boot test. Complete screen-reader/high-contrast/high-DPI coverage and Authenticode signing remain additional qualification. The release is unsigned. These tests do not certify erasure.
