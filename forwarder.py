#!/usr/bin/env python3
"""
forwarder.py - the Tailport tailnet door for Windows.

Listens on local ports and tunnels every byte through the WSL2
tailscaled SOCKS5 proxy, so ANY Windows app can reach ANY service
on the tailnet - SSH, self-hosted apps, LLMs - without Tailscale
ever touching the Windows network stack (so it never fights
Astrill or any other VPN).

The service list comes from tailport.config (next to this script):
  forward.N = local:host:port      one list, any tailnet service
The forward with the smallest local port is the status anchor
of the tray icon (it only needs to answer TCP).

Chain:
  Windows app -> 127.0.0.1:<local> -> this script -> SOCKS5
  (127.0.0.1:1055, WSL2 tailscaled) -> <host>:<port> on the tailnet

Requires: WSL2 running tailscaled in userspace mode with
          --socks5-server=<socks_port>, and pysocks in the Python install.

Usage:
  python forwarder.py                      # reads tailport.config next to it
  python forwarder.py --config D:\cfg.txt  # explicit config path
"""

import argparse
import os
import socket
import socketserver
import sys
import threading
import time

import socks  # PySocks

BUF = 65536
HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_CONFIG = os.path.join(HERE, "tailport.config")
PID_FILE = os.path.join(HERE, "forwarder.pid")
LOG_FILE = os.path.join(HERE, "tailport.log")


def log(msg):
    """Print to console (if any) AND append to the log file."""
    line = f"[{time.strftime('%Y-%m-%d %H:%M:%S')}] {msg}"
    print(line, flush=True)
    try:
        with open(LOG_FILE, "a") as f:
            f.write(line + "\n")
    except OSError:
        pass


# ---------------- config ----------------

def load_config(path):
    """key=value config: '#' comments + blank lines ignored, keys lowercased."""
    cfg = {}
    try:
        with open(path, encoding="utf-8-sig") as f:
            for raw in f:
                line = raw.strip()
                if not line or line.startswith("#") or "=" not in line:
                    continue
                k, _, v = line.partition("=")
                cfg[k.strip().lower()] = v.strip()
    except OSError as e:
        log(f"cannot read config '{path}': {e}")
        sys.exit(1)
    return cfg


def forward_specs(cfg):
    """Ordered [(local_port, host, port), ...]: every forward.N line, one list.
    Sorted by local port so the first spec is the tray's status anchor."""
    specs = []
    for k, v in cfg.items():
        if not k.startswith("forward."):
            continue
        parts = v.split(":")
        if len(parts) != 3 or not parts[0].isdigit() or not parts[2].isdigit():
            log(f"bad forward entry {k}={v} (want local:host:port)")
            continue
        specs.append((int(parts[0]), parts[1], int(parts[2])))
    specs.sort(key=lambda s: s[0])
    return specs


# ---------------- SOCKS plumbing ----------------

def hint_for(e, socks_label="127.0.0.1:1055"):
    """Map a SOCKS failure to an actionable hint for the user."""
    if isinstance(e, (socks.ProxyConnectionError, ConnectionRefusedError, OSError)):
        return f"SOCKS5 proxy {socks_label} unreachable - is WSL2 up? (Turn ON boots it)"
    if isinstance(e, socket.timeout):
        return "timed out - tailnet down or logged out? (wsl tailscale status)"
    if isinstance(e, socks.GeneralProxyError):
        return f"SOCKS5 refused the request ({e}) - target unreachable on the tailnet?"
    return f"{type(e).__name__}: {e}"


def pipe(src, dst):
    """Copy bytes from src to dst until EOF, then half-close dst."""
    try:
        while True:
            data = src.recv(BUF)
            if not data:
                break
            dst.sendall(data)
    except OSError:
        pass
    finally:
        try:
            dst.shutdown(socket.SHUT_WR)
        except OSError:
            pass


class ForwardHandler(socketserver.StreamRequestHandler):
    def handle(self):
        target = (self.server.target_host, self.server.target_port)
        try:
            s = socks.socksocket()
            s.set_proxy(socks.SOCKS5, self.server.socks_host, self.server.socks_port)
            s.settimeout(20)
            s.connect(target)
        except Exception as e:
            label = f"{self.server.socks_host}:{self.server.socks_port}"
            log(f"FAIL {self.client_address[0]} -> {target[0]}:{target[1]}: {hint_for(e, label)}")
            return
        t1 = threading.Thread(target=pipe, args=(self.connection, s), daemon=True)
        t2 = threading.Thread(target=pipe, args=(s, self.connection), daemon=True)
        t1.start()
        t2.start()
        t1.join()
        t2.join()
        s.close()


class ForwardServer(socketserver.ThreadingTCPServer):
    # NOTE: allow_reuse_address must stay FALSE on Windows. SO_REUSEADDR
    # there means "any process may bind the same port" -> orphaned instances
    # would stack up and nothing could ever free the port.
    daemon_threads = True


def probe(srv, local, host, port):
    """Startup self-test of one forward's chain, reported in the background."""
    try:
        s = socks.socksocket()
        s.set_proxy(socks.SOCKS5, srv.socks_host, srv.socks_port)
        s.settimeout(30)
        s.connect((host, port))
        s.close()
        log(f"chain OK: 127.0.0.1:{local} -> {host}:{port} "
            f"via SOCKS5 {srv.socks_host}:{srv.socks_port}")
    except Exception as e:
        label = f"{srv.socks_host}:{srv.socks_port}"
        log(f"chain DOWN at startup (127.0.0.1:{local} -> {host}:{port}): {hint_for(e, label)}")


# ---------------- main ----------------

def main():
    ap = argparse.ArgumentParser(description="Tailport tailnet door (SOCKS5 port forwarder)")
    ap.add_argument("--config", default=DEFAULT_CONFIG,
                    help="config file (default: tailport.config next to this script)")
    args = ap.parse_args()

    cfg = load_config(args.config)
    socks_host = cfg.get("socks_host", "127.0.0.1")
    socks_port = int(cfg.get("socks_port", "1055"))

    specs = forward_specs(cfg)
    if not specs:
        log("no forwards configured - add forward.N lines to " + args.config)
        sys.exit(1)

    # bind everything FIRST: a busy port aborts startup cleanly instead of
    # leaving half the service list running
    servers = []
    for local, host, port in specs:
        try:
            srv = ForwardServer(("127.0.0.1", local), ForwardHandler)
        except OSError as e:
            log(f"cannot bind 127.0.0.1:{local}: {e} (another forwarder already running?)")
            for s in servers:
                s.server_close()
            sys.exit(1)
        srv.target_host, srv.target_port = host, port
        srv.socks_host, srv.socks_port = socks_host, socks_port
        servers.append(srv)

    with open(PID_FILE, "w") as f:
        f.write(str(os.getpid()))

    for srv, (local, host, port) in zip(servers, specs):
        threading.Thread(target=srv.serve_forever, daemon=True).start()
        log(f"listening 127.0.0.1:{local} -> {host}:{port} via SOCKS5 {socks_host}:{socks_port}")
        threading.Thread(target=probe, args=(srv, local, host, port), daemon=True).start()

    try:
        threading.Event().wait()
    except KeyboardInterrupt:
        log("stopped (Ctrl+C)")
    finally:
        for s in servers:
            s.server_close()
        try:
            os.remove(PID_FILE)
        except OSError:
            pass


if __name__ == "__main__":
    main()
