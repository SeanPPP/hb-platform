import {
  getEmployeeProfileImageUploadSignature,
  uploadEmployeeProfileImageBlobToSignedUrl,
} from "@/modules/employee-profile/api";
import type {
  EmployeeProfileImageKind,
  EmployeeProfileImageUploadResult,
} from "@/modules/employee-profile/types";

export class EmployeeProfileImageUploadError extends Error {
  code: "signature_unavailable" | "upload_failed";

  constructor(code: "signature_unavailable" | "upload_failed", message: string) {
    super(message);
    this.code = code;
  }
}

function sanitizeFileName(fileName: string) {
  const trimmed = fileName.trim();
  if (!trimmed) {
    return "photo.jpg";
  }

  return trimmed.replace(/[^a-zA-Z0-9._-]/g, "-");
}

function buildObjectKey(kind: EmployeeProfileImageKind, fileName: string) {
  return `employee-profile/${kind}/${Date.now()}-${sanitizeFileName(fileName)}`;
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
}): Promise<EmployeeProfileImageUploadResult> {
  const contentType = params.contentType ?? "image/jpeg";
  const fileResponse = await fetch(params.uri);
  const fileBlob = await fileResponse.blob();

  if (!fileBlob.size) {
    throw new EmployeeProfileImageUploadError("upload_failed", "Empty image file");
  }

  let signature;
  try {
    signature = await getEmployeeProfileImageUploadSignature(params.kind, {
      fileName: params.fileName,
      contentType,
      fileSize: fileBlob.size,
      objectKey: buildObjectKey(params.kind, params.fileName),
    });
  } catch (error) {
    if (isMissingUploadSignatureError(error)) {
      throw new EmployeeProfileImageUploadError(
        "signature_unavailable",
        "Employee profile image upload is not available"
      );
    }

    throw error;
  }

  try {
    await uploadEmployeeProfileImageBlobToSignedUrl(fileBlob, signature);
  } catch (error) {
    throw new EmployeeProfileImageUploadError(
      "upload_failed",
      error instanceof Error ? error.message : "Image upload failed"
    );
  }

  return {
    objectKey: signature.objectKey,
    downloadUrl: signature.url.split("?")[0] ?? "",
  };
}
