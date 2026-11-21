# VPN Manager

A per-user WireGuard client for Debian GNOME. It imports a profile, connects and
disconnects through NetworkManager, shows the live state of the tunnel, and never
prints a private key. The GUI never elevates; the one explicit `--fix-sysctl`
action goes through the fixed polkit helper.

- **Website**: <https://github.com/benmoussa-younes/vpn-manager>
- **Licence**: `GPL-3.0-or-later` (see [`LICENSES/GPL-3.0-or-later.txt`](LICENSES/GPL-3.0-or-later.txt))

## What it does

- Lists the profiles under `~/.config/benmoussa/vpn-manager` (or the
  `--config-dir` value) and the WireGuard connections NetworkManager owns.
- Imports a `.conf` file, a pasted profile, or a full configuration text; exports
  a profile. A profile is written owner-only and parsed before it is stored.
- Connects and disconnects through `nmcli`, as the operator, one tunnel at a time.
- Shows the kernel transfer counters, the tunnel state, the IPv6 classification
  and the DNS containment of the selected tunnel.
- Offers the same connect/disconnect actions from the tray and a GNOME
  notification on every state change.
- Keeps one instance per user and surfaces the running window on a second launch.
- Supports a `--read-only` session that browses and exports without changing
  anything.
- Applies a per-profile NetworkManager policy: automatic reconnection, and the
  `Profile`, `System`, `Split` or `FullTunnel` DNS mode.
- Offers a headless CLI: `--status [--json]`, `--connect`, `--disconnect`,
  `--policy` and `--fix-sysctl`.
- Exposes the same operations on the user session bus as
  `com.bit-crafts.VpnManager` (interface `com.bit_crafts.VpnManager1`) for
  enterprise orchestration.

## What it is not

- Not a server, hub or peer-provisioning tool.
- Not a multi-protocol client: it manages WireGuard profiles only.
- Not a kill switch: it reports DNS containment, it never claims a leak is
  impossible.
- Not a general privilege tool: the GUI runs unprivileged and never invokes
  `sudo` or an arbitrary root command. One fixed full-tunnel sysctl action goes
  through the polkit-authorized helper; it never reads or writes
  `/etc/wireguard`.

## Command line

```
vpn-manager                      start the GUI
vpn-manager --config-dir <path>  manage a specific directory
vpn-manager --read-only          browse and export, change nothing
vpn-manager --diagnostics        report what is readable without a display
vpn-manager --status [--json]    print the profiles and their policy
vpn-manager --connect <profile>  bring a profile up through NetworkManager
vpn-manager --disconnect <profile>  take a profile down
vpn-manager --policy <profile> [--auto-reconnect on|off] \
  [--dns-mode profile|system|split|full] [--dns-search a,b|'']
vpn-manager --fix-sysctl       set the full-tunnel sysctl through polkit
vpn-manager --self-test          run the display-free self-test
vpn-manager --version            print the version
vpn-manager --help               print the usage text
```

A manual `--disconnect` is authoritative: the profile is marked
`autoconnect no` until the next `--connect`, so NetworkManager does not bring it
back behind the operator's back.

## Requirements

- Debian 13 (trixie) amd64 with GNOME, on Wayland or X11.
- **NetworkManager** (`nmcli`) to connect and disconnect.
- **wireguard-tools** (`wg`) for key generation and runtime inspection.
- **libnotify-bin** (`notify-send`) for desktop notifications (recommended).
- The GNOME **AppIndicator** extension for the tray icon.

## Install

The release publishes a Debian package, the matching source archive and their
checksums. Install the package as the operator:

```bash
sudo apt-get install -y ./vpn-manager_<version>-1_amd64.deb
vpn-manager
```

The package installs `/usr/lib/vpn-manager`, the `/usr/bin/vpn-manager` entry, the
desktop file and the icons. It also ships
`/usr/lib/sysctl.d/60-vpn-manager.conf`
(`net.ipv4.conf.all.src_valid_mark=1`), which a full tunnel through
NetworkManager needs on a stock Debian; the package applies only that file. The
GUI never elevates; `--fix-sysctl` is the one explicit helper action.

## Where the data lives

| Path | Contents |
|---|---|
| `~/.config/benmoussa/vpn-manager/` | The profiles the operator owns (owner-only, `0600`). |
| `~/.local/state/benmoussa/vpn-manager/` | State, appearance preferences, application log, operation journal and crash log. |
| `$XDG_RUNTIME_DIR/benmoussa-vpn-manager.socket` | The single-instance claim; the runtime directory is already per-user. |

