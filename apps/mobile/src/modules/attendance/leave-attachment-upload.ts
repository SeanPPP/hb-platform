import type {
  AttendanceDirectUploadSignature,
  AttendanceLeaveAttachmentUploadResult,
} from "@/modules/attendance/types";
import { reportExternalFetchFailure } from "@/shared/logging/external-fetch-log";
import { isIosReviewSessionActive } from "@/modules/ios-review/session";
import { reviewAwareFetch } from "@/modules/ios-review/network";

function toDownloadUrl(url: string) {
  const [downloadUrl = ""] = url.split("?");
  return downloadUrl;
}

export async function uploadAttendanceLeaveAttachmentToSignedUrl(
  uri: string,
  signature: AttendanceDirectUploadSignature
): Promise<AttendanceLeaveAttachmentUploadResult> {
  if (isIosReviewSessionActive()) {
    // 保留本地 URI 作为预览，模拟上传成功且不请求签名地址。
    return {
      objectKey: signature.objectKey || `ios-review/attendance/${Date.now()}`,
      downloadUrl: uri,
    };
  }
  let fileResponse: Response;
  try {
    fileResponse = await reviewAwareFetch(uri);
  } catch (error) {
    reportExternalFetchFailure({
      message: "请假附件本地文件读取失败",
      sourceType: "attendance.leave-upload",
      requestMethod: "GET",
      requestUrl: uri,
      error,
      fileUri: uri,
      properties: {
        objectKey: signature.objectKey,
      },
    });
    throw error;
  }

  const blob = await fileResponse.blob();
  let response: Response;
  try {
    response = await reviewAwareFetch(signature.url, {
      method: "PUT",
      headers: signature.headers,
      body: blob,
    });
  } catch (error) {
    reportExternalFetchFailure({
      message: "请假附件上传请求失败",
      sourceType: "attendance.leave-upload",
      requestMethod: "PUT",
      requestUrl: signature.url,
      error,
      fileUri: uri,
      properties: {
        objectKey: signature.objectKey,
        uploadUrl: signature.url,
      },
    });
    throw error;
  }

  if (!response.ok) {
    reportExternalFetchFailure({
      message: "请假附件上传失败",
      sourceType: "attendance.leave-upload",
      requestMethod: "PUT",
      requestUrl: signature.url,
      statusCode: response.status,
      fileUri: uri,
      properties: {
        objectKey: signature.objectKey,
        uploadUrl: signature.url,
      },
    });
    throw new Error(`Upload failed with status ${response.status}`);
  }

  return {
    objectKey: signature.objectKey,
    downloadUrl: toDownloadUrl(signature.url),
  };
}
