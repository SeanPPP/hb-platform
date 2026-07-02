export type PaymentTerminalEnvironment = 'Production' | 'Sandbox'

export interface PaymentTerminalEnvironmentStatusDto {
  environment: PaymentTerminalEnvironment
  configured: boolean
  enabled: boolean
  updatedAtUtc?: string | null
  updatedBy?: string | null
}

export interface PaymentTerminalStoreOptionDto {
  storeCode: string
  storeName: string
}

export interface LinklyCloudCredentialAdminDto {
  storeCode: string
  environment: PaymentTerminalEnvironment
  username?: string | null
  hasPassword: boolean
  updatedAtUtc?: string | null
  updatedBy?: string | null
}

export interface PaymentTerminalSettingsDto {
  square: PaymentTerminalEnvironmentStatusDto[]
  stores: PaymentTerminalStoreOptionDto[]
  selectedStoreCode?: string | null
  linkly: LinklyCloudCredentialAdminDto[]
}

export interface UpdateSquareTokenRequest {
  environment: PaymentTerminalEnvironment
  accessToken?: string
  clearToken: boolean
}

export interface UpdateLinklyCredentialRequest {
  storeCode: string
  environment: PaymentTerminalEnvironment
  username?: string
  password?: string
  clearCredential: boolean
}
