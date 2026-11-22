# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- A GNOME logout or shutdown no longer hangs: the session watcher answers the
  `QueryEndSession` request on the client object, closes on `EndSession` and
  `Stop`, and the process exits even when a disposal wedges.
- A second launch can no longer re-show a closed window and leave an
  unreachable process behind.
- Opening a new profile while a connected one was selected shows "New tunnel"
  and no Connect button instead of the previous profile and "Disconnect".
- The edit pane no longer repeats the bandwidth the sidebar already shows.

### Changed

- One button language: normal, accent (Save, Connect) and destructive (Delete,
  Remove peer) share the same box, border and disabled state.
- The DNS policy offers Profile, System and Split; the full-tunnel mode was the
  profile mode with an explicit root routing domain and is retired (a stored
  value reads as Profile). Split now routes only the listed domains to the
  profile DNS, and the search-domains field is enabled only there.
- The application menu keeps the file actions only (paste, import, export,
  backup, restore); New, Delete and Refresh live in the sidebar and the pane.
- Network loss and automatic reconnection are desktop notifications, not
  toasts; success toasts no longer print a filesystem path.

## [0.1.0] - 2026-11-21

The first release of VPN Manager: a per-user WireGuard client for Debian GNOME,
written as `VpnManager.*` on Core v0.2.3.

### Added

- A master-detail surface: a persistent profile sidebar and one editable pane,
  replacing the pushed detail/editor/rename/policy pages and the icon toolbar.
  The configuration form saves explicitly and shows a dirty dot in the header
  (Core 0.2.3), the display name commits inline, and the policy applies on
  change.
- The `CoreHost` composition: XDG paths, durable application log, operation
  journal, crash log, single-instance lock and the desktop notifier.
- The shared `Core.UI` shell: `CoreWindow` stack navigation, header commands,
  dialogs, toasts, banners, tray, preferences and the About dialog.
- `--self-test` on the published Native AOT binary, and a `--diagnostics` path
  that runs without a display.
- A `tools/scripts/binaries/gate.scs` gate, a `.slnx` solution and Xunit v3 test
  projects.
- Per-profile NetworkManager policy: automatic reconnection, and the `Profile`,
  `System`, `Split` and `FullTunnel` DNS modes applied through `nmcli`.
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
  dialog. GNOME vanilla has no StatusNotifier host, so the tray code is gone
  from the product and the package no longer depends on an extension.
- A **Policy** page edits reconnection, the DNS mode and the routing domains for
  a profile; a live connection is updated without reconnecting.
- The display name is the NetworkManager connection id: a rename renames the
  connection too, while the file, the interface and the real tunnel name stay
  unchanged.
- The window identity on Wayland is set explicitly to `vpn-manager`, the
  desktop entry's base name, so the GNOME dash and the Alt+Tab switcher show
  the product name and icon instead of "Unknown". This needs Avalonia 12.1.6.
- The window remembers its size and position (`window.json` under the state
  directory) and the first launch clamps the default size to the work area.
- The empty profile list offers an "Import a profile…" action.
- List rows carry an automation name and editor fields are labelled for
  assistive technology; keyboard focus is drawn as a ring.

### Changed

- Namespaces and assemblies are `VpnManager.Core` and `VpnManager.App`; the
  product identity is `VPN Manager`, the desktop id `vpn-manager`, the XDG
  namespace `benmoussa/vpn-manager`.
- Core is consumed at the pinned tag `v0.2.3` as a sibling source clone.
- Process execution uses `Core.Execution.IProcessRunner`: argument vectors, fixed
  trusted directories, capped output and a timeout.
- The single-instance claim is `<vendor>-<application>.socket` under the
  per-user XDG runtime directory; the account suffix is no longer repeated.
- Profiles are stored through the platform atomic writer, owner-only, and the
  state and preferences through `JsonSettingsStore<T>`.
- The appearance preferences use `Core.Mvp.Services.AppearanceService`; the old
  product-local preferences dialog is gone.

### Fixed

- Renaming a tunnel refreshes the open detail page's header immediately; a list
  refresh also no longer overwrites the header and toolbar of a pushed page.
- Closing with the VPN up shows one Cancel button instead of a duplicate: the
  dialog's built-in Cancel is the only one.
- A logout or a shutdown no longer shows the disconnect dialog. The app watches
  the GNOME session manager (QueryEndSession/EndSession) and exits quietly;
  SIGTERM and SIGINT take the same path, so a session that is ending is never
  blocked by a question.
- Connecting no longer fails after the tunnel is already up: the connect path
  applies the policy to the profile and lets activation apply it, and only an
  explicit policy change on a live connection reapplies the device. The laptop
  journal showed NetworkManager refusing the reapply with "Can't reapply
  changes to 'connection.permissions' setting" while the VPN was active.
- Closing the window with the quit behaviour now ends the process. It used to
  close the window and leave an unreachable instance behind, and a later launch
  crashed with "Cannot re-show a closed window".
- A failure shown as a toast is also written to the application log, so it
  stays readable after the toast disappears.
- `--disconnect` on a tunnel that is already down is a success, not an
  `nmcli` error: the operator asked for a state, not for a transition.
- A failed NetworkManager listing is no longer reported as "already
  disconnected": the failure reaches the caller.
- Deleting a profile only deletes the NetworkManager connection whose id
  represents it, never a connection matched by interface alone.
- A refused NetworkManager rename rolls the stored display name back, so the
  application and NetworkManager never keep different names after a failure.
- The session-end watcher registers with gnome-session and answers its
  `QueryEndSession`, as well as observing the broadcast signals.
- The three-way quit choice is gone with the tray: closing and quitting share
  the disconnect confirmation, and the dead command surface is removed.
- The `Full tunnel` DNS mode sets the root routing domain (`~.`) explicitly
  instead of relying on NetworkManager adding it because of the default route.
- A read-only session shows a persistent warning banner as well as the header
  subtitle, so the mode is visible before an action is refused.
- Changing a profile's DNS policy while its connection is active now reapplies
  the device (`nmcli device reapply`): the new search domains and containment
  hold immediately instead of at the next activation.
- `--dns-search ""` clears the stored search domains; an empty value no longer
  silently kept the previous list.
- A D-Bus call whose body does not match the method signature now receives a
  typed `com.bit_crafts.VpnManager1.Failed` reply; it used to raise inside the
  handler and disconnect the session service.
- A second `--dbus` instance no longer runs without owning the well-known name:
  it checks the request result and exits with a message instead of idling.
- A pseudo-file read under `/proc` or `/sys` is capped like a profile read, so
  a stream that never ends cannot exhaust memory.
- An import path that starts with `-` is refused before it reaches `nmcli`,
  where it would be read as an option.
- A launch without `XDG_RUNTIME_DIR` uses an owner-only directory under the
  state home instead of the sticky `/tmp` fallback, where another local account
  could pre-create the single-instance socket name.
- The `--dbus` service now exits when the session bus ends; it used to keep
  running without a bus after a logout.

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
