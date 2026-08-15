#!/usr/bin/env bash
# bootstrap-wsl.sh - one-command Tailport WSL2 setup (run as root inside the distro).
#
# Installs tailscaled in userspace + SOCKS5 mode - exactly the author's proven rig:
#   /usr/local/bin/tailscaled --tun=userspace-networking --socks5-server=localhost:1055
#
# Idempotent: safe to re-run. Skips what's already in place.
# Usage (from Windows):  wsl -d Ubuntu -u root -- bash /mnt/c/path/to/bootstrap-wsl.sh
set -euo pipefail

SOCKS_PORT="${1:-1055}"
STATE_DIR=/var/lib/tailscale
UNIT=/etc/systemd/system/tailscaled.service

echo "== Tailport WSL2 bootstrap (tailscaled userspace + SOCKS5 :${SOCKS_PORT}) =="

# 1) tailscaled binary (static build, no apt repo needed)
if [ -x /usr/local/bin/tailscaled ]; then
    echo "[ok] tailscaled already installed: $(/usr/local/bin/tailscaled --version | head -1)"
else
    echo "[..] downloading the static tailscale build..."
    VER=$(curl -fsSL https://pkgs.tailscale.com/stable/ \
        | grep -oE 'tailscale_[0-9.]+_amd64\.tgz' | sort -V | tail -1)
    curl -fsSL "https://pkgs.tailscale.com/stable/${VER}" -o /tmp/ts.tgz
    tar -xzf /tmp/ts.tgz -C /tmp
    install -m 0755 "/tmp/${VER%.tgz}/tailscale" /usr/local/bin/tailscale
    install -m 0755 "/tmp/${VER%.tgz}/tailscaled" /usr/local/bin/tailscaled
    rm -rf /tmp/ts.tgz "/tmp/${VER%.tgz}"
    echo "[ok] installed ${VER}"
fi

# 2) systemd unit: userspace networking (no kernel TUN in WSL) + the SOCKS5 door
mkdir -p "${STATE_DIR}"
cat > "${UNIT}" <<EOF
[Unit]
Description=Tailscale (WSL2 userspace + SOCKS5)
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
ExecStart=/usr/local/bin/tailscaled --tun=userspace-networking --socks5-server=localhost:${SOCKS_PORT} --state=${STATE_DIR}/tailscaled.state
Restart=on-failure

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable tailscaled >/dev/null 2>&1 || true
systemctl start tailscaled || true
echo "[ok] tailscaled unit installed + started"

# 3) join the tailnet (prints the login URL; SSH enabled for tailscale ssh)
if tailscale status >/dev/null 2>&1; then
    echo "[ok] already on the tailnet:"
    tailscale status | head -5
else
    echo "[..] not logged in yet - starting login (browser URL will appear below):"
    tailscale up --accept-dns=true --ssh || true
fi

echo
echo "== done. Next: edit tailport.config (your server's IP + ports),"
echo "   then start Tailport and click Turn ON. =="
