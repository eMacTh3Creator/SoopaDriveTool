# Security and disk safety

Do not include disk serials, recovery keys, personal filenames or private image files in public reports. Use GitHub private vulnerability reporting for security issues when available; otherwise contact the repository owner before publishing exploit details.

The app starts unelevated. Disk mutation requires explicit administrator restart and a review/apply flow. System/boot disks and protected partitions are blocked; BitLocker/unknown encryption blocks partition changes. Disk identity and layout are revalidated. Fixed embedded PowerShell scripts accept typed JSON data; no arbitrary elevated command API exists.

The 0.1 preview is not a security-audited production tool. Raw writes can leave incomplete targets if interrupted. There is no guaranteed rollback. Backups are not encrypted. Verify and retain independent copies before changes. SSD zero-overwrite is not a sanitize guarantee.

Known hardening work: bind identity to open device handles, protect against local filesystem races, lock snapshot repositories, fuzz binary parsers, qualify removable-device failures, sign releases, and implement authenticated update verification before automatic updates are added.
