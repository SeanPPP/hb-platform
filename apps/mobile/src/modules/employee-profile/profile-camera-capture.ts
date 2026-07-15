export type CapturedProfilePhoto = { uri: string; width: number; height: number };
export type ProfileCameraCaptureLock = { current: boolean };

export function createProfileCameraCaptureLock(): ProfileCameraCaptureLock {
  return { current: false };
}

export async function captureProfilePhoto({
  takePicture,
  onCaptured,
  onError,
  captureLock = createProfileCameraCaptureLock(),
  onPendingChange,
}: {
  takePicture: () => Promise<{ uri?: string; width?: number; height?: number } | undefined>;
  onCaptured: (image: CapturedProfilePhoto) => Promise<void>;
  onError: (error: unknown) => void;
  captureLock?: ProfileCameraCaptureLock;
  onPendingChange?: (pending: boolean) => void;
}) {
  if (captureLock.current) {
    return;
  }
  captureLock.current = true;
  onPendingChange?.(true);
  try {
    const picture = await takePicture();
    if (!picture?.uri) {
      throw new Error("Camera did not return a photo.");
    }
    await onCaptured({
      uri: picture.uri,
      width: picture.width || 1,
      height: picture.height || 1,
    });
  } catch (error) {
    onError(error);
  } finally {
    captureLock.current = false;
    onPendingChange?.(false);
  }
}
