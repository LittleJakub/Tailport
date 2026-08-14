#!/usr/bin/env python3
"""
tailnet-forward.py — local port forward through the WSL2 Tailscale SOCKS5 proxy.

Purpose: let ANY Windows app (proxy-unaware, like Hermes Desktop) reach a
tailnet service through Tailscale, WITHOUT Tailscale touching the Windows
network stack (so it never fights Astrill).

Chain:
  Windows app -> 127.0.0.1:<local_port> -> this script -> SOCKS5
  (127.0.0.1:1055, WSL2 tailscaled) -> <host>:<port> on the tailnet

Requires: WSL2 running with tailscaled (userspace + --socks5-server=1055),
          pysocks in the Python311 install.

Usage:
  python tailnet-forward.py                          # 8080 -> parthenon llama :8080
  python tailnet-forward.py --local 9090 --host 100.101.102.103 --port 9090
"""

import argparse
import os
import socket
import socketserver
import threading
import time

import socks  # PySocks

SOCKS_HOST = "127.0.0.1"
SOCKS_PORT = 1055
BUF = 65536

HERE = os.path.dirname(os.path.abspath(__file__))
PID_FILE = os.path.join(HERE, "tailnet-forward.pid")
LOG_FILE = os.path.join(HERE, "tailnet-forward.log")


def log(msg):
    """Print to console (if any) AND append to the log file."""
    line = f"[{time.strftime('%Y-%m-%d %H:%M:%S')}] {msg}"
    print(line, flush=True)
    try:
        with open(LOG_FILE, "a") as f:
            f.write(line + "\n")
    except OSError:
        pass


def hint_for(e):
    """Map a SOCKS failure to an actionable hint for the user."""
    if isinstance(e, (socks.ProxyConnectionError, ConnectionRefusedError, OSError)):
        return "SOCKS5 proxy 127.0.0.1:1055 unreachable — is WSL2 up? (run: wsl -d Ubuntu; or start.cmd does it for you)"
    if isinstance(e, socket.timeout):
        return "timed out — tailnet down or logged out? (check: wsl -d Ubuntu -- tailscale status)"
    if isinstance(e, socks.GeneralProxyError):
        return f"SOCKS5 refused the request ({e}) — target unreachable on the tailnet?"
    return f"{type(e).__name__}: {e}"


def socks_connect(target_host, target_port, timeout=20):
    s = socks.socksocket()
    s.set_proxy(socks.SOCKS5, SOCKS_HOST, SOCKS_PORT)
    s.settimeout(timeout)
    s.connect((target_host, target_port))
    return s


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
            s = socks_connect(*target)
        except Exception as e:
            log(f"FAIL {self.client_address[0]} -> {target[0]}:{target[1]}: {hint_for(e)}")
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
    # would stack up on 8080 and stop.cmd could never free it.
    daemon_threads = True


def probe(args):
    """Startup self-test of the whole chain, reported once in the background."""

    def run():
        try:
            s = socks_connect(args.host, args.port, timeout=30)
            s.close()
            log(f"chain OK: 127.0.0.1:{args.local} -> {args.host}:{args.port} via SOCKS5 {SOCKS_HOST}:{SOCKS_PORT}")
        except Exception as e:
            log(f"chain DOWN at startup: {hint_for(e)}")

    threading.Thread(target=run, daemon=True).start()


def main():
    ap = argparse.ArgumentParser(description="Local port forward via WSL2 tailscaled SOCKS5")
    ap.add_argument("--local", type=int, default=8080, help="local listen port (default 8080)")
    ap.add_argument("--host", default="100.101.102.103", help="target tailnet IP (default parthenon)")
    ap.add_argument("--port", type=int, default=8080, help="target port (default 8080)")
    args = ap.parse_args()

    try:
        server = ForwardServer(("127.0.0.1", args.local), ForwardHandler)
    except OSError as e:
        log(f"cannot bind 127.0.0.1:{args.local}: {e} (another forwarder already running?)")
        sys.exit(1)
    server.target_host = args.host
    server.target_port = args.port

    with open(PID_FILE, "w") as f:
        f.write(str(os.getpid()))

    log(f"listening on 127.0.0.1:{args.local} -> {args.host}:{args.port} via SOCKS5 {SOCKS_HOST}:{SOCKS_PORT}")
    probe(args)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        log("stopped (Ctrl+C)")
    finally:
        try:
            os.remove(PID_FILE)
        except OSError:
            pass


if __name__ == "__main__":
    main()
