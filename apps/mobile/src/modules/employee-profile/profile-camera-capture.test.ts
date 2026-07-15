import assert from "node:assert/strict";
import { captureProfilePhoto, createProfileCameraCaptureLock } from "./profile-camera-capture";

async function main() {
  const captureError = new Error("camera failed");
  let reportedError: unknown;
  let capturedCount = 0;
  await captureProfilePhoto({
    takePicture: async () => {
      throw captureError;
    },
    onCaptured: async () => {
      capturedCount += 1;
    },
    onError: (error) => {
      reportedError = error;
    },
  });
  assert.equal(reportedError, captureError, "相机 Promise 异常必须交给 onError");
  assert.equal(capturedCount, 0, "拍摄失败后不得进入保存回调");

  const saveError = new Error("save failed");
  reportedError = undefined;
  await captureProfilePhoto({
    takePicture: async () => ({ uri: "file://photo.jpg", width: 100, height: 200 }),
    onCaptured: async () => {
      throw saveError;
    },
    onError: (error) => {
      reportedError = error;
    },
  });
  assert.equal(reportedError, saveError, "保存回调 Promise 异常必须交给 onError");

  let savedUri = "";
  reportedError = undefined;
  await captureProfilePhoto({
    takePicture: async () => ({ uri: "file://ok.jpg", width: 0, height: 0 }),
    onCaptured: async (image) => {
      savedUri = image.uri;
      assert.equal(image.width, 1, "缺失宽度应使用安全最小值");
      assert.equal(image.height, 1, "缺失高度应使用安全最小值");
    },
    onError: (error) => {
      reportedError = error;
    },
  });
  assert.equal(savedUri, "file://ok.jpg", "拍摄成功应调用保存回调");
  assert.equal(reportedError, undefined, "成功路径不应报告错误");

  const captureLock = createProfileCameraCaptureLock();
  const pendingStates: boolean[] = [];
  let cameraCalls = 0;
  let releaseCapture: ((picture: { uri: string; width: number; height: number }) => void) | undefined;
  const takePicture = () => {
    cameraCalls += 1;
    return new Promise<{ uri: string; width: number; height: number }>((resolve) => {
      releaseCapture = resolve;
    });
  };
  const guardedOptions = {
    captureLock,
    onPendingChange: (pending: boolean) => pendingStates.push(pending),
    takePicture,
    onCaptured: async () => undefined,
    onError: (error: unknown) => {
      reportedError = error;
    },
  };
  const firstCapture = captureProfilePhoto(guardedOptions);
  const secondCapture = captureProfilePhoto(guardedOptions);
  assert.equal(cameraCalls, 1, "快速连点时第二次拍摄不得调用相机");
  releaseCapture?.({ uri: "file://guarded.jpg", width: 100, height: 100 });
  await Promise.all([firstCapture, secondCapture]);
  assert.deepEqual(pendingStates, [true, false], "拍摄锁应同步进入并在完成后释放");

  console.log("profile-camera-capture.test.ts: ok");
}

void main();
