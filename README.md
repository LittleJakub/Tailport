<h1><img src="assets/icon-512.png" width="40" height="40" alt="Tailport" align="absmiddle"> Tailport</h1>

**The Astrill-safe door to your whole Tailscale tailnet — one click from the Windows tray. Any service on your tailnet (SSH, Immich, an LLM, your NAS...) becomes reachable from Windows, without Tailscale ever touching the Windows network stack.**

If you run Astrill (or any strict VPN) on Windows **and** keep your Tailscale engine in WSL2, Windows itself has no route into the tailnet. Tailport closes that gap: a tiny native tray app that turns a WSL2 tailscaled SOCKS5 proxy into localhost doors for *every* service you list in its config.

```
your apps (browser, ssh, anything)
        │  plain TCP/HTTP
        ▼
localhost:<port>  ◄── Tailport tray toggle (ON/OFF, violet port icon)
        │  SOCKS5
        ▼
WSL2 tailscaled (userspace, never touches the Windows stack)
        │  WireGuard
        ▼
any tailnet service (Immich :2283, SSH :22, llama.cpp :8080, ...)
```

- **Service ON** — icon turns violet; every configured service answers on `localhost`
- **Service OFF** — icon greys out, nothing routes, nothing lingers
- **Quit** — full shutdown of the bridge
- Everything is described in one plain-text file: `tailport.config`

## What you get out of the box

| Local address | Service |
|---|---|
| `http://localhost:8080` | the **main forward** — any tailnet service you pick (here: an OpenAI-compatible LLM) |
| `http://localhost:2283` | Immich photos (or anything else you add) |
| `http://localhost:8001` | Paperless-ngx documents (or anything else you add) |

The main forward is the door the tray icon health-checks (it only needs to
answer TCP, so SSH and plain services work too). Add a line to the config,
Turn OFF / Turn ON, and any tailnet service is yours.

## Why

| Problem | Tailport answer |
|---|---|
| Astrill + Tailscale on Windows = mutual destruction | Tailscale never touches the Windows stack — it lives in WSL2 |
| Proxy-unaware apps can't use SOCKS5 | A local forwarder exposes the tunnel as plain localhost ports |
| WSL2 kills the VM after ~60s idle | A hidden "keeper" session holds it open while the service is ON |
| Setup is fiddly | Everything lives in one config file; installer on the way |

## Configuration

Everything is in `tailport.config` (plain `key=value`, `#` comments).
A fresh clone starts from `tailport.config.example` — copy it to
`tailport.config` (that file is git-ignored: it holds your machine
paths and tailnet addresses) and edit:

```ini
pythonw=C:\Path\To\pythonw.exe          # the hidden Python that runs the forwarder
wsl_distro=Ubuntu                       # your WSL2 distro running tailscaled
socks_host=127.0.0.1                    # tailscaled SOCKS5 (WSL2 userspace mode)
socks_port=1055
main_local_port=8080                     # local door for the main forward (status anchor)
main_target=100.101.102.103:8080         # any tailnet service (replace with YOUR tailnet IP)
forward.1=2283:100.101.102.103:2283     # extra forwards: local:tailnet-ip:port
forward.2=8001:100.101.102.103:8001
```

Edit, save, Turn OFF / Turn ON. No rebuilds, no admin.

## Status

A working, branded tray app (native Windows, .NET Framework 4.8 — zero
runtime deps) with a violet 2026 theme, glow hover, pastel state colors:

- [x] Working tray app (native Windows, ~110 KB, .NET Framework 4.8)
- [x] Forwarder + keeper + launchers (proven on the author's machine)
- [x] 2026-grade icon + theme (violet identity, glow hover, pastel states)
- [x] Config-driven: one file describes the whole door (target, ports, forwards)
- [x] In-app Settings window (edit the config from the tray menu)
- [x] One-command WSL2 bootstrap for new users (bootstrap.cmd)
- [ ] Setup wizard installer (asks for WSL2, tailnet IP, ports)

## Repository layout

```
src/            C# tray app source (WinForms, net48) -> build.cmd publishes it
build.cmd       builds Tailport.exe into the repo root (the runtime folder)
forwarder.py    the Python SOCKS5 forwarder (lives next to the exe at runtime)
tailport.config.example every address and port the door exposes (copy -> tailport.config)
tailport.config personal runtime config (git-ignored; machine paths + tailnet IPs)
config.cmd      loads tailport.config for the .cmd scripts (call config.cmd)
start.cmd       manual CLI path: boot WSL + start the forwarder
stop.cmd        manual CLI path: stop the forwarder
check.cmd       manual CLI path: status + recent log
bootstrap.cmd   one-command WSL2 setup for a fresh machine
bootstrap-wsl.sh the WSL-side half of the bootstrap (tailscaled + systemd unit)
assets/         icons (app tile, tray states)
```

## New machine setup

1. **Windows side**: enable WSL2 (`wsl --install` in an admin PowerShell),
   install Ubuntu from the Store, reboot.
2. **Bootstrap**: double-click `bootstrap.cmd` — it installs tailscaled inside
   WSL2 (userspace + SOCKS5, exactly the author's proven rig) and starts the
   login flow (a browser URL appears — approve it with your Tailscale account).
3. **Configure**: copy `tailport.config.example` to `tailport.config`, then
   edit it (or right-click the tray icon → **Settings**) with your server's
   tailnet IP and the ports you want.
4. **Go**: double-click `Tailport.exe`, click **Turn ON** — every configured
   service now answers on `localhost`.

Needs on the Windows side: Python with `pysocks` for the forwarder (the
upcoming installer wizard will handle this automatically).

## Requirements

- Windows 10/11, WSL2 with a Linux distro, Tailscale account
- Python 3 with `pysocks` for the forwarder (the installer handles this)
- A remote machine on your tailnet running the services you want to reach
- **No Tailscale installed on Windows** — that's the point

## License

MIT — see [LICENSE](LICENSE).
