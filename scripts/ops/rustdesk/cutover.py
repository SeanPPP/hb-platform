"""在 .vip 服务器使用已核验备份切换 RustDesk；不会删除通讯录数据。"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import sqlite3
import subprocess
import time


def run(args):
    subprocess.run(args, check=True)


def inspect(*names):
    return json.loads(subprocess.check_output(["docker", "inspect", *names]))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--backup", required=True)
    parser.add_argument("--compose", required=True)
    args = parser.parse_args()
    os.umask(0o077)
    base = Path("/www/rustdesk-server")
    data = base / "data"
    backup = Path(args.backup)
    incoming = Path(args.compose)
    assert os.geteuid() == 0
    assert backup.parent == Path("/www/backups") and backup.resolve() == backup
    assert base.resolve() == base and data.resolve() == data
    assert (backup / "COMPLETE").is_file() and not list(data.iterdir())
    for relative, digest in json.loads((backup / "sha256-stable.json").read_text()).items():
        assert hashlib.sha256((backup / relative).read_bytes()).hexdigest() == digest
    baseline = json.loads((backup / "containers.json").read_text())
    assert inspect("rustdesk-server")[0]["Id"] == baseline[0]["Id"]
    run(["docker", "compose", "-f", str(incoming), "config", "--quiet"])
    final = backup / "hbbs-final"
    final.mkdir()
    snapshot = backup / "hbbs"
    rollback = {
        "name": "rustdesk-server",
        "services": {"rustdesk-server": {
            "image": baseline[0]["Image"], "container_name": "rustdesk-server",
            "command": ["hbbs"], "restart": "unless-stopped", "working_dir": "/root",
            "volumes": [str(data) + ":/root"],
            "ports": [f"{p}:{p}/tcp" for p in [21115, 21116, 21117, 21118]],
        }},
    }
    (backup / "rollback-compose.json").write_text(json.dumps(rollback, indent=2))
    try:
        run(["docker", "stop", "--time", "20", "rustdesk-server"])
        run(["docker", "cp", "rustdesk-server:/root/.", str(final)])
        connection = sqlite3.connect(final / "db_v2.sqlite3")
        try:
            assert connection.execute("PRAGMA integrity_check").fetchone()[0] == "ok"
            assert connection.execute("PRAGMA wal_checkpoint(TRUNCATE)").fetchone()[0] == 0
        finally:
            connection.close()
        for key in ["id_ed25519", "id_ed25519.pub"]:
            assert (final / key).read_bytes() == (snapshot / key).read_bytes()
        snapshot = final
        # 原目录为空；先保护新持久目录，再将身份文件复制进去。
        os.chmod(data, 0o700)
        for name in ["id_ed25519", "id_ed25519.pub", "db_v2.sqlite3"]:
            shutil.copy2(snapshot / name, data / name)
        shutil.copy2(incoming, base / "docker-compose.yml")
        run(["docker", "compose", "-f", str(base / "docker-compose.yml"), "up",
             "-d", "--no-build", "--pull", "never", "rustdesk-server", "rustdesk-relay"])
        ready = False
        for _ in range(15):
            ready = True
            for container in inspect("rustdesk-server", "rustdesk-relay"):
                if container["State"]["Status"] != "running":
                    ready = False
                    continue
                sockets = subprocess.check_output([
                    "nsenter", "-t", str(container["State"]["Pid"]), "-n", "ss", "-lntu"
                ], text=True)
                signal = container["Name"] == "/rustdesk-server"
                ports = ["21115", "21116"] if signal else ["21117"]
                ready &= all(":" + port in sockets for port in ports)
                if signal:
                    ready &= any("udp" in line and ":21116" in line for line in sockets.splitlines())
            if ready:
                break
            time.sleep(1)
        assert ready, "RustDesk listeners did not become ready"
        assert (data / "id_ed25519.pub").read_bytes() == (snapshot / "id_ed25519.pub").read_bytes()
        (backup / "SERVER_CUTOVER_OK").write_text("1.1.16; identity and native TCP/UDP listeners verified.\n")
        print(json.dumps({"cutover": "ok", "identityPreserved": True, "backup": str(backup)}))
    except Exception:
        # 失败数据也保留，回滚仅重建本项目，不清理其他容器或卷。
        for name in ["rustdesk-server", "rustdesk-relay"]:
            subprocess.run(["docker", "stop", name], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        shutil.move(str(data), str(backup / "failed-deployment-data"))
        data.mkdir(mode=0o700)
        for name in ["id_ed25519", "id_ed25519.pub", "db_v2.sqlite3"]:
            shutil.copy2(snapshot / name, data / name)
        shutil.copy2(backup / "rollback-compose.json", base / "docker-compose.yml")
        run(["docker", "compose", "-f", str(base / "docker-compose.yml"), "up",
             "-d", "--no-build", "--pull", "never", "rustdesk-server"])
        raise


if __name__ == "__main__":
    main()
