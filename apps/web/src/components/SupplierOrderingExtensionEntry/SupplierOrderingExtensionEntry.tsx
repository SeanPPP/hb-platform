import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button, Modal } from 'antd'
import { ApiOutlined } from '@ant-design/icons'
import type { ApiResponse } from '../../types/api'
import request, { unwrapApiData } from '../../utils/request'
import {
  createNonce,
  OPEN_MESSAGE_TYPE,
  PING_MESSAGE_TYPE,
  PLATFORM_MESSAGE_SOURCE,
  resolveExtensionInstallExperience,
  resolveExtensionVersionStatus,
  validateExtensionOpenResultMessage,
  validateExtensionStatusMessage,
  type BrowserExtensionRelease,
} from './supplierOrderingExtensionLogic'
import './supplierOrderingExtension.css'

const RELEASE_API_PATH = '/api/react/v1/browser-extension/release'
const HANDSHAKE_TIMEOUT_MS = 1200

type EntryTone = 'checking' | 'missing' | 'ok' | 'optional' | 'forced'
type EntryPresentation = 'desktop' | 'mobile-nav'

interface SupplierOrderingExtensionEntryProps {
  presentation?: EntryPresentation
}

export default function SupplierOrderingExtensionEntry({
  presentation = 'desktop',
}: SupplierOrderingExtensionEntryProps) {
  const { t, i18n } = useTranslation()
  const userAgent = typeof navigator !== 'undefined' ? navigator.userAgent : ''
  const experience = typeof navigator !== 'undefined'
    ? resolveExtensionInstallExperience(userAgent, navigator.maxTouchPoints, navigator.platform)
    : 'desktop-unsupported'
  const supportsExtension = experience === 'desktop-edge'
    || experience === 'desktop-chrome'
    || experience === 'ios-safari'
  const isUnsupportedExperience = !supportsExtension

  const [release, setRelease] = useState<BrowserExtensionRelease | null>(null)
  const [releaseFailed, setReleaseFailed] = useState(false)
  const [installed, setInstalled] = useState(false)
  const [installedVersion, setInstalledVersion] = useState<string | null>(null)
  const [installedBrowser, setInstalledBrowser] = useState<string | null>(null)
  const [checking, setChecking] = useState(supportsExtension)
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
    if (!supportsExtension) {
      clearHandshakeTimeout()
      setInstalled(false)
      setInstalledVersion(null)
      setInstalledBrowser(null)
      setChecking(false)
      return
    }

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
  }, [clearHandshakeTimeout, supportsExtension])

  useEffect(() => {
    if (!supportsExtension) {
      return undefined
    }

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
  }, [supportsExtension])

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

  const statusLabel = isUnsupportedExperience
    ? t('supplierOrderingExtension.unsupportedShort')
    : checking
      ? t('supplierOrderingExtension.checking')
      : !installed
        ? t('supplierOrderingExtension.statusNotInstalled')
        : isForced
          ? t('supplierOrderingExtension.statusForcedUpdate')
          : isOptional
            ? t('supplierOrderingExtension.statusOptionalUpdate', { version: release?.latestVersion })
            : t('supplierOrderingExtension.statusInstalled', { version: installedVersion })
  const triggerLabel = presentation === 'mobile-nav'
    ? isUnsupportedExperience
      ? t('supplierOrderingExtension.unsupportedShort')
      : t('supplierOrderingExtension.name')
    : !checking && !installed
      ? isUnsupportedExperience
        ? t('supplierOrderingExtension.unsupportedShort')
        : t('supplierOrderingExtension.installAssistant')
      : statusLabel

  const noteLang = i18n.language?.startsWith('zh') ? 'zh' : 'en'

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

  const renderInstallLink = (browser: 'edge' | 'chrome' | 'safari') => {
    const url = browser === 'edge'
      ? release?.edgeStoreUrl
      : browser === 'safari'
        ? release?.safariStoreUrl
        : release?.chromeStoreUrl
    const label = browser === 'edge'
      ? t('supplierOrderingExtension.installEdge')
      : browser === 'safari'
        ? t('supplierOrderingExtension.installSafari')
        : t('supplierOrderingExtension.installChrome')
    const isPrimary = (experience === 'desktop-edge' && browser === 'edge')
      || (experience === 'desktop-chrome' && browser === 'chrome')
      || (experience === 'ios-safari' && browser === 'safari')

    if (!url) {
      return (
        <span
          key={browser}
          className={`soe-install-link soe-install-link--disabled${isPrimary ? ' soe-install-link--primary' : ''}`}
          aria-disabled="true"
        >
          <span className="soe-install-link-label">{label}</span>
          <span className="soe-install-link-state">
            {browser === 'safari'
              ? t('supplierOrderingExtension.safariNotPublished')
              : t('supplierOrderingExtension.notPublished')}
          </span>
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

  const unsupportedMessage = experience === 'desktop-safari-unsupported'
    ? t('supplierOrderingExtension.desktopSafariUnsupported')
    : experience === 'ios-unsupported'
      ? t('supplierOrderingExtension.iosBrowserUnsupported')
      : experience === 'android-unsupported'
        ? t('supplierOrderingExtension.androidUnsupported')
        : experience === 'desktop-unsupported'
          ? t('supplierOrderingExtension.desktopBrowserUnsupported')
          : null

  const renderSupportedInstallContent = () => {
    if (releaseFailed) {
      return <p className="soe-unavailable">{t('supplierOrderingExtension.releaseUnavailable')}</p>
    }
    if (!release) {
      return <p className="soe-unavailable">{t('supplierOrderingExtension.checking')}</p>
    }

    return (
      <div className="soe-install-list">
        {experience === 'desktop-edge' ? renderInstallLink('edge') : null}
        {experience === 'desktop-chrome' ? renderInstallLink('chrome') : null}
        {experience === 'ios-safari' ? renderInstallLink('safari') : null}
      </div>
    )
  }

  return (
    <div className={`soe-entry soe-entry--${tone} soe-entry--${presentation}`}>
      <Button
        size="small"
        className={`soe-entry-trigger soe-entry-trigger--${presentation}`}
        onClick={() => {
          setOpenError(null)
          setDialogOpen(true)
        }}
        aria-haspopup="dialog"
        aria-expanded={dialogOpen}
      >
        <span className="soe-entry-dot" aria-hidden="true" />
        {presentation === 'mobile-nav' ? <ApiOutlined className="soe-entry-mobile-icon" aria-hidden="true" /> : null}
        <span className="soe-entry-trigger-label">{triggerLabel}</span>
      </Button>

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
          {supportsExtension ? (
            <Button size="small" className="soe-dialog-recheck" onClick={runHandshake}>
              {t('supplierOrderingExtension.recheck')}
            </Button>
          ) : null}
        </div>

        {unsupportedMessage ? (
          <p className="soe-unsupported-hint" role="status">{unsupportedMessage}</p>
        ) : renderSupportedInstallContent()}

        {experience === 'ios-safari' && release?.safariStoreUrl ? (
          <div className="soe-safari-guide">
            <div className="soe-safari-guide-title">{t('supplierOrderingExtension.safariInstallIntro')}</div>
            <ol>
              <li>{t('supplierOrderingExtension.safariInstallStepStore')}</li>
              <li>{t('supplierOrderingExtension.safariInstallStepEnable')}</li>
              <li>{t('supplierOrderingExtension.safariInstallStepWebsite')}</li>
            </ol>
          </div>
        ) : null}

        {supportsExtension && release?.releaseNotes?.[noteLang] ? (
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
