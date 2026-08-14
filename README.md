<h1><img src="assets/icon-512.png" width="40" height="40" alt="Tailport" align="absmiddle"> Tailport</h1>

**A tray toggle that bridges Windows apps to your Tailscale-hosted LLM — safely, even when a VPN client owns your network stack.**

If you run Astrill (or any strict VPN) on Windows **and** want to reach a model
on another machine over Tailscale, you know the pain: Tailscale and the VPN
fight over the network stack, and nothing ends up working.

**Tailport solves it by construction** — the Tailscale engine lives *inside
WSL2* (userspace + SOCKS5), the VPN owns Windows, and a tiny native tray app
gives you a one-click door to your remote model:

```
your app (Hermes, any LLM client)
        │  plain HTTP
        ▼
127.0.0.1:8080  ◄── Tailport tray toggle (ON/OFF, violet port icon)
        │  SOCKS5
        ▼
WSL2 tailscaled (userspace, never touches the Windows stack)
        │  WireGuard
        ▼
your server's tailnet address:11434/8080  ◄── llama.cpp / Ollama
```

- **ON** — icon turns white, your apps reach the remote model through the door
- **OFF** — icon greys out, nothing routes, nothing lingers
- **Quit** — full shutdown of the bridge
- Apps that expect an OpenAI-compatible API just point at `http://127.0.0.1:8080/v1`

## Why

| Problem | Tailport answer |
|---|---|
| Astrill + Tailscale on Windows = mutual destruction | Tailscale never touches the Windows stack — it lives in WSL2 |
| Proxy-unaware apps can't use SOCKS5 | A local forwarder exposes the tunnel as plain HTTP on `127.0.0.1` |
| WSL2 kills the VM after ~60s idle | A hidden "keeper" session holds it open while the service is ON |
| Setup is fiddly | The installer asks for everything up front and wires it up |

## Status

Early stage — a proven, working prototype being refactored into a
configurable, installable product:

- [x] Working tray app (native Windows, 43 KB, .NET Framework 4.8 — zero runtime deps)
- [x] Forwarder + keeper + launchers (proven on the author's machine)
- [ ] Config-driven (no hardcoded addresses) — *in progress*
- [ ] 2026-grade icon + theme
- [ ] Setup wizard installer (asks for WSL2, tailnet IP, ports, model)
- [ ] One-command WSL2 bootstrap for new users

## Repository layout

```
src/         C# tray app source (WinForms, net48)
forwarder/   the Python SOCKS5 forwarder
scripts/     start / stop / check launchers (for the CLI-minded)
assets/      icons (tray, menu, installer)
installer/   setup wizard (planned)
```

## Requirements

- Windows 10/11, WSL2 with a Linux distro, Tailscale account
- A remote machine on your tailnet running an OpenAI-compatible server
  (llama.cpp, Ollama, ...)
- **No Tailscale installed on Windows** — that's the point

## License

MIT — see [LICENSE](LICENSE).
