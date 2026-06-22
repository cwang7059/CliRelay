#!/usr/bin/env python3
import os
import sys
import time

import paramiko

HOST = os.environ.get("DEPLOY_HOST", "67.215.253.110")
USER = os.environ.get("DEPLOY_USER", "root")
PASSWORD = os.environ.get("DEPLOY_PASSWORD", "")
LOCAL_BIN = os.environ.get(
    "DEPLOY_LOCAL_BIN",
    os.path.join(os.path.dirname(__file__), "..", "cli-proxy-api-new"),
)
REMOTE_DIR = os.environ.get("DEPLOY_REMOTE_DIR", "/opt/clirelay2")
REMOTE_TEMP = f"{REMOTE_DIR}/cli-proxy-api-new"
SERVICE = os.environ.get("DEPLOY_SERVICE", "clirelay2")


def run_ssh(client: paramiko.SSHClient, command: str) -> tuple[int, str, str]:
    print(f"$ {command}")
    _, stdout, stderr = client.exec_command(command, get_pty=True)
    out = stdout.read().decode("utf-8", errors="replace")
    err = stderr.read().decode("utf-8", errors="replace")
    code = stdout.channel.recv_exit_status()
    if out.strip():
        print(out.rstrip())
    if err.strip():
        print(err.rstrip(), file=sys.stderr)
    return code, out, err


def main() -> int:
    if not PASSWORD:
        print("DEPLOY_PASSWORD is required", file=sys.stderr)
        return 1
    local_bin = os.path.abspath(LOCAL_BIN)
    if not os.path.isfile(local_bin):
        print(f"local binary not found: {local_bin}", file=sys.stderr)
        return 1

    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    print(f"Connecting to {USER}@{HOST} ...")
    client.connect(HOST, username=USER, password=PASSWORD, timeout=30)

    print(f"Uploading {local_bin} -> {REMOTE_TEMP}")
    sftp = client.open_sftp()
    sftp.put(local_bin, REMOTE_TEMP)
    sftp.chmod(REMOTE_TEMP, 0o755)
    sftp.close()

    deploy_script = f"""
set -e
SERVICE_BIN="$(systemctl show -p ExecStart --value {SERVICE} | sed -nE 's/.*path=([^ ;]+).*/\\1/p' | head -n1)"
if [ -z "$SERVICE_BIN" ]; then
  if [ -x {REMOTE_DIR}/clirelay2 ]; then
    SERVICE_BIN="{REMOTE_DIR}/clirelay2"
  else
    SERVICE_BIN="{REMOTE_DIR}/cli-proxy-api"
  fi
fi
SERVICE_DIR="$(dirname "$SERVICE_BIN")"
SERVICE_NAME="$(basename "$SERVICE_BIN")"
TEMP_BIN="{REMOTE_TEMP}"
cd "$SERVICE_DIR"
echo "Service binary: $SERVICE_BIN"
systemctl stop {SERVICE} 2>/dev/null || true
sleep 1
PIDS=$(pgrep -f "^${{SERVICE_BIN}}$" 2>/dev/null) || true
if [ -n "$PIDS" ]; then
  kill $PIDS 2>/dev/null || true
  sleep 2
fi
if [ -f "$SERVICE_NAME" ]; then
  cp "$SERVICE_NAME" "${{SERVICE_NAME}}.bak.$(date +%Y%m%d_%H%M%S)"
fi
mv "$TEMP_BIN" "$SERVICE_NAME"
chmod +x "$SERVICE_NAME"
ls -1t "${{SERVICE_NAME}}.bak."* 2>/dev/null | tail -n +4 | xargs -r rm -f
systemctl start {SERVICE}
sleep 3
systemctl is-active {SERVICE}
curl -s -o /dev/null -w "auth-login:%{{http_code}}\\n" -X POST http://127.0.0.1:8317/v0/management/auth/login -H "Content-Type: application/json" -d '{{"username":"probe","password":"probe"}}'
"""

    code, _, _ = run_ssh(client, deploy_script)
    client.close()
    return code


if __name__ == "__main__":
    raise SystemExit(main())
