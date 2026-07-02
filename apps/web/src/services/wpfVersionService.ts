import type { ApiResponse, PagedResult } from '../types/api'
import type {
  CreateWpfAppReleaseRequest,
  WpfAppRelease,
  WpfAppReleasePagedResult,
  WpfAppReleaseUpdateRequest,
  WpfInstallerType,
  WpfReleasePolicyRequest,
  WpfReleaseUploadInitRequest,
  WpfReleaseUploadInitRawResult,
  WpfReleaseUploadInitResult,
} from '../types/wpfVersion'
import request, { unwrapApiData } from '../utils/request'

const WPF_APP_RELEASES_API = '/api/wpf-app-releases'

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' ? (value as Record<string, unknown>) : {}
}

function getString(raw: Record<string, unknown>, key: string) {
  const value = raw[key]
  return typeof value === 'string' ? value : null
}

function hasValue(value: string | null | undefined) {
  return typeof value === 'string' && value.trim().length > 0
}

function getBoolean(raw: Record<string, unknown>, key: string) {
  const value = raw[key]
  if (typeof value === 'boolean') {
    return value
  }
  if (typeof value === 'string') {
    return value.trim().toLowerCase() === 'true'
  }
  return false
}

function getNullableBoolean(raw: Record<string, unknown>, key: string) {
  const value = raw[key]
  if (typeof value === 'boolean') {
    return value
  }
  if (typeof value === 'string') {
    const normalizedValue = value.trim().toLowerCase()
    if (normalizedValue === 'true') {
      return true
    }
    if (normalizedValue === 'false') {
      return false
    }
  }
  return null
}

function getNumber(raw: Record<string, unknown>, key: string) {
  const value = raw[key]
  if (typeof value === 'number' && Number.isFinite(value)) {
    return value
  }
  if (typeof value === 'string') {
    const parsed = Number(value)
    return Number.isFinite(parsed) ? parsed : null
  }
  return null
}

export function normalizeWpfInstallerType(value: unknown): WpfInstallerType | null {
  if (typeof value !== 'string') {
    return null
  }

  const normalizedValue = value.trim().toLowerCase()

  // 前端只接受更新链真实支持的 exe/msi，避免异常值透传到界面。
  if (normalizedValue === 'exe' || normalizedValue === 'msi') {
    return normalizedValue
  }

  return null
}

function getHeaders(raw: Record<string, unknown>) {
  const headers = asRecord(raw.headers ?? raw.uploadHeaders)
  return Object.fromEntries(
    Object.entries(headers).filter((entry): entry is [string, string] => typeof entry[1] === 'string'),
  )
}

function normalizePathSegment(value: string) {
  return value.trim().replace(/\\/g, '/').replace(/^\/+|\/+$/g, '')
}

