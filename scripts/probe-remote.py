import os
import paramiko

PASSWORD = os.environ["DEPLOY_PASSWORD"]
c = paramiko.SSHClient()
c.set_missing_host_key_policy(paramiko.AutoAddPolicy())
c.connect("67.215.253.110", username="root", password=PASSWORD, timeout=30)
cmds = [
    "ls -la /opt/clirelay",
    "docker ps",
    "docker ps -a",
    "find /opt/clirelay -maxdepth 4 -type f | head -60",
    "cat /opt/clirelay/docker-compose.yml 2>/dev/null || true",
]
for cmd in cmds:
    print("===", cmd)
    _, stdout, stderr = c.exec_command(cmd)
    print(stdout.read().decode())
    err = stderr.read().decode()
    if err:
        print("ERR", err)
c.close()
