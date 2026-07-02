import type {
  LinklyCloudCredentialAdminDto,
  PaymentTerminalEnvironment,
  PaymentTerminalEnvironmentStatusDto,
  UpdateLinklyCredentialRequest,
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
