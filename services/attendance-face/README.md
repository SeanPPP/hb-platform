# iPad 人脸考勤私有核验服务

本服务只在自有服务器比较“现场照片 ↔ 所选员工的三份模板”，不做人群搜索，不提供活体动作检测。iPad 不下载模型或模板。中心后端先加密持久化原始事件/照片，再通过本服务核验；本服务不保存请求照片及模板。

## 固定模型

- 来源：OpenCV Zoo commit `47534e27c9851bb1128ccc0102f1145e27f23f98`。
- `face_detection_yunet_2023mar.onnx`，SHA-256 `8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4`（权威值以 `models.py` 为准）。
- `face_recognition_sface_2021dec.onnx`，SHA-256 `0ba9fbfa01b5270c96627c4ef784da859931e02f04419c829e83484087c34e79`。
- 契约版本：`yunet-2023mar-sface-2021dec-v1`；模型加载时核对完整摘要，版本不一致拒绝处理，不静默切换。
- YuNet/SFace 原许可证随镜像放在 `licenses/`，来源为同一固定 commit 的模型目录。参考 [OpenCV 1:1 示例](https://docs.opencv.org/4.13.0/d0/dd4/tutorial_dnn_face.html)。

每张 JPEG 最多2MiB，解码前限制像素数，必须只有一张清晰人脸；录入的三张照片两两相似度也必须达到0.50。模板是128维 float32 单位向量。中心后端可配置比对阈值 `FaceRecognition:Threshold`，默认0.50；实际门店样本校准属于发布前验收，不应把公共样本的自匹配测试当作准确率验收。

## 本地开发

```sh
python3 -m venv .venv
.venv/bin/pip install -r requirements-test.txt
.venv/bin/python download_models.py --directory .models
.venv/bin/python -m pytest -q
```

运行服务前通过环境提供 `ATTENDANCE_FACE_WORKER_TOKEN`（至少32字符；不要提交或写入日志）。然后运行 `.venv/bin/uvicorn app:app --host 127.0.0.1 --port 8092 --no-access-log`。也可使用本目录 `docker compose up --build -d`；compose 仅绑定宿主回环地址。中心后端在另一容器或服务器时，通过受控的 HTTPS 地址访问，禁止将 worker 的原始 HTTP 端口直接暴露到外部网络。

所有端点都需要 worker bearer，包含 `GET /health/ready`；健康检查成功代表模型和摘要加载完成。`POST /templates` 接收 `imagesBase64` 三张照片，返回 `templates` 和 `modelVersion`。`POST /verify` 接收 `imageBase64`、`templates`、`modelVersion`，返回 `score` 和 `modelVersion`。不合格照片返回422及固定错误码；临时不可用由中心保留队列重试。

## 交付与运维

完整部署顺序、数据库扩展、权限、照片保留期、观察查询、回退及真机验收见 [发布说明](../../docs/attendance-face-rollout.md)。本目录配置是可审查的发布材料，不代表生产服务已经启用。
