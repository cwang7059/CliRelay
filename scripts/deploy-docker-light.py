#!/usr/bin/env python3
"""Lightweight deploy: build locally, upload artifacts, hot-swap Docker container."""

from __future__ import annotations

import io
import os
import subprocess
import sys
import tarfile
import time
from pathlib import Path

import paramiko

HOST = os.environ.get("DEPLOY_HOST", "67.215.253.110")
USER = os.environ.get("DEPLOY_USER", "root")
PASSWORD = os.environ.get("DEPLOY_PASSWORD", "")
CONTAINER = os.environ.get("DEPLOY_CONTAINER", "cli-proxy-api")
REMOTE_STAGING = os.environ.get("DEPLOY_REMOTE_STAGING", "/opt/clirelay/deploy-staging")

ROOT = Path(__file__).resolve().parents[1]
CODEPROXY = Path(os.environ.get("DEPLOY_CODEPROXY_ROOT", ROOT.parent / "codeProxy"))
BINARY = ROOT / "artifacts" / "CLIProxyAPI"
UPDATER = ROOT / "artifacts" / "clirelay-updater"
PANEL = ROOT / "artifacts" / "panel"
COMMIT = os.environ.get("DEPLOY_COMMIT", "")


def run_local(cmd: list[str], cwd: Path) -> None:
    print(f"\n[local] {' '.join(cmd)}")
    subprocess.run(cmd, cwd=cwd, check=True)


def build_backend(commit: str, build_date: str) -> None:
    artifacts = ROOT / "artifacts"
    artifacts.mkdir(exist_ok=True)
    version = f"panel-{commit[:8]}"
    ldflags = (
        f"-s -w "
        f"-X 'github.com/router-for-me/CLIProxyAPI/v6/internal/buildinfo.Version={version}' "
        f"-X 'github.com/router-for-me/CLIProxyAPI/v6/internal/buildinfo.Commit={commit}' "
        f"-X 'github.com/router-for-me/CLIProxyAPI/v6/internal/buildinfo.BuildDate={build_date}' "
        f"-X 'github.com/router-for-me/CLIProxyAPI/v6/internal/buildinfo.FrontendVersion=panel' "
        f"-X 'github.com/router-for-me/CLIProxyAPI/v6/internal/buildinfo.FrontendCommit=local' "
        f"-X 'github.com/router-for-me/CLIProxyAPI/v6/internal/buildinfo.FrontendRef=local'"
    )
    env = {**os.environ, "CGO_ENABLED": "0", "GOOS": "linux", "GOARCH": "amd64"}
    run_local(
        ["go", "build", f"-ldflags={ldflags}", "-o", str(BINARY), "./cmd/server/"],
        ROOT,
    )
    run_local(
        ["go", "build", "-ldflags=-s -w", "-o", str(UPDATER), "./cmd/updater/"],
        ROOT,
    )


def build_frontend() -> None:
    if not CODEPROXY.is_dir():
        raise SystemExit(f"codeProxy not found: {CODEPROXY}")
    run_local(["bun", "run", "build"], CODEPROXY)
    dist = CODEPROXY / "dist"
    if not dist.is_dir():
        raise SystemExit(f"frontend dist missing: {dist}")
    if PANEL.exists():
        import shutil

        shutil.rmtree(PANEL)
    import shutil

    shutil.copytree(dist, PANEL)


def panel_tarball() -> bytes:
    buf = io.BytesIO()
    with tarfile.open(fileobj=buf, mode="w:gz") as tar:
        for item in PANEL.rglob("*"):
            if item.is_dir():
                continue
            tar.add(item, arcname=str(item.relative_to(PANEL)).replace("\\", "/"))
    buf.seek(0)
    return buf.read()


