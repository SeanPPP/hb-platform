export type RemoteOnlineStatus = 'online' | 'offline' | 'never'

export type RemoteServiceStatus =
  | 'notInstalled'
  | 'running'
  | 'stopped'
  | 'starting'
  | 'stopping'
  | 'checkFailed'

export interface RemoteMaintenanceDevice {
  id: string
  deviceRegistrationId: string
  storeCode: string
  deviceCode: string
  computerName: string
  rustdeskId: string
  clientVersion: string
  agentVersion: string
  onlineStatus: RemoteOnlineStatus
  serviceStatus: RemoteServiceStatus
  lastSeenAtUtc: string | null
  registeredAtUtc: string | null
  isStale: boolean
}

export interface RemoteMaintenanceDevicePage {
  items: RemoteMaintenanceDevice[]
  total: number
  page: number
  pageSize: number
  serverTimeUtc: string | null
}

export interface RemoteMaintenanceDeviceQuery {
  page: number
  pageSize: number
  keyword?: string
  storeCode?: string
  onlineStatus?: RemoteOnlineStatus
  serviceStatus?: RemoteServiceStatus
}

export interface RemoteMaintenanceArtifact {
  version: string
  fileName: string
  downloadUrl: string | null
  sha256: string
  sizeBytes: number
}

export interface RemoteMaintenanceManifest {
  idServer: string
  relayServer: string
  publicKey: string
  rustdesk: RemoteMaintenanceArtifact
  statusAgent: RemoteMaintenanceArtifact
}
