#!/bin/sh
# Build the Debian binary package from the working copy.
#
# The build is a Native AOT publish staged into a dpkg tree and packed with
# dpkg-deb, so it needs no debhelper. The debian/ directory remains the policy
# source for a future signed source package.
#
# Prerequisites: the .NET SDK pinned by global.json, a clang toolchain (Native
# AOT), dpkg-deb, and Core cloned at the CORE_VERSION pin beside this repository.
set -eu

here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo=$(dirname -- "$here")

version=$(sed -n 's/.*<VersionPrefix[^>]*>\([^<]*\)<\/VersionPrefix>.*/\1/p' "$repo/Directory.Build.props" | head -1)
[ -n "$version" ] || { echo "VersionPrefix not found in Directory.Build.props" >&2; exit 1; }

pin=$(tr -d '[:space:]' < "$repo/CORE_VERSION")
core=$(dirname -- "$repo")/core
[ -f "$core/src/Core/Core.csproj" ] || {
    echo "Core is missing: clone https://github.com/benmoussa-younes/core at $pin beside this repository" >&2
    exit 1
}

command -v dotnet >/dev/null 2>&1 || { echo "dotnet is missing" >&2; exit 1; }
command -v dpkg-deb >/dev/null 2>&1 || { echo "dpkg-deb is missing (install dpkg-dev)" >&2; exit 1; }

export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT
pkg="$stage/pkg"
mkdir -p "$pkg/DEBIAN" "$pkg/usr/lib/vpn-manager" "$pkg/usr/bin" \
    "$pkg/usr/share/applications" "$pkg/usr/share/icons/hicolor/scalable/apps" \
    "$pkg/usr/share/doc/vpn-manager" "$pkg/usr/share/man/man1" "$pkg/usr/lib/sysctl.d" \
    "$pkg/usr/share/polkit-1/actions" "$pkg/usr/share/dbus-1/services" \
    "$pkg/usr/share/lintian/overrides"

dotnet publish "$repo/src/VpnManager.App/VpnManager.App.csproj" \
    -c Release -r linux-x64 -o "$pkg/usr/lib/vpn-manager"
dotnet publish "$repo/src/VpnManager.Helper/VpnManager.Helper.csproj" \
    -c Release -r linux-x64 -o "$pkg/usr/lib/vpn-manager"

find "$pkg/usr/lib/vpn-manager" -type f \( -name '*.dbg' -o -name '*.pdb' -o -name '*.xml' \) -delete
if command -v strip >/dev/null 2>&1; then
    strip --strip-unneeded "$pkg/usr/lib/vpn-manager/libSkiaSharp.so" "$pkg/usr/lib/vpn-manager/libHarfBuzzSharp.so" 2>/dev/null || true
fi

ln -s ../lib/vpn-manager/vpn-manager "$pkg/usr/bin/vpn-manager"

cp "$repo/packaging/vpn-manager.desktop" "$pkg/usr/share/applications/"
cp "$repo/packaging/icons/vpn-manager.svg" "$pkg/usr/share/icons/hicolor/scalable/apps/" 2>/dev/null || true
mkdir -p "$pkg/usr/share/icons/hicolor"
cp -a "$repo/packaging/icons/hicolor/." "$pkg/usr/share/icons/hicolor/"
cp "$repo/packaging/debian/60-vpn-manager.conf" "$pkg/usr/lib/sysctl.d/"
cp "$repo/packaging/polkit/com.bit-crafts.vpn-manager.policy" "$pkg/usr/share/polkit-1/actions/"
cp "$repo/packaging/dbus/com.bit-crafts.VpnManager.service" "$pkg/usr/share/dbus-1/services/"

cp "$repo/NOTICE" "$repo/THIRD-PARTY" "$pkg/usr/share/doc/vpn-manager/"
cp "$repo/LICENSES/GPL-3.0-or-later.txt" "$repo/LICENSES/LGPL-3.0-or-later.txt" "$repo/LICENSES/CC-BY-SA-4.0.txt" "$pkg/usr/share/doc/vpn-manager/"
cp "$repo/packaging/debian/copyright" "$pkg/usr/share/doc/vpn-manager/copyright"
gzip -9 -n -c "$repo/packaging/debian/vpn-manager.1" > "$pkg/usr/share/man/man1/vpn-manager.1.gz"

installed_size=$(du -sk "$pkg" | cut -f1)
cat > "$pkg/DEBIAN/control" <<EOF
Package: vpn-manager
Version: $version-1
Architecture: amd64
Maintainer: Younes Benmoussa <younes.benmoussa@pm.me>
Installed-Size: $installed_size
Depends: libc6, network-manager, wireguard-tools, procps, pkexec, libfontconfig1, libx11-6, libice6, libsm6, libxext6, libxrandr2, libxi6, libxcursor1
Suggests: wireguard
Section: net
Priority: optional
Homepage: https://github.com/benmoussa-younes/vpn-manager
Description: per-user WireGuard client for Debian GNOME
 VPN Manager is a desktop client for WireGuard on a Debian GNOME host. It
 lists the profiles under the operator's configuration directory and the
 WireGuard connections NetworkManager owns, imports and exports profiles,
 connects and disconnects through NetworkManager, and shows the live state,
 the transfer counters, the IPv6 classification and the DNS containment.
 .
 Everything happens in the operator's own configuration directory. The
 application never asks for a password and never reads or writes
 /etc/wireguard. Its one privileged action, --fix-sysctl, goes through a fixed
 polkit helper.
EOF

# dh expands the token when it builds the source package; this manual package
# has no debhelper, so the staged script must not carry it.
sed '/^#DEBHELPER#$/d' "$repo/packaging/debian/vpn-manager.postinst" > "$pkg/DEBIAN/postinst"
chmod 0755 "$pkg/DEBIAN/postinst"
cp "$repo/packaging/debian/vpn-manager.lintian-overrides" "$pkg/usr/share/lintian/overrides/vpn-manager"
gzip -9 -n -c "$repo/packaging/debian/changelog" > "$pkg/usr/share/doc/vpn-manager/changelog.Debian.gz"

# Debian files are world-readable: the source tree may carry the operator's
# group umask, so the staged modes are fixed here instead of trusted.
find "$pkg" -type d -exec chmod 0755 {} +
find "$pkg" -type f -exec chmod 0644 {} +
chmod 0755 "$pkg/usr/lib/vpn-manager/vpn-manager"
chmod 0755 "$pkg/usr/lib/vpn-manager/vpn-manager-helper"
chmod 0755 "$pkg/DEBIAN/postinst"

(
    cd "$pkg"
    find usr -type f -print0 | sort -z | xargs -0 md5sum > DEBIAN/md5sums
)

mkdir -p "$repo/artifacts"
deb="$repo/artifacts/vpn-manager_${version}-1_amd64.deb"
rm -f "$deb"
dpkg-deb --build --root-owner-group "$pkg" "$deb"
echo "Built $deb"
