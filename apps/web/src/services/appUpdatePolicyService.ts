import type { ApiResponse } from '../types/api'
import type {
  AppUpdateApp,
  AppUpdateTargetScope,
  AppUpdateTargetStoreOption,
  IosAppStoreRelease,
  IosAppStoreReleaseCreateRequest,
  NativeUpdatePolicy,
  NativeUpdatePolicyRequest,
  PosIpadNativeUpdatePolicyRequest,
  PosIpadOtaRelease,
  PosIpadOtaRollout,
  PosIpadOtaRolloutRequest,
} from '../types/appUpdatePolicy'
import request, { unwrapApiData } from '../utils/request'

type TransportOptions = {
  params?: Record<string, unknown>
}

export interface AppUpdatePolicyTransport {
  get(url: string, options?: TransportOptions): Promise<unknown>
  post(url: string, payload?: unknown): Promise<unknown>
  put(url: string, payload?: unknown): Promise<unknown>
}

const defaultTransport: AppUpdatePolicyTransport = {
  get: (url, options) => request.get(url, options),
  post: (url, payload) => request.post(url, payload),
  put: (url, payload) => request.put(url, payload),
}

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : {}
}

function text(raw: Record<string, unknown>, key: string) {
  const value = raw[key]
  return typeof value === 'string' ? value : ''
}

function nullableText(raw: Record<string, unknown>, key: string) {
  const value = text(raw, key).trim()
  return value || null
}

function boolean(raw: Record<string, unknown>, key: string) {
  return raw[key] === true
}

function number(raw: Record<string, unknown>, key: string) {
  const value = Number(raw[key])
  return Number.isFinite(value) ? value : 0
}

function targetScope(raw: Record<string, unknown>): AppUpdateTargetScope {
  return raw.targetScope === 'stores' ? 'stores' : 'all'
}

function stringArray(raw: Record<string, unknown>, key: string) {
  return Array.isArray(raw[key])
    ? (raw[key] as unknown[]).filter((value): value is string => typeof value === 'string')
    : []
}

function unwrap<T>(payload: unknown) {
  return unwrapApiData(payload as ApiResponse<T> | T)
}

export function normalizeIosAppStoreRelease(value: unknown): IosAppStoreRelease {
  const raw = asRecord(value)
  return {
    id: text(raw, 'id'),
    app: raw.app === 'pos-ipad' ? 'pos-ipad' : 'mobile-ios',
    appStoreId: text(raw, 'appStoreId'),
    bundleIdentifier: text(raw, 'bundleIdentifier'),
    version: text(raw, 'version'),
    buildNumber: text(raw, 'buildNumber'),
    storefront: text(raw, 'storefront'),
    appStoreUrl: text(raw, 'appStoreUrl'),
    appleVerifiedAtUtc: text(raw, 'appleVerifiedAtUtc'),
    createdAt: text(raw, 'createdAt'),
    createdBy: nullableText(raw, 'createdBy'),
  }
}

export function normalizeNativeUpdatePolicy(value: unknown): NativeUpdatePolicy {
  const raw = asRecord(value)
  return {
    id: nullableText(raw, 'id'),
    enabled: boolean(raw, 'enabled'),
    policyVersion: number(raw, 'policyVersion'),
    releaseId: nullableText(raw, 'releaseId'),
    latestVersion: nullableText(raw, 'latestVersion'),
    minimumSupportedVersion: nullableText(raw, 'minimumSupportedVersion'),
    appStoreUrl: nullableText(raw, 'appStoreUrl'),
    releaseMessage: nullableText(raw, 'releaseMessage'),
    targetScope: targetScope(raw),
    targetStoreGuids: stringArray(raw, 'targetStoreGuids'),
    updatedAt: nullableText(raw, 'updatedAt'),
    updatedBy: nullableText(raw, 'updatedBy'),
  }
}

export function normalizeAppUpdateStoreOption(value: unknown): AppUpdateTargetStoreOption {
  const raw = asRecord(value)
  return {
    storeGuid: text(raw, 'storeGuid'),
    storeCode: text(raw, 'storeCode'),
    storeName: text(raw, 'storeName'),
  }
}

