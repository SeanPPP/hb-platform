"""官方模型及其内容摘要固定，禁止运行时静默更换识别模型。"""
from pathlib import Path
import hashlib

REVISION = "47534e27c9851bb1128ccc0102f1145e27f23f98"
MODEL_VERSION = "yunet-2023mar-sface-2021dec-v1"
MODELS = {
    "face_detection_yunet_2023mar.onnx": (
        "face_detection_yunet", "8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4"
    ),
    "face_recognition_sface_2021dec.onnx": (
        "face_recognition_sface", "0ba9fbfa01b5270c96627c4ef784da859931e02f04419c829e83484087c34e79"
    ),
}


def checked_model_paths(directory: Path) -> list[str]:
    paths = []
    for filename, (_, expected) in MODELS.items():
        path = directory / filename
        if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            raise ValueError("model_missing_or_invalid")
        paths.append(str(path))
    return paths
