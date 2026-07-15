import assert from "node:assert/strict";
import {
  EmployeeProfileImageProcessingError,
  calculateAvatarCrop,
  processEmployeeProfileImage,
} from "./image-processing";

async function main() {
assert.deepEqual(
  calculateAvatarCrop(1600, 900),
  { originX: 350, originY: 0, width: 900, height: 900 },
  "横图头像应从水平方向居中裁成正方形"
);
assert.deepEqual(
  calculateAvatarCrop(900, 1600),
  { originX: 0, originY: 350, width: 900, height: 900 },
  "竖图头像应从垂直方向居中裁成正方形"
);
assert.deepEqual(
  calculateAvatarCrop(900, 900),
  { originX: 0, originY: 0, width: 900, height: 900 },
  "正方形头像不应发生偏移"
);

const identityActions: unknown[][] = [];
const identity = await processEmployeeProfileImage(
  { kind: "identityPhoto", uri: "file://identity.heic", width: 3000, height: 4000 },
  {
    manipulate: async (_uri, actions, options) => {
      identityActions.push(actions);
      return { uri: "file://identity.jpg", width: 1536, height: 2048 };
    },
    readBlobSize: async () => 1024,
  }
);
assert.equal(identity?.contentType, "image/jpeg", "HEIC 等来源应统一转成 JPEG");
assert.deepEqual(identityActions[0], [{ resize: { height: 2048 } }], "竖版证件照应按长边缩放且保持完整比例");

const avatarAttempts: Array<{ actions: unknown[]; compress: number }> = [];
const sizes = [6 * 1024 * 1024, 5.5 * 1024 * 1024, 4 * 1024 * 1024];
const compressed = await processEmployeeProfileImage(
  { kind: "avatar", uri: "file://large.jpg", width: 2400, height: 1600 },
  {
    manipulate: async (_uri, actions, options) => {
      avatarAttempts.push({ actions, compress: options.compress });
      return { uri: `file://avatar-${avatarAttempts.length}.jpg`, width: 640, height: 640 };
    },
    readBlobSize: async () => sizes.shift() ?? 0,
  }
);
assert.equal(avatarAttempts.length, 3, "头像超限时应依次尝试三个压缩档位");
assert.deepEqual(
  avatarAttempts.map((attempt) => attempt.compress),
  [0.82, 0.72, 0.6],
  "头像压缩质量应按方案逐级下降"
);
assert.equal(compressed?.fileSize, 4 * 1024 * 1024, "仅返回不超过 5 MiB 的最终图片");
assert.equal(compressed?.uri, "file://avatar-3.jpg", "预览应使用实际上传的最终处理图片");

await assert.rejects(
  () =>
    processEmployeeProfileImage(
      { kind: "identityPhoto", uri: "file://huge.jpg", width: 5000, height: 4000 },
      {
        manipulate: async () => ({ uri: "file://still-huge.jpg", width: 1280, height: 1024 }),
        readBlobSize: async () => 6 * 1024 * 1024,
      }
    ),
  (error: unknown) =>
    error instanceof EmployeeProfileImageProcessingError && error.code === "file_too_large",
  "最低档仍超限时应停止处理"
);

await assert.rejects(
  () =>
    processEmployeeProfileImage(
      { kind: "avatar", uri: "file://broken.jpg", width: 100, height: 100 },
      {
        manipulate: async () => {
          throw new Error("decode failed");
        },
        readBlobSize: async () => 0,
      }
    ),
  (error: unknown) =>
    error instanceof EmployeeProfileImageProcessingError && error.code === "decode_failed",
  "无法解码图片时应返回可识别错误"
);

await assert.rejects(
  () =>
    processEmployeeProfileImage(
      { kind: "avatar", uri: "file://blob-failed.jpg", width: 100, height: 100 },
      {
        manipulate: async () => ({ uri: "file://processed.jpg", width: 100, height: 100 }),
        readBlobSize: async () => {
          throw new Error("blob failed");
        },
      }
    ),
  (error: unknown) =>
    error instanceof EmployeeProfileImageProcessingError && error.code === "blob_read_failed",
  "Blob 读取失败时应返回可识别错误"
);

assert.equal(await processEmployeeProfileImage(null), null, "用户取消选择时应静默返回");

console.log("image-processing.test.ts: ok");
}

void main();
