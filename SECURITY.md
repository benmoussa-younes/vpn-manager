# Security policy

## Status

No version is supported: the software is provided as is, with no warranty,
service level or promised response time. A private report is read on a
best-effort basis; a fix, if any, lands on `main` and may be released when the
owner decides.

## Report a vulnerability privately

Do not open a public issue, a public pull request or a discussion for a
vulnerability. Send a private report to
<younes.benmoussa@pm.me> with:

- the version and the Debian release;
- the smallest reproduction, and whether it needs a malicious profile, a local
  account, a network position or a display session;
- the impact you observed and the impact you suspect;
- the fix you would accept, if you have one.

There is no guaranteed acknowledgement, timeline or fix. If a fix is made it
lands on `main` through the gate, and a release follows only when the owner
orders it.

## Scope

In scope: the application, its Debian packaging, its profile parser and
validator, the process boundary, the profile/state store and the diagnostics.
Out of scope: NetworkManager, the WireGuard kernel module, GNOME, Avalonia and
Core, which have their own security policies.

## Security model

- The GUI runs as the operator. It never invokes `sudo` or an arbitrary root
  command, and never reads or writes `/etc/wireguard`. The one privileged path is
  `--fix-sysctl`, which launches the fixed helper through `pkexec`/polkit.
- A private key is never displayed, logged or passed as a command-line argument.
- `nmcli`, `wg`, `resolvectl` and the fixed helper receive an argument vector; no
  shell is involved and a bare tool name never resolves through `PATH`. Desktop
  notifications go through the session bus (`org.freedesktop.Notifications`).
- A profile is read with a bounded, no-follow read and validated before it is
  handed to NetworkManager. Hook keys are stripped from the copy handed to
  NetworkManager and never executed; the stored profile keeps them.
- The profile and state directories are owner-only; the state directory refuses
  group- or world-write access.
- The session D-Bus service runs as the operator on the user session bus; any
  process of the same account can call its methods. It is not a privilege
  boundary and never runs on the system bus.
- The display name is also the NetworkManager connection id. It is at most 64
  characters, may not contain control characters or start with `-`, and may not
  collide with another tunnel or another NetworkManager connection; the id is
  passed to `nmcli` as one argument. Renaming a foreign connection is refused.
- The policy page writes through the same validated service as the CLI: the DNS
  mode is one of the three known values, and a routing domain is validated
  before it reaches `nmcli`. `Split` sends each listed domain to the VPN as a
  routing domain (`~domain`).
- There is no tray icon and no GNOME extension. Closing the window quits, asking
  before disconnecting a connected profile. A session end (the GNOME session
  manager's `QueryEndSession`/`EndSession` signals, or `SIGTERM`/`SIGINT`) takes
  the same shutdown path without the dialog.
- DNS containment is reported, never promised.
