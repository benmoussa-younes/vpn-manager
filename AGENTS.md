# AGENTS.md — VPN Manager

A per-user WireGuard client for Debian GNOME: it lists profiles, imports and
exports them, and connects and disconnects through NetworkManager. The product
never elevates and never prints a private key. This repository ships one product,
not a library and not a tool.

## Build and test

```sh
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1   # Debian ships no ICU

dotnet build VpnManager.slnx -c Release
dotnet test  VpnManager.slnx -c Release --no-build
scripts check tools/scripts/binaries/gate.scs
scripts run tools/scripts/binaries/gate.scs        # one VERDICT line, exit 0/1
```

The gate checks the tree, the licence metadata, the pinned core tag, the version,
the build and the tests. It needs Git, `reuse` and the Scripts interpreter on
`PATH`; it writes nothing tracked.

## Core

Core is source-only and pinned by [`CORE_VERSION`](CORE_VERSION) (`v0.2.2`). It is
a sibling clone, never a NuGet package and never vendored:

```sh
git clone --branch v0.2.2 --depth 1 https://github.com/benmoussa-younes/core.git ../core
```

The projects reference `../../../core/src/...`; the pin and the references change
in the same commit as the API adaptation.

## Conventions

- Two assemblies: `VpnManager.Core` is headless (no Avalonia, no display, no
  Linux-only type in its public API); `VpnManager.App` is the Avalonia shell.
- MVP: a passive view publishes a `ShellSnapshot`; commands travel back as ids.
  No business rule in code-behind, no DI container, no service locator.
- `Result<T>`/`CoreError` for an expected failure; an exception is a bug.
- Native AOT only: `PublishAot` on the executable, `IsAotCompatible` and the trim
  analyzer on the library. No reflection, no dynamic code, no runtime codegen.
- Every package version is central in `Directory.Packages.props`. Only nuget.org.
- The product names Core, Avalonia and the reference implementation in `NOTICE`
  and `THIRD-PARTY`.

## Security

- No secret in a tracked file, a command line, a log or a process argument.
- `nmcli`, `wg` and `notify-send` are started with an argument vector through
  `IProcessRunner`; never a shell string, never `PATH`.
- The profile store is owner-only (`0600`/`0700`), read no-follow and bounded,
  written atomically. A stored profile is validated before NetworkManager sees it.
- The application never invokes `sudo`, `pkexec` or a root helper and never reads
  or writes `/etc/wireguard`. Installing the `.deb` is the operator's decision.
- A vulnerability report is private: follow `SECURITY.md`.

## Repository facts

- Product version `0.1.0`; licence `GPL-3.0-or-later`; documentation
  `CC-BY-SA-4.0`.
- Core pin `v0.2.2`; the desktop id is `vpn-manager`; the XDG namespace is
  `benmoussa/vpn-manager`.
- A release is a signed tag `v<version>` and the `.deb`, the source archive and
  `SHA256SUMS`.

## Commits and releases

- One branch per task, `work/<slug>`; one concern per commit; English, imperative subject; the body says why.
- A change lands on `main` only when the gate prints `VERDICT: PASS`; the owner
  reviews the pull request and orders the release.

## Limits

- WireGuard only; no server, no multi-protocol, no kill switch.
- NetworkManager is the connection engine; without it the product reports the
  limit instead of writing a system unit.
- X11 is reachable through XWayland; the native Wayland backend is selected when
  the session offers it, with an X11 fallback.
