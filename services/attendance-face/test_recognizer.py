import base64
from pathlib import Path

import cv2
import numpy as np
import pytest
from fastapi.testclient import TestClient

from app import create_app
from models import MODEL_VERSION, checked_model_paths
from recognizer import FaceError, FaceRecognizer, decode_photo, decode_template

TOKEN = "test-only-token-never-for-deployment-123456789"


class FakeEngine:
    model_version = MODEL_VERSION

    def templates(self, photos):
        if photos[0] == "multiple":
            raise FaceError("multiple_faces")
        return ["template"] * 3

    def verify(self, photo, templates, version):
        if version != MODEL_VERSION:
            raise FaceError("model_version_mismatch")
        return 0.75


@pytest.fixture
def client():
    with TestClient(create_app(FakeEngine(), TOKEN)) as result:
        result.headers["Authorization"] = "Bearer " + TOKEN
        yield result


def test_auth_required_and_no_input_leak(client):
    response = client.post("/templates", headers={"Authorization": "Bearer wrong"}, json={"imagesBase64": ["private-photo"] * 3})
    assert response.status_code == 401
    assert "private-photo" not in response.text


def test_empty_secret_fails_closed():
    with pytest.raises(RuntimeError):
        with TestClient(create_app(FakeEngine(), "")):
            pass


def test_three_photos_required_and_validation_does_not_echo(client):
    response = client.post("/templates", json={"imagesBase64": ["private-photo"]})
    assert response.status_code == 422
    assert response.json() == {"code": "invalid_request"}
    assert "private-photo" not in response.text


def test_template_and_verify_contract(client):
    templates = client.post("/templates", json={"imagesBase64": ["ok"] * 3})
    assert templates.status_code == 200
    assert templates.json() == {"templates": ["template"] * 3, "modelVersion": MODEL_VERSION}
    verified = client.post("/verify", json={"imageBase64": "ok", **templates.json()})
    assert verified.json() == {"score": 0.75, "modelVersion": MODEL_VERSION}
    assert verified.headers["cache-control"] == "no-store"


def test_quality_and_model_errors_remain_explicit(client):
    assert client.post("/templates", json={"imagesBase64": ["multiple"] * 3}).json() == {"code": "multiple_faces"}
    response = client.post("/verify", json={"imageBase64": "ok", "templates": ["template"] * 3, "modelVersion": "wrong"})
    assert response.status_code == 422
    assert response.json()["code"] == "model_version_mismatch"


@pytest.mark.parametrize("value", ["not-base64!", base64.b64encode(b"not-jpeg").decode()])
def test_invalid_photo_rejected_before_opencv(value):
    with pytest.raises(FaceError) as error:
        decode_photo(value)
    assert error.value.code == "invalid_photo"


@pytest.mark.parametrize("data", [b"short", np.zeros(128, dtype="<f4").tobytes(), np.full(128, np.nan, dtype="<f4").tobytes()])
def test_malformed_templates_rejected(data):
    with pytest.raises(FaceError) as error:
        decode_template(base64.b64encode(data).decode())
    assert error.value.code == "invalid_template"


def test_model_hash_enforced(tmp_path):
    (tmp_path / "face_detection_yunet_2023mar.onnx").write_bytes(b"tampered")
    with pytest.raises(ValueError, match="model_missing_or_invalid"):
        checked_model_paths(tmp_path)


@pytest.fixture(scope="module")
def real_engine():
    directory = Path(".models")
    if not (directory / "face_recognition_sface_2021dec.onnx").is_file():
        pytest.skip("Run download_models.py for real model verification")
    return FaceRecognizer(directory)


def encode_jpeg(image):
    ok, data = cv2.imencode(".jpg", image)
    assert ok
    return base64.b64encode(data).decode()


def test_real_model_blank_rejected(real_engine):
    with pytest.raises(FaceError) as error:
        real_engine.embedding(encode_jpeg(np.full((480, 640, 3), 127, np.uint8)))
    assert error.value.code == "no_face"


def test_real_model_same_person_and_multiple_faces(real_engine):
    image = cv2.imread(".models/lena.jpg")
    if image is None:
        pytest.skip("Optional OpenCV sample lena.jpg is absent")
    photo = encode_jpeg(image)
    templates = real_engine.templates([photo] * 3)
    assert all(len(base64.b64decode(item)) == 512 for item in templates)
    assert real_engine.verify(photo, templates, MODEL_VERSION) > 0.99
    with pytest.raises(FaceError) as error:
        real_engine.embedding(encode_jpeg(np.concatenate([image, image], axis=1)))
    assert error.value.code == "multiple_faces"
    # 真实模型的人脸数量与特征路径都要执行，不用 mock 代替模型加载成功。
    with pytest.raises(FaceError) as error:
        real_engine.embedding(encode_jpeg(cv2.GaussianBlur(image, (45, 45), 15)))
    assert error.value.code in {"poor_quality", "no_face"}


def test_enrollment_rejects_three_different_people():
    engine = FaceRecognizer.__new__(FaceRecognizer)
    vectors = iter([np.eye(128, dtype="<f4")[0], np.eye(128, dtype="<f4")[1], np.eye(128, dtype="<f4")[0]])
    engine.embedding = lambda _: next(vectors)
    with pytest.raises(FaceError) as error:
        engine.templates(["one", "two", "one"])
    assert error.value.code == "enrollment_faces_inconsistent"
