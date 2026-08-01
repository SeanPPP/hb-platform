import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import {
  cancelAdvertisementVideoCapture,
  cancelAdvertisementVideoCaptureSession,
  captureAdvertisementVideo,
  createAdvertisementVideoCaptureSession,
  createRecordedAdvertisementVideoAsset,
} from "./advertisement-video-capture";

async function main() {
  assert.deepEqual(
    createRecordedAdvertisementVideoAsset("file:///cache/promo.mov", () => 42),
    {
      uri: "file:///cache/promo.mov",
      type: "video",
      fileName: "advertisement-42.mov",
      mimeType: "video/quicktime",
    },
    "iOS 录制文件应保留 QuickTime 类型"
  );

  const pendingStates: boolean[] = [];
  let capturedUri = "";
  await captureAdvertisementVideo({
    record: async () => ({ uri: "file:///cache/promo.mp4" }),
    isCanceled: () => false,
    onCaptured: async (asset) => {
      capturedUri = asset.uri;
    },
    onError: (error) => {
      throw error;
    },
    onPendingChange: (pending) => pendingStates.push(pending),
  });
  assert.equal(capturedUri, "file:///cache/promo.mp4", "录制成功后应交给上传流程");
  assert.deepEqual(pendingStates, [true, false], "录制状态必须在完成后恢复");

  const lifecycleSession = createAdvertisementVideoCaptureSession();
  let lifecycleResultUploaded = false;
  let stoppedAfterCancellation = false;
  await captureAdvertisementVideo({
    record: async () => {
      cancelAdvertisementVideoCapture(lifecycleSession, () => {
        stoppedAfterCancellation = lifecycleSession.canceled;
      });
      // iOS 预览停止可能正常返回一个部分录像文件，此结果仍必须丢弃。
      return { uri: "file:///cache/partial.mov" };
    },
    isCanceled: () => lifecycleSession.canceled,
    onCaptured: async () => {
      lifecycleResultUploaded = true;
    },
    onError: (error) => {
      throw error;
    },
    onPendingChange: () => undefined,
  });
  assert.equal(stoppedAfterCancellation, true, "生命周期取消必须先失效会话再停止录像");
  assert.equal(lifecycleResultUploaded, false, "生命周期停止返回的部分录像不得上传");

  const userStopSession = createAdvertisementVideoCaptureSession();
  let userStopUploaded = false;
  await captureAdvertisementVideo({
    record: async () => ({ uri: "file:///cache/user-stop.mov" }),
    isCanceled: () => userStopSession.canceled,
    onCaptured: async () => {
      userStopUploaded = true;
    },
    onError: (error) => {
      throw error;
    },
    onPendingChange: () => undefined,
  });
  assert.equal(userStopUploaded, true, "用户主动停止未取消会话，录像应继续上传");

  let capturedAfterCancel = false;
  let reportedAfterCancel = false;
  await captureAdvertisementVideo({
    record: async () => {
      throw new Error("camera stopped");
    },
    isCanceled: () => true,
    onCaptured: async () => {
      capturedAfterCancel = true;
    },
    onError: () => {
      reportedAfterCancel = true;
    },
    onPendingChange: () => undefined,
  });
  assert.equal(capturedAfterCancel, false, "取消后不得上传录制结果");
  assert.equal(reportedAfterCancel, false, "取消导致的原生停止错误不得误报失败");

  let reportedError: unknown;
  const captureError = new Error("record failed");
  await captureAdvertisementVideo({
    record: async () => {
      throw captureError;
    },
    isCanceled: () => false,
    onCaptured: async () => undefined,
    onError: (error) => {
      reportedError = error;
    },
    onPendingChange: () => undefined,
  });
  assert.equal(reportedError, captureError, "非取消错误必须回传给 UI");

  let resolveFirstRecording: ((value: { uri: string }) => void) | undefined;
  let resolveSecondRecording: ((value: { uri: string }) => void) | undefined;
  const firstSession = createAdvertisementVideoCaptureSession();
  const activeSessionRef: {
    current: ReturnType<typeof createAdvertisementVideoCaptureSession> | null;
  } = { current: firstSession };
  let recordingPending = false;
  let stopCount = 0;
  const updateRecordingPending = (
    session: ReturnType<typeof createAdvertisementVideoCaptureSession>,
    pending: boolean
  ) => {
    // 与页面相同：只有当前会话可以更新录制状态，旧会话 finally 必须被隔离。
    if (activeSessionRef.current !== session) {
      return;
    }
    recordingPending = pending;
    if (!pending) {
      activeSessionRef.current = null;
    }
  };
  const firstCapture = captureAdvertisementVideo({
    record: () => new Promise((resolve) => {
      resolveFirstRecording = resolve;
    }),
    isCanceled: () => firstSession.canceled,
    onCaptured: async () => undefined,
    onError: (error) => {
      throw error;
    },
    onPendingChange: (pending) => updateRecordingPending(firstSession, pending),
  });
  assert.equal(recordingPending, true, "首次录制必须进入 pending");

  cancelAdvertisementVideoCaptureSession(
    activeSessionRef,
    () => {
      stopCount += 1;
    },
    () => {
      recordingPending = false;
    }
  );
  assert.equal(activeSessionRef.current, null, "取消必须同步释放当前会话");
  assert.equal(recordingPending, false, "取消必须同步复位录制状态");

  const secondSession = createAdvertisementVideoCaptureSession();
  activeSessionRef.current = secondSession;
  const secondCapture = captureAdvertisementVideo({
    record: () => new Promise((resolve) => {
      resolveSecondRecording = resolve;
    }),
    isCanceled: () => secondSession.canceled,
    onCaptured: async () => undefined,
    onError: (error) => {
      throw error;
    },
    onPendingChange: (pending) => updateRecordingPending(secondSession, pending),
  });
  assert.equal(recordingPending, true, "重开后必须允许再次录制");

  resolveFirstRecording?.({ uri: "file:///cache/canceled-first.mov" });
  await firstCapture;
  assert.equal(recordingPending, true, "旧会话 finally 不得覆盖重开的录制状态");
  assert.equal(activeSessionRef.current, secondSession, "旧会话不得清除新会话");

  resolveSecondRecording?.({ uri: "file:///cache/second.mov" });
  await secondCapture;
  assert.equal(recordingPending, false, "第二次录制完成后必须恢复空闲状态");
  assert.equal(activeSessionRef.current, null, "第二次录制完成后必须释放会话");
  assert.equal(stopCount, 1, "取消首次录制时只应停止一次原生录像");

  const screenSource = readFileSync(join(__dirname, "advertisements-screen.tsx"), "utf8");
  assert.match(
    screenSource,
    /<CameraView[\s\S]*?mode="video"[\s\S]*?mute/,
    "现场广告录像必须由静音 CameraView 承载"
  );
  assert.match(
    screenSource,
    /recordAsync\(\{\s*maxDuration:\s*30\s*\}\)/,
    "现场广告录像最长必须为 30 秒"
  );
  assert.doesNotMatch(
    screenSource,
    /recordAsync\(\{[^}]*mute/s,
    "mute 是 CameraView 属性，不得误传给 recordAsync"
  );
  assert.doesNotMatch(
    screenSource,
    /requestMicrophonePermissions|NSMicrophoneUsageDescription/,
    "广告录像不得申请麦克风权限"
  );
  assert.match(screenSource, /AppState\.addEventListener\(\s*"change"/, "App 进入后台必须监听生命周期");
  assert.match(screenSource, /useLayoutEffect\(\(\) => \{/, "组件卸载前必须同步取消原生录像");
  assert.match(screenSource, /useFocusEffect\(/, "离开广告路由但组件未卸载时也必须取消录像");
  assert.ok(
    (screenSource.match(/cancelAdvertisementVideoCaptureSession\(/g) ?? []).length >= 3,
    "关闭、生命周期退出和相机错误必须统一走同步会话取消状态机"
  );

  console.log("advertisement-video-capture.test.ts: ok");
}

void main();
