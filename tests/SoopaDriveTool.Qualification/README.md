# Windows clone boot tests

This manual test harness calls the production `QueueRunner` and clone engines. It is separate from the automated temporary-VHD integration tests. Clone tests run only on a dedicated Windows machine named `SDTLAB`, with explicit source/destination fingerprints and a GUID fixture marker. Do not rename a working computer to bypass the guard.

## Prepare the lab

Use a disposable virtual disk containing an inactive Windows installation that has already booted independently. Add a test account and document, alternate data stream, ownership/permissions, registry marker, binary hash fixture, and installed application. Shut Windows down cleanly.

Attach the disk to the lab with identifiers distinct from the lab host. It must not be attached to another running VM. Windows-aware cloning also needs a third local scratch disk with enough free space.

## Run a clone

Publish the test project as self-contained for the lab's architecture.

- `--inventory <private-output.json>` records disk identities.
- `--clone <private-job.json> <private-activity.json>` runs the clone.

The job requires `Source`, `Target`, `SourceIdentity`, `TargetIdentity`, `FixtureId`, `Mode` (`WindowsUefi` or `ByteExact`), `Workspace`, and `ExpectedDiskBytes`. The expected size must match both disks and be between 64 and 512 GiB. The source must contain `SoopaQualification/fixture-id.txt` with the supplied marker. Byte-exact mode queues the source/destination offline steps before cloning.

## Check the destination

Shut down the lab, detach the destination, and boot it in a separate VM with only that disk attached. Check Windows startup, system drive mapping, fixture hashes, account SID, ownership/permissions, alternate stream, registry marker, and application launch. Repeat after a clean shutdown.

Record the recovery-image hash and registration separately from a Windows RE boot test. Windows-aware mode should create new GPT identifiers; byte-exact mode should preserve them.

## Record results

Keep images, credentials, inventories, user SIDs, private paths, registry hives, and raw traces private. Public results should contain only sanitized timestamps, code hashes, disk geometry, fixture hashes, and test outcomes.

Component checks and hypervisor disk copies do not establish that the production clone boots. An ARM64 VM boot does not establish x64 boot, physical-device compatibility, encrypted-source support, or migration to different hardware.
