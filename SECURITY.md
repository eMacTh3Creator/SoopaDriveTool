# Security and disk safety

Report vulnerabilities through GitHub private vulnerability reporting when available. Do not publish disk serials, recovery keys, personal filenames, image files or backup credentials in public reports.

SoopaDriveTool starts unelevated. Disk writes require administrator access and review/apply; direct restore/erase workflows require exact target confirmation. Pending chains survive administrator restart and require review again. Fixed providers receive typed data. There is no arbitrary elevated script endpoint or automatic update execution.

The engine re-enumerates identity/layout before operations. Retained raw handles bind the device number, length and native descriptor; native volume extents independently block writes to running Windows. BitLocker/unknown encryption and protected boot/recovery partitions block ordinary partition changes. WinPE recovery writes require an explicit task and target. Source/output separation, bounds, overlap and metadata checks apply to the supported modes.

Protected repositories use bounded Brotli packets, AES-256-GCM authentication, random nonces, PBKDF2-SHA-256 wrapping and independent random repository keys. Manifests and object references are authenticated. Restores validate paths and lengths, reject unsafe links and authenticate/hash resources. Repositories are locked during operations. Retention deletes only unreferenced data after accounting for retained points. There is no recovery backdoor.

Bare-metal Windows restoration stages decrypted WIM resources in `restore-<snapshot ID>` inside the repository. These temporary archives are plaintext. Failure retains them for retry; each retained archive is fully hashed against the authenticated manifest before destination writes. Success removes the staging folder. Protect the repository device and remove retained plaintext archives when they are no longer needed; ordinary deletion is not secure erasure.

Windows DPAPI protects schedule secrets. SYSTEM jobs require an installed executable in Program Files and restricted administrator/SYSTEM job folders. Their parent and job directory are created atomically with administrator ownership; preexisting untrusted owners or access rules are rejected. Job ownership and access are checked before SYSTEM job data is read. Network access uses Windows credentials, not a custom credential service. Windows migration stages a constrained one-time service bound to the restored Windows partition to enable WinRE; it removes its service after successful setup. Its result is logged locally. No persistent background service is required.

Disk operations are not transactions and do not promise rollback. Move journals and original table/sector backups improve recovery but cannot cover every power loss, device failure or malicious local administrator. Keep independently verified backups. Controller erase completion is not certified purge, and overwrite cannot establish coverage of SSD remapped cells.

This unsigned preview has extensive disposable-VM checks but is not a formally audited or hardware-certified release. Physical SATA/NVMe/USB bridges, 4Kn, disconnect/power-loss behavior, x64 recovery boots, signing and complete assistive-technology coverage need additional qualification. No signing identity or automatic update mechanism is supplied.
