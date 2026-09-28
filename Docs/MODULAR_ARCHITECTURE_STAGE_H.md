# Modular Architecture — Stage H

Cumulative checkpoint: A + B + C + D + E + F + G + H.

Stage H establishes the security/package/repository boundary. It intentionally does
NOT enable arbitrary third-party CLR code.

## Added
- `IModulePermissionService`
- `IModulePackageService`
- `IModuleRepository`
- `IModuleSandbox`
- manifest/package/repository/trust models

## Security model

A package is data, not an installer. `ModulePackageService` opens a ZIP, requires a
root `manifest.json`, validates stable IDs, computes effective permissions, checks an
optional SHA-256 declaration and detects executable payloads.

Stage H's sandbox implementation is `DenyExecutableModuleSandbox`. Therefore DLL,
EXE, SO, dylib, JAR and DEX payloads are detectable but are not considered runnable.
This is deliberate: an in-process CLR plugin cannot be made safe merely by placing
permission names in a manifest.

Permissions become enforceable only when the host mediates the capability. The
permission service therefore defines policy now without pretending it can sandbox
direct file/network calls made by arbitrary loaded code.

## Repository / P2P boundary

`IModuleRepository` models a verified index entry containing package ID, version,
download URI, SHA-256, signature metadata, source and trust level.

Transport is not trust authority. A future GitHub mirror, HTTP server or P2P peer may
supply package bytes; installation must still validate those bytes against trusted
index metadata before use.

## Deliberately NOT implemented
- external assembly loading
- reflection-based plugin discovery
- automatic installation
- network downloading
- P2P transport
- signature cryptographic verification / author keyring
- OS/process/WASM sandbox execution
- permission approval UI
- persistent permission grants

Those require separate threat-modelled work. Stage H gives them stable contracts
without weakening the current application.

## Package rule

Executable third-party packages fail the declarative-install safety condition.
Declarative themes/presets/resources can be added later through host-owned parsers
without granting arbitrary code execution.

No playback, synthesis, DSP, UI rendering, queue or EOF behavior changes in H.
