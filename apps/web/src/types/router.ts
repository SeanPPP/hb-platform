import type { ReactElement } from 'react'
import type { AccessControl } from './auth'

export interface AppRouteMeta {
  title: string
  icon?: string
  hidden?: boolean
  affix?: boolean
  closable?: boolean
  keepAlive?: boolean
  accessKey?: keyof AccessControl
  activeMenu?: string
  dynamicTitle?: (params: Record<string, string>) => string
}

export interface AppRouteItem {
  path: string
  meta: AppRouteMeta
  element?: ReactElement
  children?: AppRouteItem[]
}

export interface TabItem {
  key: string
  path: string
  routePath: string
  title: string
  affix?: boolean
  closable?: boolean
  keepAlive?: boolean
}
