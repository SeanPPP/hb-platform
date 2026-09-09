from pathlib import Path
import base64
import binascii
import struct
import threading

import cv2
import numpy as np

from models import MODEL_VERSION, checked_model_paths

MAX_PHOTO_BYTES = 2 * 1024 * 1024
MAX_PHOTO_BASE64 = ((MAX_PHOTO_BYTES + 2) // 3) * 4


class FaceError(Exception):
    def __init__(self, code: str):
        self.code = code


def jpeg_dimensions(data: bytes) -> tuple[int, int]:
    """解码前检查 JPEG 的像素边界，避免小文件触发巨幅图像分配。"""
    if len(data) < 4 or data[:2] != b"\xff\xd8":
        raise FaceError("invalid_photo")
    offset = 2
    while offset + 4 <= len(data):
        if data[offset] != 0xFF:
            raise FaceError("invalid_photo")
        while offset < len(data) and data[offset] == 0xFF:
            offset += 1
        if offset >= len(data):
            break
        marker = data[offset]
        offset += 1
        if marker in (0xD9, 0xDA):
            break
        if marker in (0x01, *range(0xD0, 0xD8)):
            continue
        if offset + 2 > len(data):
            break
        length = struct.unpack_from(">H", data, offset)[0]
        if length < 2 or offset + length > len(data):
            raise FaceError("invalid_photo")
        if marker in (0xC0, 0xC1, 0xC2):
            if length < 8:
                raise FaceError("invalid_photo")
            height, width = struct.unpack_from(">HH", data, offset + 3)
            if min(height, width) < 100 or max(height, width) > 8192 or height * width > 16_000_000:
                raise FaceError("invalid_photo_dimensions")
            return width, height
        offset += length
    raise FaceError("invalid_photo")


def decode_photo(encoded: str) -> np.ndarray:
    if len(encoded) > MAX_PHOTO_BASE64:
        raise FaceError("photo_too_large")
    try:
        data = base64.b64decode(encoded, validate=True)
    except (ValueError, binascii.Error):
        raise FaceError("invalid_photo") from None
    if len(data) > MAX_PHOTO_BYTES:
        raise FaceError("photo_too_large")
    jpeg_dimensions(data)
    image = cv2.imdecode(np.frombuffer(data, np.uint8), cv2.IMREAD_COLOR)
    if image is None:
        raise FaceError("invalid_photo")
    height, width = image.shape[:2]
    if max(height, width) > 1280:
        scale = 1280 / max(height, width)
        image = cv2.resize(image, (round(width * scale), round(height * scale)))
    return image


def decode_template(encoded: str) -> np.ndarray:
    try:
        raw = base64.b64decode(encoded, validate=True)
    except (ValueError, binascii.Error):
        raise FaceError("invalid_template") from None
    if len(raw) != 128 * 4:
        raise FaceError("invalid_template")
    vector = np.frombuffer(raw, dtype="<f4").copy()
    if not np.isfinite(vector).all() or np.linalg.norm(vector) < 1e-6:
        raise FaceError("invalid_template")
    return vector / np.linalg.norm(vector)


class FaceRecognizer:
    model_version = MODEL_VERSION

    def __init__(self, directory: Path):
        detection, recognition = checked_model_paths(directory)
        self.detector = cv2.FaceDetectorYN.create(detection, "", (320, 320), 0.9, 0.3, 5000)
        self.recognizer = cv2.FaceRecognizerSF.create(recognition, "")
        self.lock = threading.Lock()

    def embedding(self, encoded: str) -> np.ndarray:
        image = decode_photo(encoded)
        # OpenCV DNN 实例包含可变推理状态，串行使用以避免并发污染。
        with self.lock:
            self.detector.setInputSize((image.shape[1], image.shape[0]))
            _, faces = self.detector.detect(image)
            if faces is None or len(faces) == 0:
                raise FaceError("no_face")
            if len(faces) != 1:
                raise FaceError("multiple_faces")
            face = faces[0]
            if min(face[2], face[3]) < 80:
                raise FaceError("poor_quality")
            aligned = self.recognizer.alignCrop(image, face)
            gray = cv2.cvtColor(aligned, cv2.COLOR_BGR2GRAY)
            if cv2.Laplacian(gray, cv2.CV_64F).var() < 35 or not 25 <= float(gray.mean()) <= 230:
                raise FaceError("poor_quality")
            vector = self.recognizer.feature(aligned).reshape(-1).astype("<f4")
        if vector.size != 128 or not np.isfinite(vector).all() or np.linalg.norm(vector) < 1e-6:
            raise FaceError("invalid_embedding")
        return vector / np.linalg.norm(vector)

    def templates(self, images: list[str]) -> list[str]:
        vectors = [self.embedding(photo) for photo in images]
        # 三张录入照片必须属于同一人，避免把多人模板绑定到一个员工。
        if any(float(np.dot(vectors[i], vectors[j])) < 0.50 for i in range(len(vectors)) for j in range(i)):
            raise FaceError("enrollment_faces_inconsistent")
        return [base64.b64encode(vector.astype("<f4").tobytes()).decode("ascii") for vector in vectors]

    def verify(self, image: str, templates: list[str], model_version: str) -> float:
        if model_version != self.model_version:
            raise FaceError("model_version_mismatch")
        vectors = [decode_template(template) for template in templates]
        captured = self.embedding(image)
        return float(np.clip(max(np.dot(captured, vector) for vector in vectors), -1, 1))