export function normalizePosIpadOtaRelease(value: unknown): PosIpadOtaRelease {
  const raw = asRecord(value)
  return {
    id: text(raw, 'id'),
    environment: text(raw, 'environment'),
    updateGroupId: text(raw, 'updateGroupId'),
    iosUpdateId: text(raw, 'iosUpdateId'),
    channel: text(raw, 'channel'),
    runtimeVersion: text(raw, 'runtimeVersion'),
    gitCommitHash: nullableText(raw, 'gitCommitHash'),
    dashboardUrl: nullableText(raw, 'dashboardUrl'),
    publishedAtUtc: text(raw, 'publishedAtUtc'),
    isRollback: boolean(raw, 'isRollback'),
    rollbackOfReleaseId: nullableText(raw, 'rollbackOfReleaseId'),
    createdAt: text(raw, 'createdAt'),
    createdBy: nullableText(raw, 'createdBy'),
  }
}

export function normalizePosIpadOtaRollout(value: unknown): PosIpadOtaRollout {
  const raw = asRecord(value)
  const release = raw.release
  return {
    id: nullableText(raw, 'id'),
    enabled: boolean(raw, 'enabled'),
    policyVersion: number(raw, 'policyVersion'),
    releaseId: nullableText(raw, 'releaseId'),
    forceUpdate: boolean(raw, 'forceUpdate'),
    targetScope: targetScope(raw),
    targetStoreGuids: stringArray(raw, 'targetStoreGuids'),
    releaseMessage: nullableText(raw, 'releaseMessage'),
    release: release && typeof release === 'object'
      ? normalizePosIpadOtaRelease(release)
      : null,
    updatedAt: nullableText(raw, 'updatedAt'),
    updatedBy: nullableText(raw, 'updatedBy'),
  }
}

export function createAppUpdatePolicyService(transport: AppUpdatePolicyTransport) {
  return {
    async getIosAppStoreReleases(app: AppUpdateApp) {
      const response = await transport.get('/api/app-update-releases/ios', {
        params: { app, storefront: 'au' },
      })
      const payload = unwrap<unknown[]>(response)
      return Array.isArray(payload) ? payload.map(normalizeIosAppStoreRelease) : []
    },

    async createIosAppStoreRelease(payload: IosAppStoreReleaseCreateRequest) {
      const response = await transport.post('/api/app-update-releases/ios', payload)
      return normalizeIosAppStoreRelease(unwrap(response))
    },

    async getMobileIosNativePolicy() {
      const response = await transport.get('/api/app-update-policies/mobile-ios')
      return normalizeNativeUpdatePolicy(unwrap(response))
    },

    async saveMobileIosNativePolicy(payload: NativeUpdatePolicyRequest) {
      const response = await transport.put('/api/app-update-policies/mobile-ios', payload)
      return normalizeNativeUpdatePolicy(unwrap(response))
    },

    async getPosIpadNativePolicy() {
      const response = await transport.get('/api/app-update-policies/pos-ipad/native')
      return normalizeNativeUpdatePolicy(unwrap(response))
    },

    async savePosIpadNativePolicy(payload: PosIpadNativeUpdatePolicyRequest) {
      const response = await transport.put('/api/app-update-policies/pos-ipad/native', payload)
      return normalizeNativeUpdatePolicy(unwrap(response))
    },

    async getPosIpadStoreOptions() {
      const response = await transport.get('/api/app-update-policies/pos-ipad/store-options')
      const payload = unwrap<unknown[]>(response)
      return Array.isArray(payload) ? payload.map(normalizeAppUpdateStoreOption) : []
    },

    async getPosIpadOtaReleases() {
      const response = await transport.get('/api/pos-ipad/ota-releases')
      const payload = unwrap<unknown[]>(response)
      return Array.isArray(payload) ? payload.map(normalizePosIpadOtaRelease) : []
    },

    async getPosIpadOtaRollout() {
      const response = await transport.get('/api/pos-ipad/ota-rollout')
      return normalizePosIpadOtaRollout(unwrap(response))
    },

    async savePosIpadOtaRollout(payload: PosIpadOtaRolloutRequest) {
      const response = await transport.put('/api/pos-ipad/ota-rollout', payload)
      return normalizePosIpadOtaRollout(unwrap(response))
    },
  }
}

export const appUpdatePolicyService = createAppUpdatePolicyService(defaultTransport)
