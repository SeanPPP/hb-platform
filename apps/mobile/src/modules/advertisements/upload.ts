import type {
  AdvertisementUploadSignature,
  AdvertisementUploadedAsset,
} from "@/modules/advertisements/types";
import { reportExternalFetchFailure } from "@/shared/logging/external-fetch-log";
import { isIosReviewSessionActive } from "@/modules/ios-review/session";
import { reviewAwareFetch } from "@/modules/ios-review/network";

export function stripUrlQuery(url: string) {
  const [baseUrl = ""] = url.split("?");
  return baseUrl;
}

export async function uploadAdvertisementAssetToSignedUrl(
  uri: string,
  signature: AdvertisementUploadSignature
): Promise<AdvertisementUploadedAsset> {
  if (isIosReviewSessionActive()) {
    // 审核模式直接使用本地素材 URI，保持预览但不访问对象存储。
    return {
      objectKey: signature.objectKey || `ios-review/advertisements/${Date.now()}`,
      mediaUrl: uri,
    };
  }
  let fileResponse: Response;
  try {
    fileResponse = await reviewAwareFetch(uri);
  } catch (error) {
    reportExternalFetchFailure({
      message: "广告素材本地文件读取失败",
      sourceType: "advertisements.upload",
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
  const uploadUrl = signature.url || signature.uploadUrl;
  if (!uploadUrl) {
    throw new Error("Upload URL is empty");
  }

  let response: Response;
  try {
    response = await reviewAwareFetch(uploadUrl, {
      method: "PUT",
      headers: signature.headers,
      body: blob,
    });
  } catch (error) {
    reportExternalFetchFailure({
      message: "广告素材上传请求失败",
      sourceType: "advertisements.upload",
      requestMethod: "PUT",
      requestUrl: uploadUrl,
      error,
      fileUri: uri,
      properties: {
        objectKey: signature.objectKey,
        uploadUrl,
      },
    });
    throw error;
  }

  if (!response.ok) {
    reportExternalFetchFailure({
      message: "广告素材上传失败",
      sourceType: "advertisements.upload",
      requestMethod: "PUT",
      requestUrl: uploadUrl,
      statusCode: response.status,
      fileUri: uri,
      properties: {
        objectKey: signature.objectKey,
        uploadUrl,
      },
    });
    throw new Error(`Upload failed with status ${response.status}`);
  }

  return {
    objectKey: signature.objectKey,
    mediaUrl: stripUrlQuery(signature.mediaUrl || uploadUrl),
  };
}
