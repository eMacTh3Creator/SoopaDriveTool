# Security

## Report a vulnerability

Use GitHub's private vulnerability reporting for this repository when available. If it is unavailable, open an issue asking for a private contact without including the vulnerability details.

Do not include drive serial numbers, recovery keys, personal filenames, disk images, or backup credentials in a public report.

## Administrator access and disk writes

The application starts without administrator privileges. Disk changes require elevation and review before Apply. Restore and erase operations also require confirmation of the destination. A pending queue is retained when the app restarts as administrator and must be reviewed again.

Elevated operations accept typed requests through fixed providers. There is no endpoint for arbitrary elevated scripts, and the app does not download and run updates automatically.

Before an operation, the engine checks the disk identity and layout again. Raw handles are checked against the device number, size, and native descriptor. Volume extents provide a separate check against writing to the running Windows disk. Ordinary partition operations reject protected boot/recovery partitions, BitLocker, and unknown encryption states. Recovery writes in WinPE require an explicit operation and target.

Supported copy and recovery modes check source/destination separation, byte bounds, overlap, and metadata before writing.

## Encrypted backups

Repositories use bounded Brotli packets and AES-256-GCM authentication with random nonces. PBKDF2-SHA-256 protects independent random repository keys. Manifests and object references are authenticated.

Restore checks paths, lengths, links, authentication, and resource hashes. A repository remains locked during an operation. Retention removes only objects that are no longer referenced by the retained restore points. Lost passphrases cannot be recovered through the application.

## Temporary Windows image files

Full Windows restoration decrypts WIM resources into `restore-<snapshot ID>` inside the repository. **These temporary files are not encrypted**, even when the repository is encrypted.

A failed restore retains them for another attempt. Before writing to the destination, the next attempt hashes every retained archive against the authenticated manifest. A successful restore removes the staging folder.

Protect the repository device and remove retained temporary files when they are no longer needed. Deleting a file does not securely erase its contents.

## Scheduled jobs

Windows DPAPI protects schedule secrets. SYSTEM jobs require an installed executable in Program Files and restricted job storage. The parent and job directories are created atomically with administrator ownership. Existing directories with untrusted owners or permissions are rejected. Ownership and permissions are checked before the SYSTEM task reads a job.

Network access uses Windows credentials. No separate credential service is installed.

Windows migration may install a constrained, temporary service to enable Windows RE on the destination's first startup. It is bound to the restored partition, records its result locally, and removes its service after success. The application does not require a persistent background service.

## Limits

Disk operations cannot be rolled back as a transaction. Move journals and saved table/sector data help with recovery, but they cannot cover every power loss, hardware failure, or action by a malicious local administrator. Keep a separate, verified backup.

Overwriting a disk does not prove that remapped SSD cells were erased. A controller's successful erase response is not a certified purge.

The current release is unsigned and has not had a formal security audit. Tests use disposable VMs and disks. Physical controllers, USB bridges, 4Kn disks, power interruption, x64 recovery boots, and complete accessibility support still need testing. See the [validation report](docs/VALIDATION-0.3.0.md).
