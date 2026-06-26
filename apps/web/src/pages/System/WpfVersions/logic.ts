import type { WpfAppRelease, WpfReleasePolicyRequest } from '../../../types/wpfVersion'

export const WPF_RELEASE_CHANNELS = ['production', 'preview'] as const

export function normalizeWpfReleaseChannel(channel?: string | null) {
  const normalized = channel?.trim().toLowerCase()
  return normalized || 'production'
}

export function isSupportedWpfInstallerFile(fileName: string) {
  return /\.(exe|msi)$/i.test(fileName.trim())
}

export function getWpfVersionErrorMessage(error: unknown, fallback: string) {
  if (error instanceof Error && error.message.trim()) {
    return error.message
  }

  return fallback
}

export function inferWpfInstallerType(fileName: string) {
  const normalized = fileName.trim().toLowerCase()
  return normalized.endsWith('.msi') ? 'msi' : 'exe'
}

export async function calculateFileSha256(file: Blob): Promise<string> {
  if (!globalThis.crypto?.subtle) {
    throw new Error('SHA-256 calculation is not available in this browser')
  }

  // 使用浏览器原生 Web Crypto 直接对安装包内容算哈希，避免人工录入校验值出错。
  const digest = await globalThis.crypto.subtle.digest('SHA-256', await file.arrayBuffer())
  return Array.from(new Uint8Array(digest))
    .map((byte) => byte.toString(16).padStart(2, '0'))
    .join('')
}

export function buildWpfPolicyPayload(input: WpfReleasePolicyRequest): WpfReleasePolicyRequest {
  return {
    channel: normalizeWpfReleaseChannel(input.channel),
    targetVersion: input.targetVersion.trim(),
    minimumSupportedVersion: input.minimumSupportedVersion.trim(),
    forceUpdate: input.forceUpdate,
    isRollback: input.isRollback,
    rollbackConfirmed: input.rollbackConfirmed,
  }
}

export function canSubmitWpfPolicy(input: Pick<WpfReleasePolicyRequest, 'targetVersion' | 'minimumSupportedVersion'>) {
  return Boolean(input.targetVersion.trim() && input.minimumSupportedVersion.trim())
}

export function getEffectiveWpfMinimumSupportedVersion(
  input: Pick<WpfReleasePolicyRequest, 'targetVersion' | 'minimumSupportedVersion'>,
) {
  const targetVersion = input.targetVersion.trim()
  const minimumSupportedVersion = input.minimumSupportedVersion.trim()

  if (!targetVersion) {
    return minimumSupportedVersion
  }

  if (!minimumSupportedVersion) {
    return targetVersion
  }

  const comparison = compareWpfVersion(minimumSupportedVersion, targetVersion)
  if (comparison !== null && comparison > 0) {
    return targetVersion
  }

  return minimumSupportedVersion
}

export function getWpfPolicyRangeError(
  input: Pick<WpfReleasePolicyRequest, 'targetVersion' | 'minimumSupportedVersion'>,
) {
  const targetVersion = input.targetVersion.trim()
  const minimumSupportedVersion = input.minimumSupportedVersion.trim()

  if (!targetVersion || !minimumSupportedVersion) {
    return null
  }

  // 中文注释：前端提交前先拦住 minimum > target，避免 set-current/rollback 复用旧 minimum 生成非法策略。
  const comparison = compareWpfVersion(minimumSupportedVersion, targetVersion)
  return comparison !== null && comparison > 0 ? 'INVALID_VERSION_RANGE' : null
}

export function isWpfRollbackTarget(
  targetVersion: string,
  releases: Array<Pick<WpfAppRelease, 'version' | 'isCurrent' | 'targetVersion'>>,
) {
  // 回滚确认以后台当前策略目标为基准，不能用当前分页里的最新活跃版本推断。
  const baselineVersion =
    releases.find((item) => item.targetVersion)?.targetVersion ??
    releases.find((item) => item.isCurrent)?.version ??
    null

  if (!baselineVersion) {
    return false
  }

  const comparison = compareWpfVersion(targetVersion, baselineVersion)
  return comparison !== null && comparison < 0
}

function compareWpfVersion(left: string, right: string) {
  const leftParts = parseWpfVersion(left)
  const rightParts = parseWpfVersion(right)
  if (!leftParts || !rightParts) {
    return null
  }

  for (let index = 0; index < Math.max(leftParts.length, rightParts.length); index += 1) {
    const delta = (leftParts[index] ?? 0) - (rightParts[index] ?? 0)
    if (delta !== 0) {
      return delta
    }
  }
  return 0
}

function parseWpfVersion(version: string) {
  const match = version.trim().match(/^v?(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?$/i)
  if (!match) {
    return null
  }

  return match.slice(1).map((part) => Number(part ?? 0))
}