def run_ssh(client: paramiko.SSHClient, cmd: str, timeout: int = 300) -> tuple[int, str]:
    print(f"\n[remote] {cmd}")
    _, stdout, stderr = client.exec_command(cmd, get_pty=True, timeout=timeout)
    out = stdout.read().decode("utf-8", errors="replace")
    err = stderr.read().decode("utf-8", errors="replace")
    code = stdout.channel.recv_exit_status()
    text = (out + err).strip()
    if text:
        print(text)
    return code, text


def main() -> int:
    if not PASSWORD:
        print("DEPLOY_PASSWORD is required", file=sys.stderr)
        return 1

    skip_build = os.environ.get("DEPLOY_SKIP_BUILD", "").lower() in {"1", "true", "yes"}
    commit = COMMIT or subprocess.check_output(
        ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True
    ).strip()
    build_date = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())

    if not skip_build:
        print("=== 1/3 Local build (backend + panel) ===")
        build_backend(commit, build_date)
        build_frontend()
    else:
        for path in (BINARY, UPDATER, PANEL):
            if not path.exists():
                print(f"missing artifact: {path}", file=sys.stderr)
                return 1

    bin_size = BINARY.stat().st_size / 1024 / 1024
    panel_size = sum(f.stat().st_size for f in PANEL.rglob("*") if f.is_file()) / 1024 / 1024
    print(f"\nArtifacts: binary {bin_size:.1f} MB, panel {panel_size:.1f} MB")

    print("\n=== 2/3 Upload ===")
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(HOST, username=USER, password=PASSWORD, timeout=30)

    run_ssh(client, f"mkdir -p {REMOTE_STAGING}/panel")
    sftp = client.open_sftp()
    sftp.put(str(BINARY), f"{REMOTE_STAGING}/CLIProxyAPI")
    sftp.chmod(f"{REMOTE_STAGING}/CLIProxyAPI", 0o755)
    sftp.put(str(UPDATER), f"{REMOTE_STAGING}/clirelay-updater")
    sftp.chmod(f"{REMOTE_STAGING}/clirelay-updater", 0o755)
    panel_tar = panel_tarball()
    with sftp.file(f"{REMOTE_STAGING}/panel.tar.gz", "wb") as f:
        f.write(panel_tar)
    sftp.close()
    print(f"Uploaded panel.tar.gz ({len(panel_tar)/1024/1024:.1f} MB)")

    print("\n=== 3/3 Hot-swap container ===")
    deploy = f"""
set -e
cd {REMOTE_STAGING}
rm -rf panel && mkdir panel
tar -xzf panel.tar.gz -C panel
docker cp {REMOTE_STAGING}/CLIProxyAPI {CONTAINER}:/CLIProxyAPI/CLIProxyAPI
docker cp {REMOTE_STAGING}/clirelay-updater {CONTAINER}:/CLIProxyAPI/clirelay-updater
docker exec {CONTAINER} sh -c 'rm -rf /CLIProxyAPI/panel/*'
docker cp {REMOTE_STAGING}/panel/. {CONTAINER}:/CLIProxyAPI/panel/
docker exec {CONTAINER} chmod +x /CLIProxyAPI/CLIProxyAPI /CLIProxyAPI/clirelay-updater
docker restart {CONTAINER}
sleep 6
docker ps --filter name={CONTAINER} --format 'status: {{{{.Status}}}} image: {{{{.Image}}}}'
curl -s -o /dev/null -w 'auth-login:%{{http_code}}\\n' -X POST http://127.0.0.1:8317/v0/management/auth/login -H 'Content-Type: application/json' -d '{{"username":"probe","password":"probe"}}'
"""
    code, out = run_ssh(client, deploy, timeout=120)
    client.close()

    if "auth-login:404" in out:
        print("\nWARN: auth/login still 404 — check container logs", file=sys.stderr)
        return 1
    if "auth-login:401" in out or "auth-login:400" in out:
        print("\nOK: new auth endpoint is live (non-404)")
        return 0
    return code


if __name__ == "__main__":
    raise SystemExit(main())
