import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button, Modal } from 'antd'
import type { ApiResponse } from '../../types/api'
import request, { unwrapApiData } from '../../utils/request'
import {
  createNonce,
  detectBrowser,
  isMobileBrowser,
  OPEN_MESSAGE_TYPE,
  PING_MESSAGE_TYPE,
  PLATFORM_MESSAGE_SOURCE,
  resolveExtensionVersionStatus,
  validateExtensionOpenResultMessage,
  validateExtensionStatusMessage,
  type BrowserExtensionRelease,
  type DetectedBrowser,
} from './supplierOrderingExtensionLogic'
import './supplierOrderingExtension.css'

const RELEASE_API_PATH = '/api/react/v1/browser-extension/release'
const HANDSHAKE_TIMEOUT_MS = 1200

type EntryTone = 'checking' | 'missing' | 'ok' | 'optional' | 'forced'

export default function SupplierOrderingExtensionEntry() {
  const { t, i18n } = useTranslation()

  const [release, setRelease] = useState<BrowserExtensionRelease | null>(null)
  const [releaseFailed, setReleaseFailed] = useState(false)
  const [installed, setInstalled] = useState(false)
  const [installedVersion, setInstalledVersion] = useState<string | null>(null)
  const [installedBrowser, setInstalledBrowser] = useState<string | null>(null)
  const [checking, setChecking] = useState(true)
  const [dialogOpen, setDialogOpen] = useState(false)
  const [openError, setOpenError] = useState<string | null>(null)

  const nonceRef = useRef('')
  const openNonceRef = useRef('')
  const timeoutRef = useRef<number | null>(null)

  const clearHandshakeTimeout = useCallback(() => {
    if (timeoutRef.current !== null) {
      window.clearTimeout(timeoutRef.current)
      timeoutRef.current = null
    }
  }, [])

  const runHandshake = useCallback(() => {
    const nonce = createNonce()
    nonceRef.current = nonce
    clearHandshakeTimeout()
    setChecking(true)

    // 只向当前页面同源窗口投递，不携带任何 token / 账号 / 销售额信息。
    window.postMessage(
      { source: PLATFORM_MESSAGE_SOURCE, type: PING_MESSAGE_TYPE, nonce },
      window.location.origin,
    )

    timeoutRef.current = window.setTimeout(() => {
      timeoutRef.current = null
      // 仅当仍是本次握手等待期才判定为未安装/被禁用/超时。
      if (nonceRef.current === nonce) {
        setInstalled(false)
        setInstalledVersion(null)
        setInstalledBrowser(null)
        setChecking(false)
      }
    }, HANDSHAKE_TIMEOUT_MS)
  }, [clearHandshakeTimeout])

  useEffect(() => {
    let cancelled = false

    const loadRelease = async () => {
      try {
        const response = await request.get<ApiResponse<BrowserExtensionRelease>>(RELEASE_API_PATH)
        const data = unwrapApiData(response)
        if (!cancelled) {
          setRelease(data)
        }
      } catch {
        if (!cancelled) {
          setReleaseFailed(true)
        }
      }
    }

    void loadRelease()

    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    const onMessage = (event: MessageEvent) => {
      const result = validateExtensionStatusMessage(event.data, {
        eventSource: event.source,
        windowObject: window,
        messageOrigin: event.origin,
        windowOrigin: window.location.origin,
        expectedNonce: nonceRef.current,
      })
      if (!result.ok) {
        const openResult = validateExtensionOpenResultMessage(event.data, {
          eventSource: event.source,
          windowObject: window,
          messageOrigin: event.origin,
          windowOrigin: window.location.origin,
          expectedNonce: openNonceRef.current,
        })
        if (!openResult.ok) return
        if (openResult.opened) {
          setOpenError(null)
          setDialogOpen(false)
        } else {
          setOpenError(openResult.error || '')
        }
        return
      }

      clearHandshakeTimeout()
      setInstalled(true)
      setInstalledVersion(result.version)
      setInstalledBrowser(result.browser)
      setChecking(false)
    }

    window.addEventListener('message', onMessage)
    return () => window.removeEventListener('message', onMessage)
  }, [clearHandshakeTimeout])

  useEffect(() => {
    runHandshake()
  }, [runHandshake])

  useEffect(() => {
    const onFocus = () => runHandshake()
    const onVisibilityChange = () => {
      if (document.visibilityState === 'visible') {
        runHandshake()
      }
    }

    window.addEventListener('focus', onFocus)
    document.addEventListener('visibilitychange', onVisibilityChange)
    return () => {
      window.removeEventListener('focus', onFocus)
      document.removeEventListener('visibilitychange', onVisibilityChange)
    }
  }, [runHandshake])

  useEffect(() => () => clearHandshakeTimeout(), [clearHandshakeTimeout])

  const versionStatus = installed && installedVersion && release
    ? resolveExtensionVersionStatus(installedVersion, release.minimumVersion, release.latestVersion)
    : null
  const isForced = versionStatus === 'forced'
  const isOptional = versionStatus === 'optional'

  let tone: EntryTone
  if (checking) {
    tone = 'checking'
  } else if (!installed) {
    tone = 'missing'
  } else if (isForced) {
    tone = 'forced'
  } else if (isOptional) {
    tone = 'optional'
  } else {
    tone = 'ok'
  }

  const statusLabel = checking
    ? t('supplierOrderingExtension.checking')
    : !installed
      ? t('supplierOrderingExtension.statusNotInstalled')
      : isForced
        ? t('supplierOrderingExtension.statusForcedUpdate')
        : isOptional
          ? t('supplierOrderingExtension.statusOptionalUpdate', { version: release?.latestVersion })
          : t('supplierOrderingExtension.statusInstalled', { version: installedVersion })

  const noteLang = i18n.language?.startsWith('zh') ? 'zh' : 'en'
  const userAgent = typeof navigator !== 'undefined' ? navigator.userAgent : ''
  const detectedBrowser: DetectedBrowser =
    typeof navigator !== 'undefined' ? detectBrowser(userAgent) : 'other'
  const isMobile = typeof navigator !== 'undefined'
    ? isMobileBrowser(userAgent, navigator.maxTouchPoints, navigator.platform)
    : false

  const handleOpenAssistant = () => {
    // 打开扩展助手时只发送随机 nonce，绝不传递 token / account / sales。
    const nonce = createNonce()
    openNonceRef.current = nonce
    setOpenError(null)
    window.postMessage(
      { source: PLATFORM_MESSAGE_SOURCE, type: OPEN_MESSAGE_TYPE, nonce },
      window.location.origin,
    )
  }

  const renderInstallLink = (browser: 'edge' | 'chrome') => {
    const url = browser === 'edge' ? release?.edgeStoreUrl : release?.chromeStoreUrl
    const label = browser === 'edge'
      ? t('supplierOrderingExtension.installEdge')
      : t('supplierOrderingExtension.installChrome')
    const isPrimary = detectedBrowser === browser

    if (!url) {
      return (
        <span
          key={browser}
          className={`soe-install-link soe-install-link--disabled${isPrimary ? ' soe-install-link--primary' : ''}`}
          aria-disabled="true"
        >
          <span className="soe-install-link-label">{label}</span>
          <span className="soe-install-link-state">{t('supplierOrderingExtension.notPublished')}</span>
        </span>
      )
    }

    return (
      <a
        key={browser}
        className={`soe-install-link${isPrimary ? ' soe-install-link--primary' : ''}`}
        href={url}
        target="_blank"
        rel="noopener noreferrer"
      >
        <span className="soe-install-link-label">{label}</span>
        {isPrimary ? <span className="soe-install-link-badge">{t('supplierOrderingExtension.recommended')}</span> : null}
      </a>
    )
  }

  return (
    <div className={`soe-entry soe-entry--${tone}`}>
      <button
        type="button"
        className="soe-entry-main"
        onClick={() => {
          setOpenError(null)
          setDialogOpen(true)
        }}
        aria-haspopup="dialog"
        aria-expanded={dialogOpen}
      >
        <span className="soe-entry-dot" aria-hidden="true" />
        <span className="soe-entry-name">{t('supplierOrderingExtension.name')}</span>
        <span className="soe-entry-status">{statusLabel}</span>
        {!checking && !installed ? (
          <span className="soe-entry-action">{t('supplierOrderingExtension.installAssistant')}</span>
        ) : null}
      </button>
      <button type="button" className="soe-entry-recheck" onClick={runHandshake}>
        {t('supplierOrderingExtension.recheck')}
      </button>

      <Modal
        className="soe-dialog"
        open={dialogOpen}
        title={t('supplierOrderingExtension.name')}
        onCancel={() => setDialogOpen(false)}
        footer={null}
      >
        <div className="soe-dialog-status">
          <span className={`soe-entry-dot soe-entry-dot--${tone}`} aria-hidden="true" />
          <span className="soe-dialog-status-text">{statusLabel}</span>
          {installed && installedVersion ? (
            <span className="soe-dialog-version">
              {t('supplierOrderingExtension.version')} {installedVersion}
            </span>
          ) : null}
          {installedBrowser ? (
            <span className="soe-dialog-browser">{installedBrowser}</span>
          ) : null}
        </div>

        {isMobile ? (
          <p className="soe-mobile-hint">{t('supplierOrderingExtension.mobileHint')}</p>
        ) : releaseFailed ? (
          <p className="soe-unavailable">{t('supplierOrderingExtension.releaseUnavailable')}</p>
        ) : !release ? (
          <p className="soe-unavailable">{t('supplierOrderingExtension.checking')}</p>
        ) : (
          <div className="soe-install-list">
            {renderInstallLink('edge')}
            {renderInstallLink('chrome')}
          </div>
        )}

        {release?.releaseNotes?.[noteLang] ? (
          <div className="soe-notes">
            <div className="soe-notes-title">{t('supplierOrderingExtension.releaseNotes')}</div>
            <p>{release.releaseNotes[noteLang]}</p>
          </div>
        ) : null}

        {openError !== null ? (
          <p className="soe-unavailable" role="alert">
            {openError || t('supplierOrderingExtension.openFailed')}
          </p>
        ) : null}

        {installed && !isForced ? (
          <Button block type="primary" className="soe-open" onClick={handleOpenAssistant}>
            {t('supplierOrderingExtension.openAssistant')}
          </Button>
        ) : null}
      </Modal>
    </div>
  )
}
