# SoopaDriveTool 0.2.1

Version 0.2.1 improved Windows recovery-image discovery and clone progress reporting. These notes describe that patch; see the [current guide](USER-GUIDE.md) for later features.

## Changed

- Find WinRE on the selected source's registered or hidden recovery partition, including installations without `Windows/System32/Recovery/Winre.wim`.
- Size the recovery partition for the image, apply its GPT attributes, verify the copied image, and remove temporary EFI/recovery drive letters.
- Capture with native wimlib, preserve permissions strictly, and verify the entire archive before Microsoft DISM applies it.
- Report file counts during scanning and measured progress during capture, verification, and apply. Initial scanning uses an indeterminate progress bar.
- Replace the installed release guide during an upgrade. Packages include engine licenses and corresponding source archives.

## Testing

Windows-aware and byte-exact clones of a complete Windows 11 ARM64 installation each passed two independent Secure Boot cold boots with only the destination attached. Checks covered password sign-in, profile files, NTFS permissions and named streams, registry data, application launch, and the recovery-image hash. The byte-exact copy included all 256 GiB and passed complete SHA-256 read-back verification.

The local ARM64 integration suite passed 123 checks. Native ARM64 CI passed 75 checks, and native x64 CI passed 123. Both installers passed install, render, and uninstall checks. The final ARM64 installer also upgraded the booted clone from 0.2.0; installed files matched the portable package.

Packages came from [CI run 40810726511](https://github.com/eMacTh3Creator/SoopaDriveTool/actions/runs/40810726511), compiled from `105dc5e6af742803f77ddc774728225b04862a0d`. Later tag changes contained documentation and site records only. Full Windows tests used .NET 10.0.12 with the same engine sources; release packages included .NET 10.0.14 and passed packaged-runtime checks.

## Limits in this release

The installers were unsigned. Full Windows boot testing covered ARM64 on Parallels UEFI. x64 Windows boot, physical hardware, live Windows cloning, encrypted sources, activation/TPM binding, and different-hardware migration were outside that test scope. A copied WinRE image did not establish a separate recovery boot. Upstream marked wimlib's ARM64 build experimental.

[0.2.1 test results](VALIDATION-0.2.1.md) · [Clone and boot record](CLONE-BOOT-CHECK-0.2.1.json)
