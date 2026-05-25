import type {
  AttendanceDirectUploadSignature,
  AttendanceLeaveAttachmentUploadResult,
} from "@/modules/attendance/types";

function toDownloadUrl(url: string) {
  const [downloadUrl = ""] = url.split("?");
  return downloadUrl;
}

export async function uploadAttendanceLeaveAttachmentToSignedUrl(
  uri: string,
  signature: AttendanceDirectUploadSignature
): Promise<AttendanceLeaveAttachmentUploadResult> {
  const fileResponse = await fetch(uri);
  const blob = await fileResponse.blob();
  const response = await fetch(signature.url, {
    method: "PUT",
    headers: signature.headers,
    body: blob,
  });

  if (!response.ok) {
    throw new Error(`Upload failed with status ${response.status}`);
  }

  return {
    objectKey: signature.objectKey,
    downloadUrl: toDownloadUrl(signature.url),
  };
}
