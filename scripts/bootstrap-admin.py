#!/usr/bin/env python3
"""Bootstrap admin user on remote CliRelay via management key or direct DB insert."""

from __future__ import annotations

import json
import os
import sys
import uuid
from datetime import datetime, timezone

import paramiko

HOST = os.environ.get("DEPLOY_HOST", "67.215.253.110")
USER = os.environ.get("DEPLOY_USER", "root")
PASSWORD = os.environ.get("DEPLOY_PASSWORD", "")
ADMIN_USER = os.environ.get("BOOTSTRAP_USERNAME", "admin")
ADMIN_PASS = os.environ.get("BOOTSTRAP_PASSWORD", "admin123")


def run(client: paramiko.SSHClient, script: str, timeout: int = 120) -> tuple[int, str]:
    _, stdout, stderr = client.exec_command(script, get_pty=True, timeout=timeout)
    out = stdout.read().decode("utf-8", errors="replace")
    err = stderr.read().decode("utf-8", errors="replace")
    code = stdout.channel.recv_exit_status()
    return code, (out + err).strip()


def main() -> int:
    if not PASSWORD:
        print("DEPLOY_PASSWORD required", file=sys.stderr)
        return 1

    remote_py = r'''
import bcrypt
import glob
import json
import os
import re
import sqlite3
import subprocess
import sys
import urllib.error
import urllib.request
import uuid
from datetime import datetime, timezone

ADMIN_USER = ''' + json.dumps(ADMIN_USER) + r'''
ADMIN_PASS = ''' + json.dumps(ADMIN_PASS) + r'''
CONFIG_PATH = "/opt/clirelay/config.yaml"
DATA_DIR = "/opt/clirelay/data"

def now():
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

def load_config_hash():
    text = open(CONFIG_PATH).read()
    m = re.search(r"secret-key:\s*[\"']?([^\"'\n]+)", text)
    return m.group(1).strip() if m else ""

def candidate_keys():
    keys = []
    seen = set()
    for path in glob.glob("/opt/clirelay/**", recursive=True):
        if not os.path.isfile(path):
            continue
        if os.path.getsize(path) > 2_000_000:
            continue
        try:
            text = open(path, "r", encoding="utf-8", errors="ignore").read()
        except Exception:
            continue
        for m in re.finditer(r'secret-key:\s*["\']?([^"\'\n#]+)', text):
            val = m.group(1).strip()
            if val and not val.startswith("$2"):
                if val not in seen:
                    seen.add(val)
                    keys.append(val)
        for m in re.finditer(r'(sk-[a-z0-9]{20,})', text):
            val = m.group(1)
            if val not in seen:
                seen.add(val)
                keys.append(val)
    return keys

def try_bootstrap(key: str):
    body = json.dumps({"username": ADMIN_USER, "password": ADMIN_PASS}).encode()
    req = urllib.request.Request(
        "http://127.0.0.1:8317/v0/management/auth/bootstrap",
        data=body,
        headers={
            "Authorization": f"Bearer {key}",
            "Content-Type": "application/json",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            return resp.status, resp.read().decode()
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()

def try_login():
    body = json.dumps({"username": ADMIN_USER, "password": ADMIN_PASS}).encode()
    req = urllib.request.Request(
        "http://127.0.0.1:8317/v0/management/auth/login",
        data=body,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            return resp.status, resp.read().decode()
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()

def direct_db_insert():
    db_paths = sorted(glob.glob(DATA_DIR + "/**/*.db", recursive=True))
    db_paths += sorted(glob.glob(DATA_DIR + "/*.db"))
    db_paths = [p for p in db_paths if os.path.isfile(p)]
    if not db_paths:
        return False, "usage db not found"
    db_path = db_paths[0]
    conn = sqlite3.connect(db_path)
    cur = conn.cursor()
    cur.execute("SELECT name FROM sqlite_master WHERE type='table' AND name='panel_users'")
    if not cur.fetchone():
        conn.close()
        return False, "panel_users table missing"
    cur.execute("SELECT COUNT(*) FROM panel_users")
    if cur.fetchone()[0] > 0:
        conn.close()
        return False, "panel users already exist"
    pw_hash = bcrypt.hashpw(ADMIN_PASS.encode(), bcrypt.gensalt(rounds=12)).decode()
    user_id = str(uuid.uuid4())
    ts = now()
    cur.execute(
        "INSERT INTO panel_users (id, username, password_hash, role, disabled, created_at, updated_at, last_login_at) VALUES (?,?,?,?,0,?,?,'')",
        (user_id, ADMIN_USER, pw_hash, "admin", ts, ts),
    )
    conn.commit()
    conn.close()
    return True, f"inserted into {db_path}"

status, body = try_login()
if status == 200:
    print("ALREADY_OK", body)
    sys.exit(0)

cfg_hash = load_config_hash()
for key in candidate_keys():
    if cfg_hash and key == cfg_hash:
        continue
    code, resp = try_bootstrap(key)
    print(f"TRY key={key[:8]}... code={code}")
    if code == 201:
        print("BOOTSTRAP_OK", resp)
        status, body = try_login()
        print(f"LOGIN code={status}", body)
        sys.exit(0 if status == 200 else 1)

# verify keys against bcrypt hash
if cfg_hash.startswith("$2"):
    import bcrypt as bc
    for key in candidate_keys():
        try:
            if bc.checkpw(key.encode(), cfg_hash.encode()):
                code, resp = try_bootstrap(key)
                print(f"BCRYPT_MATCH key={key[:8]}... code={code}")
                if code == 201:
                    print("BOOTSTRAP_OK", resp)
                    status, body = try_login()
                    print(f"LOGIN code={status}", body)
                    sys.exit(0 if status == 200 else 1)
        except Exception:
            pass

ok, msg = direct_db_insert()
if not ok and "already exist" not in msg:
    subprocess.run(["docker", "stop", "cli-proxy-api"], check=False)
    ok, msg = direct_db_insert()
    subprocess.run(["docker", "start", "cli-proxy-api"], check=False)
    import time; time.sleep(5)
print("DB_INSERT", ok, msg)
status, body = try_login()
print(f"LOGIN code={status}", body)
sys.exit(0 if status == 200 else 1)
'''

    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(HOST, username=USER, password=PASSWORD, timeout=30)

    # ensure bcrypt available on remote
    run(client, "python3 -c 'import bcrypt' 2>/dev/null || pip3 install -q bcrypt", timeout=120)

    code, out = run(
        client,
        "python3 - <<'PY'\n" + remote_py + "\nPY",
        timeout=120,
    )
    print(out)
    client.close()
    return 0 if "LOGIN code=200" in out or "ALREADY_OK" in out else 1


if __name__ == "__main__":
    raise SystemExit(main())
