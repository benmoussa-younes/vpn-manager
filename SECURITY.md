# Security policy

## Supported versions

| Version | Supported |
|---|---|
| 0.1.x | yes |
| older | no |

## Report a vulnerability privately

Do not open a public issue, a public pull request or a discussion for a
vulnerability. Send a private report to
<younes.benmoussa@pm.me> with:

- the version and the Debian release;
- the smallest reproduction, and whether it needs a malicious profile, a local
  account, a network position or a display session;
- the impact you observed and the impact you suspect;
- the fix you would accept, if you have one.

You get an acknowledgement within seven days. A fix lands on `main` through the
gate and a release follows when the owner orders it. Credit is given unless you
ask otherwise.

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
- `nmcli`, `wg` and `notify-send` receive an argument vector; no shell is
  involved and a bare tool name never resolves through `PATH`.
- A profile is read with a bounded, no-follow read and validated before it is
  handed to NetworkManager. Hook keys are refused because NetworkManager does
  not execute them.
- The profile and state directories are owner-only; the state directory refuses
  group- or world-write access.
- The display name is also the NetworkManager connection id. It is at most 64
  characters, may not contain control characters or start with `-`, and may not
  collide with another tunnel or another NetworkManager connection; the id is
  passed to `nmcli` as one argument. Renaming a foreign connection is refused.
- The policy page writes through the same validated service as the CLI: the DNS
  mode is one of the four known values, and a routing domain is validated before
  it reaches `nmcli`. The `Full tunnel` mode sets the root routing domain `~.`
  explicitly.
- There is no tray icon and no GNOME extension. Closing the window quits, asking
  before disconnecting a connected profile. A session end (the GNOME session
  manager's `QueryEndSession`/`EndSession` signals, or `SIGTERM`/`SIGINT`) takes
  the same shutdown path without the dialog.
- DNS containment is reported, never promised.
