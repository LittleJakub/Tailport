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

- **ON** — icon turns violet, your apps reach the remote model through the door
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

A working, branded tray app (native Windows, .NET Framework 4.8 — zero
runtime deps) with a violet 2026 theme, glow hover, pastel state colors and
an installer on the way:

- [x] Working tray app (native Windows, ~110 KB, .NET Framework 4.8 — zero runtime deps)
- [x] Forwarder + keeper + launchers (proven on the author's machine)
- [x] 2026-grade icon + theme (violet identity, glow hover, pastel states)
- [ ] Config-driven (no hardcoded addresses) — *in progress*
- [ ] Setup wizard installer (asks for WSL2, tailnet IP, ports, model)
- [ ] One-command WSL2 bootstrap for new users

## Repository layout

```
src/         C# tray app source (WinForms, net48)  -> build.cmd publishes it
build.cmd    builds Tailport.exe into the repo root (the runtime folder)
forwarder.py the Python SOCKS5 forwarder (lives next to the exe at runtime)
start.cmd    manual CLI path: boot WSL + start the forwarder
stop.cmd     manual CLI path: stop the forwarder
check.cmd    manual CLI path: status + recent log
assets/      icons (app tile, tray states)
```

## Requirements

- Windows 10/11, WSL2 with a Linux distro, Tailscale account
- A remote machine on your tailnet running an OpenAI-compatible server
  (llama.cpp, Ollama, ...)
- **No Tailscale installed on Windows** — that's the point

## License

MIT — see [LICENSE](LICENSE).
