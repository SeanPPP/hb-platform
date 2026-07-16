import {
  completeEmployeeProfileImageUploadApi,
  getEmployeeProfileImageUploadSignature,
  uploadEmployeeProfileImageBlobToSignedUrl,
} from "@/modules/employee-profile/api";
import { completeEmployeeProfileImageUpload } from "@/modules/employee-profile/image-upload-workflow";
import type { EmployeeProfileImageKind } from "@/modules/employee-profile/types";
import { i18n } from "@/shared/i18n/i18n";
import { isIosReviewSessionActive } from "@/modules/ios-review/session";
import { reviewAwareFetch } from "@/modules/ios-review/network";

export class EmployeeProfileImageUploadError extends Error {
  code: "signature_unavailable" | "upload_failed";

  constructor(code: "signature_unavailable" | "upload_failed", message: string) {
    super(message);
    this.code = code;
  }
}

function isMissingUploadSignatureError(error: unknown) {
  if (!(error instanceof Error)) {
    return false;
  }

  const normalized = error.message.trim().toLowerCase();
  return normalized.includes("404") || normalized.includes("not found");
}

export async function uploadEmployeeProfileImage(params: {
  kind: EmployeeProfileImageKind;
  uri: string;
  fileName: string;
  contentType?: string;
  fileSize: number;
}) {
  const contentType = params.contentType ?? "image/jpeg";
  try {
    if (isIosReviewSessionActive()) {
      // 审核模式直接由本地 adapter 完成资料变更，跳过 Blob、签名和对象存储。
      return await completeEmployeeProfileImageUploadApi(params.kind, params.uri);
    }
    return await completeEmployeeProfileImageUpload(
      { ...params, contentType },
      {
        readBlob: async (uri) => {
          const response = await reviewAwareFetch(uri);
          return response.blob();
        },
        requestSignature: getEmployeeProfileImageUploadSignature,
        uploadBlob: uploadEmployeeProfileImageBlobToSignedUrl,
        completeUpload: completeEmployeeProfileImageUploadApi,
      }
    );
  } catch (error) {
    if (isMissingUploadSignatureError(error)) {
      throw new EmployeeProfileImageUploadError(
        "signature_unavailable",
        i18n.t("common:errors.requestFailed")
      );
    }
    if (error instanceof EmployeeProfileImageUploadError) {
      throw error;
    }
    throw new EmployeeProfileImageUploadError(
      "upload_failed",
      error instanceof Error ? error.message : i18n.t("common:errors.requestFailed")
    );
  }
}
