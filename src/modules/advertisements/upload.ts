import type {
  AdvertisementUploadSignature,
  AdvertisementUploadedAsset,
} from "@/modules/advertisements/types";

export function stripUrlQuery(url: string) {
  const [baseUrl = ""] = url.split("?");
  return baseUrl;
}

export async function uploadAdvertisementAssetToSignedUrl(
  uri: string,
  signature: AdvertisementUploadSignature
): Promise<AdvertisementUploadedAsset> {
  const fileResponse = await fetch(uri);
  const blob = await fileResponse.blob();
  const uploadUrl = signature.url || signature.uploadUrl;
  if (!uploadUrl) {
    throw new Error("Upload URL is empty");
  }

  const response = await fetch(uploadUrl, {
    method: "PUT",
    headers: signature.headers,
    body: blob,
  });

  if (!response.ok) {
    throw new Error(`Upload failed with status ${response.status}`);
  }

  return {
    objectKey: signature.objectKey,
    mediaUrl: stripUrlQuery(signature.mediaUrl || uploadUrl),
  };
}
