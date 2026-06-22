#!/usr/bin/env python3
"""Deploy latest CliRelay source to remote Docker host and rebuild image."""

from __future__ import annotations

import io
import os
import sys
import tarfile
import time
from pathlib import Path

import paramiko

HOST = os.environ.get("DEPLOY_HOST", "67.215.253.110")
USER = os.environ.get("DEPLOY_USER", "root")
PASSWORD = os.environ.get("DEPLOY_PASSWORD", "")
LOCAL_ROOT = Path(os.environ.get("DEPLOY_LOCAL_ROOT", Path(__file__).resolve().parents[1]))
REMOTE_ROOT = os.environ.get("DEPLOY_REMOTE_ROOT", "/opt/clirelay")
REMOTE_SRC = f"{REMOTE_ROOT}/custom-src"
COMMIT = os.environ.get("DEPLOY_COMMIT", "da69b587")
FRONTEND_REF = os.environ.get("DEPLOY_FRONTEND_REF", "feature/electron-management-shell")
FRONTEND_COMMIT = os.environ.get("DEPLOY_FRONTEND_COMMIT", "06bc55c")
IMAGE_TAG = os.environ.get("DEPLOY_IMAGE_TAG", f"clirelay-local:panel-{COMMIT}")

SKIP_DIRS = {
    ".git",
    ".tools",
    "node_modules",
    "dist",
    "auths",
    "auths.backup-before-restore-20260528-133335",
    "data",
    "logs",
    "examples",
}
SKIP_FILES = {
    "cli-proxy-api-new",
    "custom-clirelay-build.tar.gz",
}


def should_skip(path: Path) -> bool:
    parts = set(path.parts)
    if parts & SKIP_DIRS:
        return True
    if path.name in SKIP_FILES:
        return True
    if path.suffix == ".exe":
        return True
    return False


def build_source_tarball(root: Path) -> bytes:
    buffer = io.BytesIO()
    with tarfile.open(fileobj=buffer, mode="w:gz") as tar:
        for item in root.rglob("*"):
            rel = item.relative_to(root)
            if should_skip(rel):
                continue
            if item.is_dir():
                continue
            tar.add(item, arcname=str(rel).replace("\\", "/"))
    buffer.seek(0)
    return buffer.read()


def run_ssh(client: paramiko.SSHClient, command: str, timeout: int = 3600) -> tuple[int, str]:
    print(f"\n$ {command}")
    _, stdout, stderr = client.exec_command(command, get_pty=True, timeout=timeout)
    out = stdout.read().decode("utf-8", errors="replace")
    err = stderr.read().decode("utf-8", errors="replace")
    code = stdout.channel.recv_exit_status()
    if out.strip():
        print(out.rstrip())
    if err.strip():
        print(err.rstrip(), file=sys.stderr)
    return code, out + err


def main() -> int:
    if not PASSWORD:
        print("DEPLOY_PASSWORD is required", file=sys.stderr)
        return 1

    print(f"Packing source from {LOCAL_ROOT} ...")
    payload = build_source_tarball(LOCAL_ROOT)
    print(f"Tarball size: {len(payload) / 1024 / 1024:.1f} MB")

    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    print(f"Connecting to {USER}@{HOST} ...")
    client.connect(HOST, username=USER, password=PASSWORD, timeout=30)

    remote_tar = f"{REMOTE_ROOT}/clirelay-src-{COMMIT}.tar.gz"
    print(f"Uploading -> {remote_tar}")
    sftp = client.open_sftp()
    with sftp.file(remote_tar, "wb") as remote_file:
        remote_file.write(payload)
    sftp.close()

    build_date = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
    deploy_cmds = f"""
set -e
cd {REMOTE_ROOT}
rm -rf {REMOTE_SRC}.next
mkdir -p {REMOTE_SRC}.next
tar -xzf {remote_tar} -C {REMOTE_SRC}.next
rm -rf {REMOTE_SRC}.bak
if [ -d {REMOTE_SRC} ]; then mv {REMOTE_SRC} {REMOTE_SRC}.bak; fi
mv {REMOTE_SRC}.next {REMOTE_SRC}
cd {REMOTE_SRC}
docker build \
  --build-arg VERSION=panel-{COMMIT} \
  --build-arg COMMIT={COMMIT} \
  --build-arg BUILD_DATE={build_date} \
  --build-arg FRONTEND_REF={FRONTEND_REF} \
  --build-arg FRONTEND_COMMIT={FRONTEND_COMMIT} \
  --build-arg UI_VERSION=panel-{FRONTEND_COMMIT} \
  -t {IMAGE_TAG} .
cd {REMOTE_ROOT}
python3 - <<'PY'
from pathlib import Path
path = Path('docker-compose.yml')
text = path.read_text()
lines = []
for line in text.splitlines():
    if line.strip().startswith('image:'):
        lines.append('    image: {IMAGE_TAG}')
    else:
        lines.append(line)
path.write_text('\\n'.join(lines) + '\\n')
PY
docker compose up -d --force-recreate cli-proxy-api
sleep 5
docker ps --filter name=cli-proxy-api
curl -s -o /dev/null -w "auth-login:%{{http_code}}\\n" -X POST http://127.0.0.1:8317/v0/management/auth/login -H "Content-Type: application/json" -d '{{"username":"probe","password":"probe"}}'
"""

    code, _ = run_ssh(client, deploy_cmds, timeout=3600)
    client.close()
    return code


if __name__ == "__main__":
    raise SystemExit(main())
