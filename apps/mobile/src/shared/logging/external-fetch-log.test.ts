import assert from "node:assert/strict";
import { createExternalFetchFailureLogInput } from "./external-fetch-log";

const logInput = createExternalFetchFailureLogInput({
  level: "Error",
  message: "对象存储上传失败",
  sourceType: "storage.upload",
  requestMethod: "PUT",
  requestUrl:
    "https://bucket.example.com/uploads/image.jpg?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=test&token=secret",
  statusCode: 403,
  error: new Error("upload failed"),
  fileUri: "file:///var/mobile/Containers/Data/Application/cache/avatar.png?cache=1",
  properties: {
    objectKey: "uploads/image.jpg",
    uploadUrl:
      "https://bucket.example.com/uploads/image.jpg?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=test&token=secret",
    token: "secret",
    authorizationCode: "auth-code",
  },
});

assert.equal(
  logInput.requestPath,
  "https://bucket.example.com/uploads/image.jpg",
  "外部 fetch 失败日志只能记录脱敏后的 URL"
);
assert.equal(logInput.requestMethod, "PUT", "外部 fetch 失败日志应保留请求方法");
assert.equal(logInput.statusCode, 403, "外部 fetch 失败日志应保留状态码");
assert.equal(logInput.exceptionType, "Error", "外部 fetch 失败日志应保留异常类型");
assert.equal(logInput.exceptionMessage, "upload failed", "外部 fetch 失败日志应保留异常摘要");
assert.deepEqual(
  logInput.properties,
  {
    objectKey: "uploads/image.jpg",
    uploadUrl: "https://bucket.example.com/uploads/image.jpg",
    fileUriTail: "avatar.png",
  },
  "外部 fetch 失败日志属性应只保留安全摘要"
);

const serializedLogInput = JSON.stringify(logInput);
assert.equal(
  serializedLogInput.includes("X-Amz-Credential"),
  false,
  "日志中不能包含签名 URL 查询参数"
);
assert.equal(serializedLogInput.includes("token"), false, "日志中不能包含 token");
assert.equal(serializedLogInput.includes("auth-code"), false, "日志中不能包含授权码");
