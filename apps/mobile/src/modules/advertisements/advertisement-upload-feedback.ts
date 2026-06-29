import type {
  AdvertisementDraft,
  AdvertisementMediaType,
  AdvertisementUploadedAsset,
} from "@/modules/advertisements/types";

export type AdvertisementUploadSource = "library" | "cameraPhoto" | "cameraVideo";

export type AdvertisementUploadFeedbackStatus =
  | "idle"
  | "uploading"
  | "uploaded"
  | "canceled"
  | "permissionDenied"
  | "cameraPermissionDenied"
  | "failed";

export interface AdvertisementUploadFeedbackInput {
  status: AdvertisementUploadFeedbackStatus;
  source?: AdvertisementUploadSource | null;
  mediaType?: AdvertisementMediaType | null;
  fileName?: string | null;
}

export interface AdvertisementUploadFeedback {
  titleKey: string;
  descriptionKey: string;
}

export interface ApplyUploadedAssetToDraftInput {
  draft: AdvertisementDraft;
  uploadedAsset: AdvertisementUploadedAsset;
  assetType?: AdvertisementMediaType | null;
  fileName: string;
  contentType: string;
  fileSize: number;
}

export function getAdvertisementUploadFeedback(
  input: AdvertisementUploadFeedbackInput
): AdvertisementUploadFeedback {
  switch (input.status) {
    case "uploading":
      return {
        titleKey: "upload.status.uploadingTitle",
        descriptionKey:
          input.source === "library"
            ? "upload.status.libraryDescription"
            : "upload.status.cameraDescription",
      };
    case "uploaded":
      return {
        titleKey:
          input.mediaType === "video"
            ? "upload.status.videoReadyTitle"
            : "upload.status.imageReadyTitle",
        descriptionKey: "upload.status.uploadedDescription",
      };
    case "canceled":
      return {
        titleKey: "upload.status.canceledTitle",
        descriptionKey: "upload.status.canceledDescription",
      };
    case "permissionDenied":
      return {
        titleKey: "upload.status.permissionTitle",
        descriptionKey: "upload.status.libraryPermissionDescription",
      };
    case "cameraPermissionDenied":
      return {
        titleKey: "upload.status.permissionTitle",
        descriptionKey: "upload.status.cameraPermissionDescription",
      };
    case "failed":
      return {
        titleKey: "upload.status.failedTitle",
        descriptionKey: "upload.status.failedDescription",
      };
    default:
      return {
        titleKey: input.fileName?.trim()
          ? "upload.status.fileSelectedTitle"
          : "upload.status.idleTitle",
        descriptionKey: input.fileName?.trim()
          ? "upload.status.fileSelectedDescription"
          : "upload.status.idleDescription",
      };
  }
}

export function resolveAdvertisementUploadErrorStatus(
  error: unknown
): AdvertisementUploadFeedbackStatus {
  if (error instanceof Error && error.message === "permission-denied") {
    return "permissionDenied";
  }
  if (error instanceof Error && error.message === "camera-permission-denied") {
    return "cameraPermissionDenied";
  }
  return "failed";
}

export function applyUploadedAssetToDraft(
  input: ApplyUploadedAssetToDraftInput
): AdvertisementDraft {
  const nextMediaType = input.assetType === "video" ? "video" : "image";
  const nextMediaUrl = input.uploadedAsset.mediaUrl.trim()
    ? input.uploadedAsset.mediaUrl
    : input.draft.mediaUrl;

  return {
    ...input.draft,
    // 上传成功后统一回填素材元信息，避免保存前还要手工补齐。
    mediaType: nextMediaType,
    mediaUrl: nextMediaUrl,
    thumbnailUrl: nextMediaType === "image" ? nextMediaUrl : "",
    objectKey: input.uploadedAsset.objectKey,
    originalFileName: input.fileName,
    contentType: input.contentType,
    fileSize: String(input.fileSize),
  };
}
