import type {
  CreateLinklyTerminalRequest,
  LinklyCloudCredentialAdminDto,
  LinklyTerminalAdminDto,
  LinklyTerminalManagementDto,
  PaymentTerminalEnvironment,
  PaymentTerminalEnvironmentStatusDto,
  UpdateLinklyCredentialRequest,
  UpdateLinklyTerminalRequest,
  UpdateSquareTokenRequest,
} from '../../../types/paymentTerminalSettings'

export interface SquareTokenFormValues {
  accessToken: string
  clearToken: boolean
}

export interface LinklyCredentialFormValues {
  username: string
  password: string
  clearCredential: boolean
}

export interface LinklyTerminalFormValues {
  laneNo: number
  displayName: string
  username: string
  password: string
}

export function createSquareTokenFormValues(): SquareTokenFormValues {
  return {
    accessToken: '',
    clearToken: false,
  }
}

export function createLinklyCredentialFormValues(
  credential?: LinklyCloudCredentialAdminDto | null,
): LinklyCredentialFormValues {
  return {
    username: credential?.username ?? '',
    password: '',
    clearCredential: false,
  }
}

export function createLinklyTerminalFormValues(
  terminal?: LinklyTerminalAdminDto | null,
): LinklyTerminalFormValues {
  return {
    laneNo: terminal?.laneNo ?? 1,
    displayName: terminal?.displayName ?? '',
    // 后端只返回掩码用户名；编辑时留空代表保留现有凭据。
    username: '',
    password: '',
  }
}

export function buildSquareTokenPayload(
  environment: PaymentTerminalEnvironment,
  values: SquareTokenFormValues,
): UpdateSquareTokenRequest {
  const payload: UpdateSquareTokenRequest = {
    environment,
    clearToken: values.clearToken,
  }

  // 清除或留空时不提交 token 明文；后端据此清除或保留原 token。
  const token = values.accessToken.trim()
  if (!values.clearToken && token) {
    payload.accessToken = token
  }

  return payload
}

export function buildLinklyCredentialPayload(
  storeCode: string,
  environment: PaymentTerminalEnvironment,
  values: LinklyCredentialFormValues,
): UpdateLinklyCredentialRequest {
  const payload: UpdateLinklyCredentialRequest = {
    storeCode,
    environment,
    clearCredential: values.clearCredential,
  }

  const username = values.username.trim()
  const password = values.password.trim()
  if (!values.clearCredential && username) {
    payload.username = username
  }
  // 密码留空表示保留旧密码；清除时也不发送密码，避免无意义地传输密钥。
  if (!values.clearCredential && password) {
    payload.password = password
  }

  return payload
}

export function buildCreateLinklyTerminalPayload(
  storeCode: string,
  environment: PaymentTerminalEnvironment,
  values: LinklyTerminalFormValues,
): CreateLinklyTerminalRequest {
  return {
    storeCode,
    environment,
    laneNo: values.laneNo,
    displayName: values.displayName.trim(),
    username: values.username.trim(),
    password: values.password.trim(),
  }
}

export function buildUpdateLinklyTerminalPayload(
  storeCode: string,
  environment: PaymentTerminalEnvironment,
  values: LinklyTerminalFormValues,
): UpdateLinklyTerminalRequest {
  const payload: UpdateLinklyTerminalRequest = {
    storeCode,
    environment,
    laneNo: values.laneNo,
    displayName: values.displayName.trim(),
  }

  const username = values.username.trim()
  const password = values.password.trim()
  if (username) {
    payload.username = username
  }
  if (password) {
    payload.password = password
  }
  return payload
}

export function canActivateLinklyConfiguration(management?: LinklyTerminalManagementDto | null) {
  if (!management || management.mode === 'Active') {
    return false
  }

  const readyTerminalIds = new Set(
    management.terminals
      .filter((terminal) => terminal.pairingState === 'Ready')
      .map((terminal) => terminal.terminalId),
  )
  if (readyTerminalIds.size === 0) {
    return false
  }

  const enabledDevices = management.devices.filter((device) => device.enabled)
  const selectedTerminalIds = enabledDevices.flatMap((device) => device.terminalId ? [device.terminalId] : [])
  return selectedTerminalIds.length === enabledDevices.length
    && new Set(selectedTerminalIds).size === selectedTerminalIds.length
    && selectedTerminalIds.every((terminalId) => readyTerminalIds.has(terminalId))
}

export function getLinklyTerminalAssignmentOwner(
  management: LinklyTerminalManagementDto | null | undefined,
  terminalId: string,
  currentDeviceCode: string,
) {
  return management?.devices.find((device) => (
    device.deviceCode !== currentDeviceCode && device.terminalId === terminalId
  ))?.deviceCode ?? null
}

export function getEnvironmentStatus<T extends { environment: PaymentTerminalEnvironment }>(
  statuses: T[],
  environment: PaymentTerminalEnvironment,
): T | undefined {
  return statuses.find((status) => status.environment === environment)
}

export function isConfiguredStatus(status?: PaymentTerminalEnvironmentStatusDto | LinklyCloudCredentialAdminDto) {
  if (!status) {
    return false
  }
  return 'hasPassword' in status ? status.hasPassword : status.configured
}

export function resolvePaymentTerminalSettingsErrorMessage(error: unknown, fallback: string) {
  return error instanceof Error && error.message.trim() ? error.message : fallback
}
