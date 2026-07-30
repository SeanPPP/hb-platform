export type ServiceApiTokenStatus = 'active' | 'revoked' | 'expired' | string
export type ServiceApiTokenPurpose =
  | 'mobile-ota-publisher'
  | 'pos-ipad-update-decision-reader'

export interface ServiceApiTokenCreateRequest {
  name: string
  purpose: ServiceApiTokenPurpose
}

export interface ServiceApiToken {
  id: string
  name: string
  tokenPrefix: string
  scopes: string[]
  status: ServiceApiTokenStatus
  createdAt?: string | null
  expiresAt?: string | null
  revokedAt?: string | null
  lastUsedAt?: string | null
  lastUsedIp?: string | null
}

export interface ServiceApiTokenCreateResponse extends ServiceApiToken {
  token: string
}
