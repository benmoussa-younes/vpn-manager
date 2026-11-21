# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-11-21

The first release of VPN Manager: a per-user WireGuard client for Debian GNOME,
written as `VpnManager.*` on Core v0.2.1.

### Added

- The `CoreHost` composition: XDG paths, durable application log, operation
  journal, crash log, single-instance lock and the desktop notifier.
- The shared `Core.UI` shell: `CoreWindow` stack navigation, header commands,
  dialogs, toasts, banners, tray, preferences and the About dialog.
- `--self-test` on the published Native AOT binary, and a `--diagnostics` path
  that runs without a display.
- A `tools/scripts/binaries/gate.scs` gate, a `.slnx` solution and Xunit v3 test
  projects.

### Changed

- Namespaces and assemblies are `VpnManager.Core` and `VpnManager.App`; the
  product identity is `VPN Manager`, the desktop id `vpn-manager`, the XDG
  namespace `vpnmanager/vpn-manager`.
- Core is consumed at the pinned tag `v0.2.1` as a sibling source clone.
- Process execution uses `Core.Execution.IProcessRunner`: argument vectors, fixed
  trusted directories, capped output and a timeout.
- Profiles are stored through the platform atomic writer, owner-only, and the
  state and preferences through `JsonSettingsStore<T>`.
- The appearance preferences use `Core.Mvp.Services.AppearanceService`; the old
  product-local preferences dialog is gone.

### Security

- A stored profile is re-parsed and re-validated before it is handed to
  NetworkManager; a parse failure refuses the connection instead of passing the
  raw text.
- Hook keys (`PreUp`, `PostUp`, `PreDown`, `PostDown`) are stripped from the
  copy handed to NetworkManager: NetworkManager does not run them, and the
  product never passes a command it cannot honour. The stored profile keeps the
  original hook lines for reference.
- Profile reads are bounded and no-follow; a symbolic link or an oversized file
  is refused. The import temporary directory is owner-only and lives under the
  application state directory.
- The Debian post-install applies only the package's own sysctl drop-in, not
  every drop-in on the machine.
- `network-manager` and `wireguard-tools` are dependencies of the package, not
  recommendations.

### Removed

- The vendored shared-library source, the root helper, the system scope, the
  `wg-quick` activation path and the product-local About/Preferences/tray
  implementations.
