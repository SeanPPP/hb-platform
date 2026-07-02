import {
  applyUploadedAssetToDraft,
  getAdvertisementUploadFeedback,
  resolveAdvertisementUploadErrorStatus,
  type AdvertisementUploadFeedbackInput,
} from "./advertisement-upload-feedback";
import type { AdvertisementDraft } from "./types";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualText = JSON.stringify(actual);
  const expectedText = JSON.stringify(expected);
  if (actualText !== expectedText) {
    throw new Error(`${label}: expected ${expectedText}, got ${actualText}`);
  }
}

function assertFeedback(
  input: AdvertisementUploadFeedbackInput,
  expected: { titleKey: string; descriptionKey: string }
) {
  const feedback = getAdvertisementUploadFeedback(input);
  assertEqual(feedback.titleKey, expected.titleKey, "upload feedback title key");
  assertEqual(
    feedback.descriptionKey,
    expected.descriptionKey,
    "upload feedback description key"
  );
}

assertFeedback(
  {
    status: "idle",
    fileName: "",
  },
  {
    titleKey: "upload.status.idleTitle",
    descriptionKey: "upload.status.idleDescription",
  }
);

assertFeedback(
  {
    status: "uploading",
    source: "library",
  },
  {
    titleKey: "upload.status.uploadingTitle",
    descriptionKey: "upload.status.libraryDescription",
  }
);

assertFeedback(
  {
    status: "uploaded",
    mediaType: "video",
    fileName: "promo.mp4",
  },
  {
    titleKey: "upload.status.videoReadyTitle",
    descriptionKey: "upload.status.uploadedDescription",
  }
);

assertFeedback(
  {
    status: "canceled",
    source: "cameraPhoto",
  },
  {
    titleKey: "upload.status.canceledTitle",
    descriptionKey: "upload.status.canceledDescription",
  }
);

assertFeedback(
  {
    status: "permissionDenied",
    source: "library",
  },
  {
    titleKey: "upload.status.permissionTitle",
    descriptionKey: "upload.status.libraryPermissionDescription",
  }
);

assertFeedback(
  {
    status: "cameraPermissionDenied",
    source: "cameraVideo",
  },
  {
    titleKey: "upload.status.permissionTitle",
    descriptionKey: "upload.status.cameraPermissionDescription",
  }
);

assertFeedback(
  {
    status: "failed",
    source: "library",
  },
  {
    titleKey: "upload.status.failedTitle",
    descriptionKey: "upload.status.failedDescription",
  }
);

const baseDraft: AdvertisementDraft = {
  id: null,
  title: "",
  description: "",
  mediaType: "image",
  mediaUrl: "",
  thumbnailUrl: "",
  objectKey: "",
  originalFileName: "",
  contentType: "",
  fileSize: "",
  effectiveStart: "",
  effectiveEnd: "",
  isEnabled: true,
  sortOrder: "0",
  storeCodes: [],
};

assertDeepEqual(
  applyUploadedAssetToDraft({
    draft: baseDraft,
    uploadedAsset: {
      objectKey: "ads/banner.jpg",
      mediaUrl: "https://cdn.example.com/banner.jpg",
    },
    assetType: "image",
    fileName: "banner.jpg",
    contentType: "image/jpeg",
    fileSize: 4096,
  }),
  {
    ...baseDraft,
    mediaType: "image",
    mediaUrl: "https://cdn.example.com/banner.jpg",
    thumbnailUrl: "https://cdn.example.com/banner.jpg",
    objectKey: "ads/banner.jpg",
    originalFileName: "banner.jpg",
    contentType: "image/jpeg",
    fileSize: "4096",
  },
  "image upload backfills draft asset fields"
);

assertDeepEqual(
  applyUploadedAssetToDraft({
    draft: {
      ...baseDraft,
      mediaType: "image",
      mediaUrl: "https://cdn.example.com/old.jpg",
      thumbnailUrl: "https://cdn.example.com/old-thumb.jpg",
    },
    uploadedAsset: {
      objectKey: "ads/promo.mp4",
      mediaUrl: "https://cdn.example.com/promo.mp4",
    },
    assetType: "video",
    fileName: "promo.mp4",
    contentType: "video/mp4",
    fileSize: 8192,
  }),
  {
    ...baseDraft,
    mediaType: "video",
    mediaUrl: "https://cdn.example.com/promo.mp4",
    thumbnailUrl: "",
    objectKey: "ads/promo.mp4",
    originalFileName: "promo.mp4",
    contentType: "video/mp4",
    fileSize: "8192",
  },
  "video upload clears stale image thumbnail and backfills draft fields"
);

assertEqual(
  resolveAdvertisementUploadErrorStatus(new Error("permission-denied")),
  "permissionDenied",
  "library permission error maps to dedicated upload status"
);

assertEqual(
  resolveAdvertisementUploadErrorStatus(new Error("camera-permission-denied")),
  "cameraPermissionDenied",
  "camera permission error maps to dedicated upload status"
);

assertEqual(
  resolveAdvertisementUploadErrorStatus(new Error("network down")),
  "failed",
  "generic upload errors map to failed status"
);
