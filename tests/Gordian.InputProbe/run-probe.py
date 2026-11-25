#!/usr/bin/env python3
"""Run native keyboard routing on a private authenticated Xvfb, without the GPU viewport."""
import argparse
import os
from pathlib import Path
import secrets
import selectors
import shutil
import struct
import subprocess
import sys
import tempfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--input-layout", choices=["us", "de"], default="us")
options = parser.parse_args()
root = Path(__file__).resolve().parents[2]
xvfb = os.environ.get("XVFB", "Xvfb")
if sys.platform != "linux" or shutil.which(xvfb) is None:
    print("SKIP: Linux and Xvfb required", file=sys.stderr)
    sys.exit(77)
with tempfile.TemporaryDirectory(prefix="gordian-input-") as temp:
    state = Path(temp)
    auth = state / "Xauthority"
    def field(data):
        return struct.pack("!H", len(data)) + data
    auth.write_bytes(struct.pack("!H", 65535) + field(b"") + field(b"")
                     + field(b"MIT-MAGIC-COOKIE-1") + field(secrets.token_bytes(16)))
    auth.chmod(0o600)
    env = os.environ.copy()
    for key in ["DISPLAY", "WAYLAND_DISPLAY", "DBUS_SESSION_BUS_ADDRESS", "SESSION_MANAGER"]:
        env.pop(key, None)
    for key in ["HOME", "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_CACHE_HOME", "XDG_RUNTIME_DIR", "TMPDIR"]:
        folder = state / key
        folder.mkdir(mode=0o700)
        env[key] = str(folder)
    env.update(XAUTHORITY=str(auth), GORDIAN_VIEWPORT_PROBE="1", GORDIAN_INPUT_LAYOUT=options.input_layout)
    read_fd, write_fd = os.pipe()
    server = subprocess.Popen([xvfb, "-displayfd", str(write_fd), "-screen", "0", "1280x1024x24",
                               "-nolisten", "tcp", "-auth", str(auth), "-noreset"],
                              pass_fds=[write_fd], env=env, stdout=subprocess.DEVNULL)
    os.close(write_fd)
    try:
        with selectors.DefaultSelector() as selector:
            selector.register(read_fd, selectors.EVENT_READ)
            if not selector.select(10):
                raise RuntimeError("Xvfb did not become ready")
            number = os.read(read_fd, 64).decode().strip()
        if not number.isdigit():
            raise RuntimeError("Xvfb failed")
        env["DISPLAY"] = ":" + number
        subprocess.run(["setxkbmap", "-layout", options.input_layout], env=env, check=True, timeout=10)
        dll = root / "tests/Gordian.InputProbe/bin/Debug/net10.0/Gordian.InputProbe.dll"
        result = subprocess.run(["dotnet", str(dll)], env=env, cwd=state, timeout=120)
        sys.exit(result.returncode if result.returncode >= 0 else 128 - result.returncode)
    finally:
        os.close(read_fd)
        server.terminate()
        server.wait(timeout=10)
