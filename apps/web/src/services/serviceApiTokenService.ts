import type { ApiResponse } from '../types/api'
import type { ServiceApiToken, ServiceApiTokenCreateResponse } from '../types/serviceApiToken'
import request, { unwrapApiData } from '../utils/request'

const SERVICE_API_TOKENS_API = '/api/service-api-tokens'

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' ? (value as Record<string, unknown>) : {}
}

function getString(raw: Record<string, unknown>, key: string) {
  const value = raw[key]
  return typeof value === 'string' ? value : null
}

function getStringArray(raw: Record<string, unknown>, key: string) {
  const value = raw[key]
  if (Array.isArray(value)) {
    return value.filter((item): item is string => typeof item === 'string')
  }
  if (typeof value === 'string' && value.trim()) {
    return value
      .split(/[;,]/)
      .map((item) => item.trim())
      .filter(Boolean)
  }
  return []
}

export function normalizeServiceApiToken(raw: Record<string, unknown>): ServiceApiToken {
  return {
    id: getString(raw, 'id') ?? '',
    name: getString(raw, 'name') ?? '',
    tokenPrefix: getString(raw, 'tokenPrefix') ?? '',
    scopes: getStringArray(raw, 'scopes'),
    status: getString(raw, 'status') ?? 'active',
    createdAt: getString(raw, 'createdAt'),
    expiresAt: getString(raw, 'expiresAt'),
    revokedAt: getString(raw, 'revokedAt'),
    lastUsedAt: getString(raw, 'lastUsedAt'),
    lastUsedIp: getString(raw, 'lastUsedIp'),
  }
}

export function normalizeServiceApiTokenCreateResponse(
  raw: Record<string, unknown>,
): ServiceApiTokenCreateResponse {
  return {
    ...normalizeServiceApiToken(raw),
    token: getString(raw, 'token') ?? '',
  }
}

export async function getServiceApiTokens() {
  const response = await request.get<ApiResponse<Record<string, unknown>[]> | Record<string, unknown>[]>(
    SERVICE_API_TOKENS_API,
  )
  const payload = unwrapApiData(response)
  return Array.isArray(payload) ? payload.map((item) => normalizeServiceApiToken(asRecord(item))) : []
}

export async function createServiceApiToken(name: string) {
  const response = await request.post<
    ApiResponse<Record<string, unknown>> | Record<string, unknown>
  >(SERVICE_API_TOKENS_API, { name })
  return normalizeServiceApiTokenCreateResponse(asRecord(unwrapApiData(response)))
}

export async function revokeServiceApiToken(id: string) {
  const response = await request.post<ApiResponse<Record<string, unknown>> | Record<string, unknown>>(
    `${SERVICE_API_TOKENS_API}/${encodeURIComponent(id)}/revoke`,
    {},
  )
  return normalizeServiceApiToken(asRecord(unwrapApiData(response)))
}
