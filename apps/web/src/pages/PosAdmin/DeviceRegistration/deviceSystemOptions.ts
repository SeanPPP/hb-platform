export const REGISTERED_DEVICE_SYSTEM_OPTIONS = ['Windows', 'iPadOS', 'Other'] as const

export const APP_DEVICE_SYSTEM_OPTIONS = ['Android', 'iOS', 'iPadOS', 'Windows', 'Mac'] as const

export const EDITABLE_DEVICE_SYSTEM_OPTIONS = ['Android', 'iOS', 'iPadOS', 'Windows', 'Mac'] as const

const LEGACY_IOS_CANONICALIZATION_OPTIONS = ['iOS', 'iPadOS'] as const

export function isEnabledLegacyIosPos(
  status: number,
  currentSystem: string,
  deviceType: string,
): boolean {
  return (
    status === 1 &&
    currentSystem.trim().toLowerCase() === 'ios' &&
    deviceType.trim().toLowerCase() === 'pos'
  )
}

export function canEditRegisteredDeviceSystem(
  status: number,
  currentSystem: string,
  deviceType: string,
): boolean {
  return status === -1 || isEnabledLegacyIosPos(status, currentSystem, deviceType)
}

export function getRegisteredDeviceSystemEditOptions(
  status: number,
  currentSystem: string,
  deviceType: string,
): readonly string[] {
  if (isEnabledLegacyIosPos(status, currentSystem, deviceType)) {
    // 关键逻辑：已获批设备只能把等价旧值 iOS 单向规范化为 iPadOS。
    return LEGACY_IOS_CANONICALIZATION_OPTIONS
  }

  return EDITABLE_DEVICE_SYSTEM_OPTIONS
}
