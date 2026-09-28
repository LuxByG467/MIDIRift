# Modular Architecture — Stage A

Baseline: 0.12.4D6.3-alpha.

Stage A introduces only the module infrastructure. It deliberately does not migrate
audio engines, playback, visualizers, themes, navigation, library, or platform code.

## New contracts
- `IMidiRiftModule`
- `IModuleContext`
- `IModuleRegistry`
- `IModuleManager`

## Supporting types
- `ModuleDescriptor`
- `ModuleType`
- `ModuleContext`
- `ModuleRegistry`
- `ModuleManager`

## Invariants
1. Existing MIDIRift behavior is unchanged.
2. No external DLL/module loading exists.
3. No permissions, packages, repository, P2P, or sandbox exists yet.
4. No existing subsystem is forced through the module layer in Stage A.
5. Registration is explicit and compile-time.
6. Modules initialize in registration order and dispose in reverse order.
7. Duplicate module IDs are rejected.
8. Modules obtain capabilities through contracts, not MainPage/AppShell.

The infrastructure is intentionally dormant until later stages register real internal
modules. Stage A is a low-risk compile checkpoint.
