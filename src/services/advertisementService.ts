import type { ApiResponse } from '../types/api'
import type {
  AdvertisementDetailDto,
  AdvertisementGridResult,
  AdvertisementMediaType,
  AdvertisementPayloadInput,
  AdvertisementUploadSignatureRequest,
  AdvertisementUploadSignatureResponse,
  CreateAdvertisementDto,
  UpdateAdvertisementDto,
} from '../types/advertisement'
import request, { unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/advertisements'

function toIsoString(value: string | { toISOString: () => string }) {
  return typeof value === 'string' ? value : value.toISOString()
}

export function stripAdvertisementMediaUrlQuery(rawUrl: string) {
  if (!rawUrl) {
    return ''
  }

  try {
    const parsedUrl = new URL(rawUrl)
    return `${parsedUrl.origin}${parsedUrl.pathname}`
  } catch {
    return rawUrl.split('?')[0]
  }
}

export function resolveAdvertisementMediaType(file: { type?: string; name?: string }): AdvertisementMediaType {
  const normalizedType = (file.type || '').toLowerCase()
  if (normalizedType.startsWith('video/')) {
    return 'Video'
  }

  const normalizedName = (file.name || '').toLowerCase()
  if (/\.(mp4|mov|m4v|webm|ogg)$/i.test(normalizedName)) {
    return 'Video'
  }

  return 'Image'
}

export function buildAdvertisementUpsertPayload(
  input: AdvertisementPayloadInput,
): CreateAdvertisementDto {
  return {
    title: input.title.trim(),
    description: input.description?.trim() || undefined,
    mediaType: input.mediaType,
    mediaUrl: stripAdvertisementMediaUrlQuery(input.mediaUrl),
    thumbnailUrl: input.thumbnailUrl?.trim() || undefined,
    objectKey: input.objectKey?.trim() || undefined,
    originalFileName: input.originalFileName?.trim() || undefined,
    contentType: input.contentType?.trim() || undefined,
    fileSize: input.fileSize == null ? undefined : Number(input.fileSize),
    effectiveStart: toIsoString(input.effectiveStart),
    effectiveEnd: toIsoString(input.effectiveEnd),
    isEnabled: input.isEnabled ?? true,
    sortOrder: Number(input.sortOrder ?? 0),
    stores: (input.stores ?? []).map((store) =>
      typeof store === 'string' ? { storeCode: store } : { storeCode: store.storeCode },
    ),
  }
}

export async function getAdvertisementGrid(data: Record<string, unknown>) {
  const response = await request.post<ApiResponse<AdvertisementGridResult>>(`${API_BASE}/grid`, data)
  return unwrapApiData(response)
}

export async function getAdvertisementById(id: string): Promise<AdvertisementDetailDto> {
  const response = await request.get<ApiResponse<AdvertisementDetailDto>>(`${API_BASE}/${id}`)
  return unwrapApiData(response)
}

export async function createAdvertisement(data: CreateAdvertisementDto): Promise<AdvertisementDetailDto> {
  const response = await request.post<ApiResponse<AdvertisementDetailDto>>(API_BASE, data)
  return unwrapApiData(response)
}

export async function updateAdvertisement(id: string, data: UpdateAdvertisementDto): Promise<AdvertisementDetailDto> {
  const response = await request.put<ApiResponse<AdvertisementDetailDto>>(`${API_BASE}/${id}`, data)
  return unwrapApiData(response)
}

export async function deleteAdvertisement(id: string): Promise<void> {
  await request.delete(`${API_BASE}/${id}`)
}

export async function enableAdvertisement(id: string, enable: boolean): Promise<void> {
  await request.post(`${API_BASE}/${id}/enable?enable=${enable}`)
}

export async function requestAdvertisementUploadSignature(
  data: AdvertisementUploadSignatureRequest,
): Promise<AdvertisementUploadSignatureResponse> {
  const response = await request.post<ApiResponse<AdvertisementUploadSignatureResponse>>(
    `${API_BASE}/upload-signature`,
    data,
  )
  return unwrapApiData(response)
}

export async function uploadAdvertisementFile(
  signature: AdvertisementUploadSignatureResponse,
  file: File,
): Promise<string> {
  const uploadUrl = signature.url || signature.uploadUrl
  if (!uploadUrl) {
    throw new Error('Upload URL is empty')
  }

  const response = await fetch(uploadUrl, {
    method: 'PUT',
    headers: signature.headers,
    body: file,
  })

  if (!response.ok) {
    throw new Error(`Upload failed: ${response.status}`)
  }

  return stripAdvertisementMediaUrlQuery(signature.mediaUrl || uploadUrl)
}
