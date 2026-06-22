#!/usr/bin/env python3
"""Finish remote Docker deploy using tarball already on server."""

from __future__ import annotations

import os
import sys
import time

import paramiko

HOST = os.environ.get("DEPLOY_HOST", "67.215.253.110")
USER = os.environ.get("DEPLOY_USER", "root")
PASSWORD = os.environ.get("DEPLOY_PASSWORD", "")
REMOTE_ROOT = "/opt/clirelay"
COMMIT = os.environ.get("DEPLOY_COMMIT", "da69b587")
FRONTEND_REF = os.environ.get("DEPLOY_FRONTEND_REF", "feature/electron-management-shell")
FRONTEND_COMMIT = os.environ.get("DEPLOY_FRONTEND_COMMIT", "06bc55c")
IMAGE_TAG = f"clirelay-local:panel-{COMMIT}"
REMOTE_TAR = f"{REMOTE_ROOT}/clirelay-src-{COMMIT}.tar.gz"
REMOTE_SRC = f"{REMOTE_ROOT}/custom-src"
LOG = f"{REMOTE_ROOT}/docker-build-{COMMIT}.log"


def run(client: paramiko.SSHClient, cmd: str, timeout: int = 7200) -> tuple[int, str]:
    print(f"\n$ {cmd}")
    _, stdout, stderr = client.exec_command(cmd, get_pty=True, timeout=timeout)
    out = stdout.read().decode("utf-8", errors="replace")
    err = stderr.read().decode("utf-8", errors="replace")
    code = stdout.channel.recv_exit_status()
    text = (out + err).strip()
    if text:
        print(text[-8000:] if len(text) > 8000 else text)
    return code, text


def main() -> int:
    if not PASSWORD:
        print("DEPLOY_PASSWORD required", file=sys.stderr)
        return 1

    build_date = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(HOST, username=USER, password=PASSWORD, timeout=30)

    prep = f"""
set -e
test -f {REMOTE_TAR}
rm -rf {REMOTE_SRC}.next
mkdir -p {REMOTE_SRC}.next
tar -xzf {REMOTE_TAR} -C {REMOTE_SRC}.next
test -f {REMOTE_SRC}.next/internal/api/handlers/management/auth.go
rm -rf {REMOTE_SRC}.bak
if [ -d {REMOTE_SRC} ]; then mv {REMOTE_SRC} {REMOTE_SRC}.bak; fi
mv {REMOTE_SRC}.next {REMOTE_SRC}
echo SOURCE_OK
"""
    code, out = run(client, prep, timeout=300)
    if code != 0 or "SOURCE_OK" not in out:
        client.close()
        return code or 1

    build_cmd = f"""
set -e
cd {REMOTE_SRC}
nohup docker build \
  --build-arg VERSION=panel-{COMMIT} \
  --build-arg COMMIT={COMMIT} \
  --build-arg BUILD_DATE={build_date} \
  --build-arg FRONTEND_REF={FRONTEND_REF} \
  --build-arg FRONTEND_COMMIT={FRONTEND_COMMIT} \
  --build-arg UI_VERSION=panel-{FRONTEND_COMMIT} \
  -t {IMAGE_TAG} . > {LOG} 2>&1 &
echo $! > {REMOTE_ROOT}/docker-build.pid
echo BUILD_STARTED
"""
    code, out = run(client, build_cmd, timeout=60)
    if "BUILD_STARTED" not in out:
        client.close()
        return 1

    print("Waiting for docker build (polling log)...")
    for i in range(180):
        time.sleep(30)
        _, tail = run(
            client,
            f"tail -n 5 {LOG} 2>/dev/null; docker images -q {IMAGE_TAG} 2>/dev/null | head -1",
            timeout=30,
        )
        if IMAGE_TAG.split(":")[1] in tail or "Successfully tagged" in tail:
            # verify image exists
            code2, img = run(client, f"docker images -q {IMAGE_TAG}", timeout=30)
            if img.strip():
                print(f"Image ready after ~{(i+1)*30}s")
                break
        if i % 4 == 0:
            print(f"  still building... ({(i+1)*30}s)")
    else:
        print("Build timeout - check log on server", file=sys.stderr)
        client.close()
        return 1

    restart = f"""
set -e
cd {REMOTE_ROOT}
python3 - <<'PY'
from pathlib import Path
p = Path('docker-compose.yml')
lines = []
for line in p.read_text().splitlines():
    if line.strip().startswith('image:'):
        lines.append('    image: {IMAGE_TAG}')
    else:
        lines.append(line)
p.write_text('\\n'.join(lines) + '\\n')
PY
docker compose up -d --force-recreate cli-proxy-api
sleep 8
docker ps --filter name=cli-proxy-api
curl -s -o /dev/null -w "auth-login:%{{http_code}}\\n" -X POST http://127.0.0.1:8317/v0/management/auth/login -H "Content-Type: application/json" -d '{{"username":"probe","password":"probe"}}'
"""
    code, out = run(client, restart, timeout=120)
    client.close()
    return code


if __name__ == "__main__":
    raise SystemExit(main())
