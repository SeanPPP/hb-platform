import type {
  DirectUploadRequest,
  DirectUploadSignature,
  EmployeeProfile,
  EmployeeProfileImageKind,
} from "./types";
import { EMPLOYEE_PROFILE_IMAGE_MAX_BYTES } from "./image-processing";

type UploadInput = {
  kind: EmployeeProfileImageKind;
  uri: string;
  fileName: string;
  contentType: string;
  fileSize: number;
};

type UploadDependencies = {
  readBlob: (uri: string) => Promise<Blob>;
  requestSignature: (
    kind: EmployeeProfileImageKind,
    request: DirectUploadRequest
  ) => Promise<DirectUploadSignature>;
  uploadBlob: (blob: Blob, signature: DirectUploadSignature) => Promise<string>;
  completeUpload: (
    kind: EmployeeProfileImageKind,
    objectKey: string
  ) => Promise<EmployeeProfile>;
};

export async function completeEmployeeProfileImageUpload(
  input: UploadInput,
  dependencies: UploadDependencies
) {
  const blob = await dependencies.readBlob(input.uri);
  if (!blob.size || blob.size > EMPLOYEE_PROFILE_IMAGE_MAX_BYTES) {
    throw new Error("empty image");
  }

  const signature = await dependencies.requestSignature(input.kind, {
    fileName: input.fileName,
    contentType: input.contentType,
    // 请求签名前再次以实际 Blob 为准，避免处理结果和声明大小发生偏差。
    fileSize: blob.size,
  });
  await dependencies.uploadBlob(blob, signature);
  return dependencies.completeUpload(input.kind, signature.objectKey);
}