export function normalizeWpfVersion(value: string) {
  const trimmed = value.trim()
  const match = trimmed.match(/^v?(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?$/i)
  if (!match) {
    return trimmed
  }

  return match[4]
    ? `${match[1]}.${match[2]}.${match[3]}.${match[4]}`
    : `${match[1]}.${match[2]}.${match[3]}`
}

function buildWpfResponseError(raw: Record<string, unknown>) {
  const code = getString(raw, 'code') ?? getString(raw, 'errorCode')
  const message = getString(raw, 'message') ?? 'WPF release request failed'

  return new Error(code ? `${code}: ${message}` : message)
}

function assertWpfResponseSucceeded(payload: unknown) {
  const raw = asRecord(payload)
  const success = getNullableBoolean(raw, 'success') ?? getNullableBoolean(raw, 'isSuccess')

  // WPF 版本接口可能返回 HTTP 200 + success=false，这里统一转成抛错。
  if (success === false) {
    throw buildWpfResponseError(raw)
  }
}

function unwrapWpfResponseData<T>(payload: ApiResponse<T> | T) {
  assertWpfResponseSucceeded(payload)

  return unwrapApiData(payload)
}

function unwrapWpfReleaseListData<T>(payload: ApiResponse<T> | T) {
  assertWpfResponseSucceeded(payload)
  const raw = asRecord(payload)

  // 历史兼容形态是 { data: [], total, page, pageSize }，不能拆掉 data 后丢失同级分页字段。
  if (Array.isArray(raw.data)) {
    return raw
  }

  return unwrapApiData(payload)
}

export function buildWpfReleaseObjectKey(input: {
  channel: string
  version: string
  fileName: string
}) {
  const channel = normalizePathSegment(input.channel).toLowerCase() || 'production'
  const version = normalizePathSegment(normalizeWpfVersion(input.version))
  const fileName = normalizePathSegment(input.fileName).replace(/\s+/g, '-')
  return `wpf-releases/${channel}/${version}/${fileName}`
}

export function normalizeWpfAppRelease(raw: Record<string, unknown>): WpfAppRelease {
  return {
    id: getString(raw, 'id') ?? getString(raw, 'releaseId') ?? '',
    version: getString(raw, 'version') ?? '',
    channel: getString(raw, 'channel') ?? 'production',
    fileName: getString(raw, 'fileName') ?? '',
    fileSize: getNumber(raw, 'fileSize'),
    sha256: getString(raw, 'sha256'),
    installerType: normalizeWpfInstallerType(raw.installerType),
    installerArguments: getString(raw, 'installerArguments'),
    downloadUrl: getString(raw, 'downloadUrl'),
    objectKey: getString(raw, 'objectKey') ?? getString(raw, 'cosObjectKey'),
    releaseNotes: getString(raw, 'releaseNotes'),
    isActive: getBoolean(raw, 'isActive') || getBoolean(raw, 'active'),
    isCurrent: getBoolean(raw, 'isCurrent') || getBoolean(raw, 'current'),
    isRollback: getBoolean(raw, 'isRollback'),
    forceUpdate: getBoolean(raw, 'forceUpdate'),
    minimumSupportedVersion: getString(raw, 'minimumSupportedVersion'),
    targetVersion: getString(raw, 'targetVersion'),
    createdAt: getString(raw, 'createdAt'),
    updatedAt: getString(raw, 'updatedAt') ?? getString(raw, 'lastModifiedAt'),
  }
}

export function normalizeWpfReleaseUploadInitResult(raw: WpfReleaseUploadInitRawResult): WpfReleaseUploadInitResult {
  const directUpload = asRecord(raw.directUpload)

  // 中文注释：直传初始化成功契约必须显式包含 directUpload，不能把 multipartUpload 误当成可直接 PUT 的地址。
  return {
    uploadUrl: getString(directUpload, 'uploadUrl') ?? getString(directUpload, 'signedUrl') ?? getString(directUpload, 'url') ?? '',
    uploadMethod: getString(directUpload, 'uploadMethod') ?? getString(directUpload, 'method') ?? 'PUT',
    objectKey: getString(asRecord(raw), 'objectKey') ?? getString(directUpload, 'objectKey') ?? getString(asRecord(raw), 'cosObjectKey') ?? '',
    downloadUrl: getString(asRecord(raw), 'downloadUrl') ?? getString(asRecord(raw), 'publicUrl') ?? getString(directUpload, 'publicUrl') ?? '',
    headers: getHeaders(directUpload),
  }
}

export async function getWpfAppReleases(params?: {
  page?: number
  pageSize?: number
  channel?: string
  includeDisabled?: boolean
}): Promise<WpfAppReleasePagedResult> {
  const page = params?.page ?? 1
  const pageSize = params?.pageSize ?? 10
  const response = await request.get<
    | ApiResponse<PagedResult<Record<string, unknown>>>
    | PagedResult<Record<string, unknown>>
    | {
        data?: Record<string, unknown>[]
        list?: Record<string, unknown>[]
        items?: Record<string, unknown>[]
        total?: number
        totalCount?: number
        page?: number
        pageSize?: number
      }
  >(WPF_APP_RELEASES_API, {
    params: {
      page,
      pageSize,
      channel: params?.channel,
      includeDisabled: params?.includeDisabled,
    },
  })

  const raw = asRecord(unwrapWpfReleaseListData(response))
  // 兼容 items/list/data 三种分页形态，避免代理层包装差异影响页面。
  const rawItems = Array.isArray(raw.items)
    ? raw.items
    : Array.isArray(raw.list)
      ? raw.list
      : Array.isArray(raw.data)
        ? raw.data
        : []

  return {
    items: rawItems.map((item) => normalizeWpfAppRelease(asRecord(item))),
    total: Number(raw.total ?? raw.totalCount ?? rawItems.length),
    page: Number(raw.page ?? page),
    pageSize: Number(raw.pageSize ?? pageSize),
  }
}

export async function initWpfReleaseUpload(input: WpfReleaseUploadInitRequest) {
  const payload = {
    ...input,
    channel: input.channel.trim().toLowerCase(),
    version: normalizeWpfVersion(input.version),
    fileName: input.fileName.trim(),
    sha256: input.sha256?.trim() || undefined,
    contentType: input.contentType || undefined,
    objectKey: buildWpfReleaseObjectKey(input),
  }
  const response = await request.post<ApiResponse<Record<string, unknown>> | Record<string, unknown>>(
    `${WPF_APP_RELEASES_API}/upload/init`,
    payload,
  )
  const uploadInit = normalizeWpfReleaseUploadInitResult(asRecord(unwrapWpfResponseData(response)))
  // 上传初始化必须返回可直接 PUT 的地址，避免后续 fetch 空 URL。
  if (!hasValue(uploadInit.uploadUrl)) {
    throw new Error('WPF release upload init response is missing uploadUrl')
  }
  // 发布记录依赖可下载地址，初始化阶段缺失时立即失败。
  if (!hasValue(uploadInit.downloadUrl)) {
    throw new Error('WPF release upload init response is missing downloadUrl')
  }
  return uploadInit
}

export async function uploadWpfReleaseFile(file: File, upload: WpfReleaseUploadInitResult) {
  // 二次防御：即使调用方绕过初始化校验，也不能向空地址发起上传。
  if (!hasValue(upload.uploadUrl)) {
    throw new Error('WPF release uploadUrl is required before uploading file')
  }

  const response = await fetch(upload.uploadUrl, {
    method: upload.uploadMethod || 'PUT',
    headers: upload.headers,
    body: file,
  })

  if (!response.ok) {
    throw new Error(`COS upload failed (${response.status})`)
  }
}

export async function createWpfAppRelease(input: CreateWpfAppReleaseRequest) {
  const payload = {
    ...input,
    version: normalizeWpfVersion(input.version),
    channel: input.channel.trim().toLowerCase(),
    fileName: input.fileName.trim(),
    sha256: input.sha256?.trim() || undefined,
    installerArguments: input.installerArguments?.trim() || undefined,
    releaseNotes: input.releaseNotes?.trim() || undefined,
    cosObjectKey: input.objectKey,
  }
  const response = await request.post<ApiResponse<Record<string, unknown>> | Record<string, unknown>>(
    WPF_APP_RELEASES_API,
    payload,
  )
  return normalizeWpfAppRelease(asRecord(unwrapWpfResponseData(response)))
}

export async function saveWpfReleasePolicy(input: WpfReleasePolicyRequest) {
  const payload: WpfReleasePolicyRequest = {
    channel: input.channel.trim().toLowerCase(),
    targetVersion: normalizeWpfVersion(input.targetVersion),
    minimumSupportedVersion: normalizeWpfVersion(input.minimumSupportedVersion),
    forceUpdate: input.forceUpdate,
    isRollback: input.isRollback,
    rollbackConfirmed: input.rollbackConfirmed,
  }
  const response = await request.post<ApiResponse<unknown> | unknown>(
    `${WPF_APP_RELEASES_API}/policy`,
    payload,
  )
  return unwrapWpfResponseData(response)
}

export async function updateWpfAppRelease(id: string, input: WpfAppReleaseUpdateRequest) {
  const payload: WpfAppReleaseUpdateRequest = {
    ...input,
    downloadUrl: input.downloadUrl?.trim() || undefined,
    sha256: input.sha256?.trim() || undefined,
    installerArguments: input.installerArguments?.trim() || undefined,
    releaseNotes: input.releaseNotes?.trim() || undefined,
  }
  const response = await request.put<ApiResponse<Record<string, unknown>> | Record<string, unknown>>(
    `${WPF_APP_RELEASES_API}/${id}`,
    payload,
  )
  return normalizeWpfAppRelease(asRecord(unwrapWpfResponseData(response)))
}
