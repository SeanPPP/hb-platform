import type {
  AdvertisementUploadSignature,
  AdvertisementUploadedAsset,
} from "@/modules/advertisements/types";
import { reportExternalFetchFailure } from "@/shared/logging/external-fetch-log";

export function stripUrlQuery(url: string) {
  const [baseUrl = ""] = url.split("?");
  return baseUrl;
}

export async function uploadAdvertisementAssetToSignedUrl(
  uri: string,
  signature: AdvertisementUploadSignature
): Promise<AdvertisementUploadedAsset> {
  let fileResponse: Response;
  try {
    fileResponse = await fetch(uri);
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
    response = await fetch(uploadUrl, {
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
