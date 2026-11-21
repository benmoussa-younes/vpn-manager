# Packaging

`build-deb.sh` builds the Debian binary package from the working copy with
`dpkg-deb`; it does not need debhelper. It publishes the Native AOT binary,
stages the desktop entry, the icons, the manual page, the sysctl drop-in and the
legal notices, then writes `artifacts/vpn-manager_<version>-1_amd64.deb`.

The `debian/` directory is the policy source for a future signed source package.
The `postinst` applies only the package's own sysctl drop-in
(`/usr/lib/sysctl.d/60-vpn-manager.conf`); the application itself never
elevates.

Prerequisites: the .NET SDK pinned by `global.json`, a clang toolchain for
Native AOT, `dpkg-deb`, and Core cloned at the `CORE_VERSION` pin beside this
repository.
