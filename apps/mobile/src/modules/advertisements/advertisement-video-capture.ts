export interface RecordedAdvertisementVideoAsset {
  uri: string;
  type: "video";
  fileName: string;
  mimeType: "video/mp4" | "video/quicktime";
}

export interface AdvertisementVideoCaptureSession {
  canceled: boolean;
}

interface CaptureAdvertisementVideoOptions {
  record: () => Promise<{ uri: string } | undefined>;
  isCanceled: () => boolean;
  onCaptured: (asset: RecordedAdvertisementVideoAsset) => void | Promise<void>;
  onError: (error: unknown) => void;
  onPendingChange: (pending: boolean) => void;
  now?: () => number;
}

export function createAdvertisementVideoCaptureSession(): AdvertisementVideoCaptureSession {
  return { canceled: false };
}

export function cancelAdvertisementVideoCapture(
  session: AdvertisementVideoCaptureSession | null,
  stopRecording: () => void
) {
  if (session) {
    // 必须先失效会话，再停止原生录像；部分文件正常返回时也不得进入上传。
    session.canceled = true;
  }
  stopRecording();
}

export function cancelAdvertisementVideoCaptureSession(
  sessionRef: { current: AdvertisementVideoCaptureSession | null },
  stopRecording: () => void,
  resetPending: () => void
) {
  const session = sessionRef.current;
  if (session) {
    session.canceled = true;
  }

  // 先同步释放当前会话并复位 UI，再停止原生录像；旧异步 finally 因会话不匹配不会覆盖新会话。
  sessionRef.current = null;
  resetPending();
  stopRecording();
}

export function createRecordedAdvertisementVideoAsset(
  uri: string,
  now: () => number = Date.now
): RecordedAdvertisementVideoAsset {
  const isQuickTime = /\.mov(?:$|[?#])/i.test(uri);
  return {
    uri,
    type: "video",
    fileName: `advertisement-${now()}.${isQuickTime ? "mov" : "mp4"}`,
    mimeType: isQuickTime ? "video/quicktime" : "video/mp4",
  };
}

export async function captureAdvertisementVideo({
  record,
  isCanceled,
  onCaptured,
  onError,
  onPendingChange,
  now,
}: CaptureAdvertisementVideoOptions) {
  onPendingChange(true);
  try {
    const result = await record();
    if (!result?.uri || isCanceled()) {
      return;
    }
    await onCaptured(createRecordedAdvertisementVideoAsset(result.uri, now));
  } catch (error) {
    // 用户取消会主动停止录制；此时原生层的拒绝不应被展示为上传失败。
    if (!isCanceled()) {
      onError(error);
    }
  } finally {
    onPendingChange(false);
  }
}
