# VPN Manager

A per-user WireGuard client for Debian GNOME. It imports a profile, connects and
disconnects through NetworkManager, shows the live state of the tunnel, and never
asks for a password, never elevates and never prints a private key.

- **Website**: <https://github.com/benmoussa-younes/vpn-manager>
- **Licence**: `GPL-3.0-or-later` (see [`LICENSES/GPL-3.0-or-later.txt`](LICENSES/GPL-3.0-or-later.txt))

## What it does

- Lists the profiles under `~/.config/vpnmanager/vpn-manager` (or the
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

## What it is not

- Not a server, hub or peer-provisioning tool.
- Not a multi-protocol client: it manages WireGuard profiles only.
- Not a kill switch: it reports DNS containment, it never claims a leak is
  impossible.
- Not a privilege tool: it never invokes `sudo`, `pkexec` or a root helper, and it
  never reads or writes `/etc/wireguard`.

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
NetworkManager needs on a stock Debian; the package applies only that file, and
the application itself never elevates.

## Where the data lives

| Path | Contents |
|---|---|
| `~/.config/vpnmanager/vpn-manager/` | The profiles the operator owns (owner-only, `0600`). |
| `~/.local/state/vpnmanager/vpn-manager/` | State, appearance preferences, application log, operation journal and crash log. |
| `$XDG_RUNTIME_DIR/vpnmanager-vpn-manager-<user>.sock` | The single-instance claim. |

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

The product consumes **Core v0.2.1** as source: core is not a NuGet package, and
the tag is pinned in [`CORE_VERSION`](CORE_VERSION). Clone it beside this
repository:

```bash
git clone --branch v0.2.1 --depth 1 https://github.com/benmoussa-younes/core.git ../core
git -C ../core describe --tags --exact-match   # must print v0.2.1
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

## Package the Debian package

```bash
packaging/build-deb.sh
```

It stages the Native AOT publish, the desktop entry, the icons, the manual page
and the sysctl drop-in, then builds `artifacts/vpn-manager_<version>-1_amd64.deb`.

## Security model

- No secret is ever printed: a private key is never displayed, logged or passed
  as a command-line argument.
- No elevation: the application runs as the operator and never invokes `sudo`,
  `pkexec` or a privileged helper.
- No system write: it never reads or writes `/etc/wireguard`.
- Every external command is an argument vector, never a shell string; a bare
  tool name resolves against fixed system directories, never `PATH`.
- Profiles are read with a bounded, no-follow read and written atomically
  owner-only. A stored profile is re-validated before it is handed to
  NetworkManager; a `PreUp`/`PostUp`/`PreDown`/`PostDown` hook is stripped from
  the imported copy because NetworkManager does not execute it, while the stored
  profile keeps the original lines.
- DNS containment is reported as `Contained`/`Split`/`Uncontained`/`Unknown`;
  no leak is claimed impossible.
- A user-scope session configures a profile but cannot bring it up without the
  NetworkManager policy that every GNOME session already has; the interface says
  so instead of failing silently.

See [`SECURITY.md`](SECURITY.md) for the private vulnerability report.

## Licence

`GPL-3.0-or-later`. Copyright (C) 2026 Younes BENMOUSSA
<younes.benmoussa@pm.me>. The documentation is `CC-BY-SA-4.0`; the third-party
components are listed in [`THIRD-PARTY`](THIRD-PARTY).
