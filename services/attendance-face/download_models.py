"""下载固定版本模型；先核对摘要，再原子落盘。"""
from pathlib import Path
import argparse
import hashlib
import urllib.request
from models import MODELS, REVISION


def download(directory: Path) -> None:
    directory.mkdir(parents=True, exist_ok=True)
    for filename, (folder, expected) in MODELS.items():
        destination = directory / filename
        if destination.exists() and hashlib.sha256(destination.read_bytes()).hexdigest() == expected:
            continue
        url = f"https://media.githubusercontent.com/media/opencv/opencv_zoo/{REVISION}/models/{folder}/{filename}"
        with urllib.request.urlopen(url, timeout=90) as response:
            data = response.read(50 * 1024 * 1024)
        if hashlib.sha256(data).hexdigest() != expected:
            raise ValueError(f"模型摘要不匹配: {filename}")
        temporary = destination.with_suffix(".download")
        temporary.write_bytes(data)
        temporary.replace(destination)
        print(f"已校验模型: {filename}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--directory", type=Path, default=Path(".models"))
    download(parser.parse_args().directory)
