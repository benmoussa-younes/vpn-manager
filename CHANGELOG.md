# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- A root `LICENSE` carrying the GNU GPL version 3 text, so a code host reports
  `GPL-3.0`; `LICENSES/` keeps the SPDX texts.

### Changed

- The gate refuses a tracked build artifact (`artifacts/`, `*.deb`), a root
  `LICENSE` that diverges from `LICENSES/GPL-3.0-or-later.txt`, and a `LICENSE`
  that is not a regular file.

## [0.1.0] - 2026-11-22

The first release of VPN Manager: a per-user WireGuard client for Debian GNOME,
written as `VpnManager.*` on Core v0.2.3.

### Added

- A master-detail surface: a persistent profile sidebar and one editable pane.
  The configuration form saves explicitly and shows a dirty dot in the header
  (Core 0.2.3), the display name commits inline, and the policy applies on
  change.
- The `CoreHost` composition: XDG paths, durable application log, operation
  journal, crash log, single-instance lock and the desktop notifier.
- The shared `Core.UI` shell: `CoreWindow` stack navigation, header commands,
  dialogs, toasts, banners, preferences and the About dialog.
- `--self-test` on the published Native AOT binary, and a `--diagnostics` path
  that runs without a display.
- A `tools/scripts/binaries/gate.scs` gate, a `.slnx` solution and Xunit v3 test
  projects.
- Per-profile NetworkManager policy: automatic reconnection, and the `Profile`,
  `System` and `Split` DNS modes applied through `nmcli`.
- A headless CLI: `--status [--json]`, `--connect`, `--disconnect` and
  `--policy --auto-reconnect/--dns-mode/--dns-search`.
- A fixed root helper and the polkit action
  `com.bit-crafts.vpn-manager.sysctl-src-valid-mark`; `--fix-sysctl` sets and
  verifies the full-tunnel sysctl through `pkexec`.
- An `nmcli monitor` event stream for immediate state refresh, with the periodic
  poll as fallback.
- A user-session D-Bus service, `com.bit-crafts.VpnManager`, with status,
  connect, disconnect and policy methods; the package ships its activation file.
- The product has no tray icon: closing the window quits, asking before
  disconnecting a connected profile, and a session end closes without that
  dialog.
- The policy card in the pane edits reconnection, the DNS mode and the routing
  domains for a profile; a live connection is updated without reconnecting.
- The display name is the NetworkManager connection id: a rename renames the
  connection too, while the file, the interface and the real tunnel name stay
  unchanged.
- The window identity on Wayland is set explicitly to `vpn-manager`, the
  desktop entry's base name, so the GNOME dash and the Alt+Tab switcher show
  the product name and icon instead of "Unknown".
- The window remembers its size and position (`window.json` under the state
  directory) and the first launch clamps the default size to the work area.
- The empty profile list offers an "Import a profile…" action.
- List rows carry an automation name and editor fields are labelled for
  assistive technology; keyboard focus is drawn as a ring.

### Changed

- Namespaces and assemblies are `VpnManager.Core`, `VpnManager.App` and
  `VpnManager.Helper`; the product identity is `VPN Manager`, the desktop id
  `vpn-manager`, the XDG namespace `benmoussa/vpn-manager`.
- Core is consumed at the pinned tag `v0.2.3` as a sibling source clone.
- Process execution uses the product `IProcessRunner` with argument vectors,
  fixed trusted directories, capped output and a timeout.
- The single-instance claim is `<vendor>-<application>.socket` under the
  per-user XDG runtime directory; the account suffix is no longer repeated.
- Profiles are stored through the platform atomic writer, owner-only, and the
  state and preferences through `JsonSettingsStore<T>`.
- The appearance preferences use `Core.Mvp.Services.AppearanceService`; the old
  product-local preferences dialog is gone.
- One button language: normal, accent (Save, Connect) and destructive (Delete,
  Remove peer) share the same box, border and disabled state.
- The application menu keeps the file actions only (paste, import, export,
  backup, restore); New, Delete and Refresh live in the sidebar and the pane.
- Network loss and automatic reconnection are desktop notifications, not
  toasts; success toasts no longer print a filesystem path. A connectivity
  change reported by NetworkManager while a tunnel is up also notifies.

### Fixed

- Selecting a profile no longer marks it dirty: Avalonia raises `TextChanged`
  through the dispatcher, after the presenter has loaded the fields, so the
  filled values looked like operator edits and switching profile asked to save.
  The editor compares a fingerprint of its values with the loaded one.
- A GNOME logout or shutdown no longer hangs: the session watcher answers
  `QueryEndSession` on the client object, closes on `EndSession` and `Stop`, and
  the process exits even when a disposal wedges.
- A second launch can no longer re-show a closed window and leave an
  unreachable process behind.
- Opening a new profile while a connected one was selected shows "New tunnel"
  and no Connect button instead of the previous profile and "Disconnect".
- The edit pane fills the window instead of shrinking to its cards, and no
  longer repeats the bandwidth the sidebar already shows.
- A destructive button is a strong red fill with a white label in both themes.
- A state file written before the full-tunnel DNS mode was retired still loads:
  the retired value reads as Profile instead of failing the document.
- A network that goes down is reported even when NetworkManager says `limited`
  or the daemon is asleep/disconnected, and every connectivity change is
  journalled with its value.
- `Split` DNS requires at least one routing domain; the policy is refused
  without one instead of sending every name to the profile DNS.
- A D-Bus operation that fails returns a typed
  `com.bit_crafts.VpnManager1.Failed` reply instead of a successful string.
- `--read-only --fix-sysctl` is refused like every other changing action.
- Disconnecting a name that never existed is a typed failure instead of a
  reported success.
- Connecting an already-connected profile is a no-op instead of deleting and
  re-importing the live connection.
- The editor preserves the stored `SaveConfig` line, like the hook keys.
- A failure shown as a toast is also written to the application log, so it
  stays readable after the toast disappears.
- A failed NetworkManager listing is no longer reported as "already
  disconnected": the failure reaches the caller.
- Deleting a profile only deletes the NetworkManager connection whose id
  represents it, never a connection matched by interface alone.
- A refused NetworkManager rename rolls the stored display name back, so the
  application and NetworkManager never keep different names after a failure.
- The `--dbus` service exits when the session bus ends, and on `SIGTERM`.
- A D-Bus call whose body does not match the method signature receives a typed
  error reply and leaves the service running.
- A second `--dbus` instance exits with a message instead of idling without the
  well-known name.
- An import path that starts with `-` is refused before it reaches `nmcli`,
  where it would be read as an option.
- A launch without `XDG_RUNTIME_DIR` uses an owner-only directory under the
  state home instead of the sticky `/tmp` fallback.
- A pseudo-file read under `/proc` or `/sys` is capped like a profile read.

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
- A manual disconnect marks the NetworkManager profile as `autoconnect no`
  until the next manual connect, so NM does not override the operator's intent.
- DNS is applied through NetworkManager; `/etc/resolv.conf` is never edited.
- The Debian post-install applies only the package's own sysctl drop-in, not
  every drop-in on the machine.
- `network-manager` and `wireguard-tools` are dependencies of the package, not
  recommendations.

### Removed

- The vendored shared-library source, the old privileged helper, the system
  scope, the `wg-quick` activation path and the product-local
  About/Preferences/tray implementations.
