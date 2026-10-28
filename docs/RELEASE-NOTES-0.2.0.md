# SoopaDriveTool 0.2.0 — drive workflows preview

Adds the requested cloning choices, quick/thorough wiping, dependent Apply chains, live activity and full available SMART reports.

- **Clone modes:** byte-exact whole disk; filesystem-aware used-file NTFS volume; Windows-aware UEFI cloning of an attached inactive Windows installation. WIM modes use third-disk scratch storage, preserve source read-only state and validate resources during capture/apply. Windows mode configures destination EFI/BCD; independent boot/hardware qualification remains outstanding.
- **Wiping:** quick partition reset; full zero overwrite; cryptographic-random overwrite; three-pass random/ones/zeros. Thorough modes verify full read-back after every pass. Quick reset leaves file contents recoverable. These are not firmware sanitize or SSD remapped-cell erasure guarantees.
- **Action chains:** preview and Apply dependent delete → delete → create → rename steps on the same disk. Newly created partitions can be referenced by later steps. Identity/layout and every operation’s result are checked; unexpected changes stop later steps.
- **Activity:** current/completed/upcoming/skipped steps, real percentages where exposed, indeterminate Windows phases where no percentage exists, byte throughput/ETA for raw I/O, elapsed time, event journals, exports and rotating facts/messages.
- **SMART:** all available ATA/NVMe/SCSI attributes, counters and extended logs; preserved original JSON/text; health-bitfield handling; serial-identity warnings; JSON/CSV/text/printable HTML exports. Unavailable or partial data is explicit and never called healthy by default.

The application and DiskSpd are native ARM64/x64. The separate GPL smartctl collector is x64 and uses Windows emulation on ARM; its license and complete corresponding source archive are included. No smartd service is installed.

**Unsigned development preview.** The current system/boot disk stays protected. Live OS migration, independent Windows-clone boot qualification, physical controller/SMART coverage, firmware sanitize, and power-loss/hot-unplug qualification remain outstanding. Applied action chains cannot automatically roll back. Keep independent verified backups and evaluate destructive actions on disposable data disks.

[Detailed workflows and research](https://github.com/eMacTh3Creator/SoopaDriveTool/blob/main/docs/DRIVE-WORKFLOWS-0.2.md) · [Validation](https://github.com/eMacTh3Creator/SoopaDriveTool/blob/main/docs/VALIDATION-0.2.md)