A private key lives in the profile file (owner-only) and in memory while an
import or an export runs. It is never displayed, logged or passed as a
command-line argument.

### Coming from the previous WireGuard manager

The previous application stored its profiles in its own configuration
directory. Point the new client at that directory to keep managing them in
place:

```bash
vpn-manager --config-dir <the-previous-configuration-directory>
```

The display aliases and the other application state are not migrated; they live
in the previous state directory and are rebuilt as the client runs.

## Build from source

The product consumes **Core v0.2.2** as source: core is not a NuGet package, and
the tag is pinned in [`CORE_VERSION`](CORE_VERSION). Clone it beside this
repository:

```bash
git clone --branch v0.2.2 --depth 1 https://github.com/benmoussa-younes/core.git ../core
git -C ../core describe --tags --exact-match   # must print v0.2.2
```

Then build, test and run the gate:

```bash
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1   # Debian ships no ICU

dotnet restore VpnManager.slnx
dotnet build   VpnManager.slnx -c Release
dotnet test    VpnManager.slnx -c Release --no-build

./tools/scripts/binaries/gate.scs                # one VERDICT line, exit 0/1
```

The gate needs the Scripts interpreter on `PATH` (`scripts --version`), Git and
`reuse` (`reuse lint`). The `.scs` syntax check is a CI step, not a verdict:

```bash
scripts check tools/scripts/binaries/gate.scs
```

Publish the only shipping form, Native AOT:

```bash
dotnet publish src/VpnManager.App/VpnManager.App.csproj \
  -c Release -r linux-x64 -o publish
./publish/VpnManager.App --self-test
```

Count the `ILC: ` lines of the publish: zero is the proof. `--self-test` runs
the display-free claims on the published native binary; `dotnet test` runs the
Junit-free Xunit suite in JIT.

## Coverage

```bash
dotnet test VpnManager.slnx -c Release \
  --collect:"XPlat Code Coverage" --results-directory artifacts/coverage
```

The report covers the hand-written `VpnManager.Core`, `vpn-manager` and
`vpn-manager-helper` assemblies. Core is above 95 % line coverage; the Avalonia
shell is the residual and is tracked in the workstream.

## Package the Debian package

```bash
packaging/build-deb.sh
```

It stages the Native AOT publish, the desktop entry, the icons, the manual page
and the sysctl drop-in, then builds `artifacts/vpn-manager_<version>-1_amd64.deb`.

## Security model

- No secret is ever printed: a private key is never displayed, logged or passed
  as a command-line argument.
- No general elevation: the GUI runs as the operator. The only privileged path
  is the fixed `--fix-sysctl` action, launched by `pkexec` and authorized by the
  polkit action `com.bit-crafts.vpn-manager.sysctl-src-valid-mark`; the helper
  accepts one verb and no caller path or command.
- No system write to `/etc/wireguard`. The package installs the sysctl drop-in;
  the explicit `--fix-sysctl` action sets and verifies that one sysctl.
- Every external command is an argument vector, never a shell string; a bare
  tool name resolves against fixed system directories, never `PATH`.
- Profiles are read with a bounded, no-follow read and written atomically
  owner-only. A stored profile is re-validated before it is handed to
  NetworkManager; a `PreUp`/`PostUp`/`PreDown`/`PostDown` hook is stripped from
  the imported copy because NetworkManager does not execute it, while the stored
  profile keeps the original lines.
- DNS containment is reported as `Contained`/`Split`/`Uncontained`/`Unknown`;
  no leak is claimed impossible. The DNS mode is applied through NetworkManager
  (`ipv4.dns`, `ipv6.dns`, `dns-search`, `dns-priority`,
  `ignore-auto-dns`), never by editing `/etc/resolv.conf`. A policy change on an
  active connection is reapplied with `nmcli device reapply`, so it holds at
  once; `--dns-search ""` clears the stored search domains.
- A user-scope session configures a profile but cannot bring it up without the
  NetworkManager policy that every GNOME session already has; the interface says
  so instead of failing silently.

See [`SECURITY.md`](SECURITY.md) for the private vulnerability report.

## Licence

`GPL-3.0-or-later`. Copyright (C) 2026 Younes BENMOUSSA
<younes.benmoussa@pm.me>. The documentation is `CC-BY-SA-4.0`; the third-party
components are listed in [`THIRD-PARTY`](THIRD-PARTY).
