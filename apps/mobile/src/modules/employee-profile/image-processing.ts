import type { EmployeeProfileImageKind } from "@/modules/employee-profile/types";

export const EMPLOYEE_PROFILE_IMAGE_MAX_BYTES = 5 * 1024 * 1024;

type ImageSource = {
  kind: EmployeeProfileImageKind;
  uri: string;
  width: number;
  height: number;
};

type ProcessedImage = {
  uri: string;
  width: number;
  height: number;
  fileName: string;
  contentType: "image/jpeg";
  fileSize: number;
};

type ProcessingDependencies = {
  manipulate?: (
    uri: string,
    actions: ImageAction[],
    options: { compress: number; format: "jpeg" }
  ) => Promise<{ uri: string; width: number; height: number }>;
  readBlobSize?: (uri: string) => Promise<number>;
};

type ImageAction =
  | { crop: { originX: number; originY: number; width: number; height: number } }
  | { resize: { width?: number; height?: number } };

export class EmployeeProfileImageProcessingError extends Error {
  constructor(
    public readonly code: "decode_failed" | "blob_read_failed" | "file_too_large"
  ) {
    super(code);
  }
}

export function calculateAvatarCrop(width: number, height: number) {
  const size = Math.min(width, height);
  return {
    originX: Math.floor((width - size) / 2),
    originY: Math.floor((height - size) / 2),
    width: size,
    height: size,
  };
}

const PROCESSING_LEVELS: Record<EmployeeProfileImageKind, Array<{ maxSide: number; quality: number }>> = {
  avatar: [
    { maxSide: 1024, quality: 0.82 },
    { maxSide: 768, quality: 0.72 },
    { maxSide: 640, quality: 0.6 },
  ],
  identityPhoto: [
    { maxSide: 2048, quality: 0.85 },
    { maxSide: 1600, quality: 0.72 },
    { maxSide: 1280, quality: 0.6 },
  ],
};

function buildActions(source: ImageSource, maxSide: number): ImageAction[] {
  if (source.kind === "avatar") {
    const crop = calculateAvatarCrop(source.width, source.height);
    return [{ crop }, { resize: { width: Math.min(crop.width, maxSide) } }];
  }

  // 证件照只按长边缩放，不能裁掉证件边缘。
  if (source.width >= source.height) {
    return [{ resize: { width: Math.min(source.width, maxSide) } }];
  }
  return [{ resize: { height: Math.min(source.height, maxSide) } }];
}

async function readBlobSize(uri: string) {
  const response = await fetch(uri);
  const blob = await response.blob();
  if (!blob.size) {
    throw new Error("empty blob");
  }
  return blob.size;
}

export async function processEmployeeProfileImage(
  source: ImageSource | null,
  dependencies: ProcessingDependencies = {}
): Promise<ProcessedImage | null> {
  if (!source) {
    return null;
  }

  const manipulate = dependencies.manipulate ?? (async (uri, actions, options) => {
    // 延迟加载原生模块，让纯图片策略测试不依赖 React Native 运行时。
    const imageManipulator = await import("expo-image-manipulator");
    return imageManipulator.manipulateAsync(uri, actions, {
      compress: options.compress,
      format: imageManipulator.SaveFormat.JPEG,
    });
  });
  const getBlobSize = dependencies.readBlobSize ?? readBlobSize;
  const levels = PROCESSING_LEVELS[source.kind];

  for (let index = 0; index < levels.length; index += 1) {
    const level = levels[index];
    let result: { uri: string; width: number; height: number };
    try {
      result = await manipulate(source.uri, buildActions(source, level.maxSide), {
        compress: level.quality,
        format: "jpeg",
      });
    } catch {
      throw new EmployeeProfileImageProcessingError("decode_failed");
    }

    let fileSize: number;
    try {
      fileSize = await getBlobSize(result.uri);
    } catch {
      throw new EmployeeProfileImageProcessingError("blob_read_failed");
    }

    if (fileSize > 0 && fileSize <= EMPLOYEE_PROFILE_IMAGE_MAX_BYTES) {
      return {
        ...result,
        fileName: `${source.kind}-${Date.now()}.jpg`,
        contentType: "image/jpeg",
        fileSize,
      };
    }
  }

  throw new EmployeeProfileImageProcessingError("file_too_large");
}
