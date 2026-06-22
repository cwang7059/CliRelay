#!/usr/bin/env python3
import os
import sys

import paramiko

PASSWORD = os.environ.get("DEPLOY_PASSWORD", "")
if not PASSWORD:
    print("DEPLOY_PASSWORD required", file=sys.stderr)
    raise SystemExit(1)

c = paramiko.SSHClient()
c.set_missing_host_key_policy(paramiko.AutoAddPolicy())
c.connect("67.215.253.110", username="root", password=PASSWORD, timeout=30)

cmds = [
    "ls -lh /opt/clirelay/clirelay-src-da69b587.tar.gz 2>/dev/null || echo 'tar: missing'",
    "test -f /opt/clirelay/custom-src/internal/api/handlers/management/auth.go && echo 'source: NEW (has auth.go)' || echo 'source: OLD (no auth.go)'",
    "test -d /opt/clirelay/custom-src.bak && echo 'backup: exists' || echo 'backup: none'",
    "wc -c /opt/clirelay/clirelay-src-da69b587.tar.gz 2>/dev/null || true",
    "tail -20 /opt/clirelay/docker-build-da69b587.log 2>/dev/null || echo 'build-log: none'",
    "ps aux | grep 'docker build' | grep -v grep || echo 'build-process: not running'",
    "docker images --format '{{.Repository}}:{{.Tag}} {{.Size}}' | grep clirelay || true",
    "grep 'image:' /opt/clirelay/docker-compose.yml",
    "docker ps --filter name=cli-proxy-api --format 'container: {{.Image}} | {{.Status}}'",
    """curl -s -o /dev/null -w 'auth-login:%{http_code}' -X POST http://127.0.0.1:8317/v0/management/auth/login -H 'Content-Type: application/json' -d '{"username":"x","password":"x"}'""",
]

for cmd in cmds:
    print(f"--- {cmd.split(chr(10))[0][:70]}")
    _, o, e = c.exec_command(cmd)
    out = o.read().decode().strip()
    err = e.read().decode().strip()
    if out:
        print(out)
    if err:
        print("ERR:", err)

c.close()
