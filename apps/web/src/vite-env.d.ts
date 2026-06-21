/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL: string
  readonly VITE_DEV_PROXY_TARGET: string
  readonly VITE_CENTER_LOG_PROJECT?: string
  readonly VITE_CENTER_LOG_KEY?: string
  readonly VITE_CENTER_LOG_ENVIRONMENT?: string
  readonly VITE_CENTER_LOG_SERVICE_NAME?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
