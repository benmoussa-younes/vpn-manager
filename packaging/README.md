# Packaging

`build-deb.sh` builds the Debian binary package from the working copy with
`dpkg-deb`; it does not need debhelper. It publishes the Native AOT binary,
stages the desktop entry, the icons, the manual page, the sysctl drop-in and the
legal notices, then writes `artifacts/vpn-manager_<version>-1_amd64.deb`.

The `debian/` directory is the policy source for a future signed source package.
It is not a buildable source package today: the projects consume Core from a
sibling clone by design, and a Debian source package must ship its build inputs.
The binary package built by this script is the shipping artifact; the source
archive from the release includes this repository only.
The `postinst` applies only the package's own sysctl drop-in
(`/usr/lib/sysctl.d/60-vpn-manager.conf`); the application itself never
elevates.

Prerequisites: the .NET SDK pinned by `global.json`, a clang toolchain for
Native AOT, `dpkg-deb`, and Core cloned at the `CORE_VERSION` pin beside this
repository.

The package also installs the root-owned `vpn-manager-helper` and the polkit
action `com.bit-crafts.vpn-manager.sysctl-src-valid-mark`; `pkexec` launches the
helper and polkit asks the operator.

`lintian` on the hand-built binary reports the three libraries embedded in
`libSkiaSharp.so` (freetype, libjpeg, libpng) and the missing Debian bug
closure. Both are expected and documented in
`debian/vpn-manager.lintian-overrides`: the AOT bundle embeds Skia's copies, and
the package is not uploaded to the Debian archive.
