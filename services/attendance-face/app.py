from contextlib import asynccontextmanager
from pathlib import Path
import hmac
import os

from fastapi import Depends, FastAPI, Header, HTTPException
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from pydantic import BaseModel, ConfigDict, Field

from recognizer import FaceError, FaceRecognizer, MAX_PHOTO_BASE64


class TemplateRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    imagesBase64: list[str] = Field(min_length=3, max_length=3)


class VerifyRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    imageBase64: str = Field(min_length=1, max_length=MAX_PHOTO_BASE64)
    templates: list[str] = Field(min_length=3, max_length=3)
    modelVersion: str = Field(min_length=1, max_length=80)


def create_app(engine=None, token: str | None = None) -> FastAPI:
    secret = token if token is not None else os.environ.get("ATTENDANCE_FACE_WORKER_TOKEN", "")

    @asynccontextmanager
    async def lifespan(application):
        if len(secret) < 32:
            raise RuntimeError("ATTENDANCE_FACE_WORKER_TOKEN must contain at least 32 characters")
        if application.state.engine is None:
            application.state.engine = FaceRecognizer(Path(os.environ.get("ATTENDANCE_FACE_MODEL_DIR", ".models")))
        yield

    application = FastAPI(lifespan=lifespan, docs_url=None, redoc_url=None, openapi_url=None)
    application.state.engine = engine

    @application.middleware("http")
    async def protect_request(request, call_next):
        authorization = request.headers.get("authorization", "")
        if len(secret) < 32 or not hmac.compare_digest(authorization, "Bearer " + secret):
            return JSONResponse({"code": "unauthorized"}, status_code=401)
        # 内部接口同样限制流式请求，不能只相信 Content-Length。
        size = 0
        chunks = []
        async for chunk in request.stream():
            size += len(chunk)
            if size > 9 * 1024 * 1024:
                return JSONResponse({"code": "payload_too_large"}, status_code=413)
            chunks.append(chunk)
        request._body = b"".join(chunks)
        response = await call_next(request)
        response.headers["Cache-Control"] = "no-store"
        return response

    # Pydantic 默认错误会附带原始输入，必须去掉照片和模板内容。
    @application.exception_handler(RequestValidationError)
    async def invalid_request(_request, _exception):
        return JSONResponse({"code": "invalid_request"}, status_code=422)

    @application.exception_handler(FaceError)
    async def face_error(_request, exception):
        return JSONResponse({"code": exception.code}, status_code=422)

    def get_engine():
        if application.state.engine is None:
            raise HTTPException(503, detail="model_unavailable")
        return application.state.engine

    @application.get("/health/ready")
    def ready(recognizer=Depends(get_engine)):
        return {"ready": True, "modelVersion": recognizer.model_version}

    @application.post("/templates")
    def templates(request: TemplateRequest, recognizer=Depends(get_engine)):
        return {"templates": recognizer.templates(request.imagesBase64), "modelVersion": recognizer.model_version}

    @application.post("/verify")
    def verify(request: VerifyRequest, recognizer=Depends(get_engine)):
        return {"score": recognizer.verify(request.imageBase64, request.templates, request.modelVersion),
                "modelVersion": recognizer.model_version}

    return application


app = create_app()
