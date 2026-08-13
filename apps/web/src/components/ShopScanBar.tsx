import {
  AudioOutlined,
  CameraOutlined,
  PauseCircleOutlined,
  PlayCircleOutlined,
  ScanOutlined,
  SoundOutlined,
} from '@ant-design/icons'
import { Button, Card, Image, Input, Space, Tag, Typography } from 'antd'
import { useState } from 'react'
import type { TFunction } from 'i18next'
import { useTranslation } from 'react-i18next'
import type { StoreOrderScanStatus } from '../types/storeOrder'
import ShopCameraScanner, { type ShopCameraSubmitOutcome } from './ShopCameraScanner'

const { Text } = Typography

interface ShopScanBarProps {
  status: StoreOrderScanStatus
  lastScannedCode?: string
  lastProductName?: string
  lastProductImage?: string
  lastItemNumber?: string
  lastQuantity?: number
  lastCartTotalQuantity?: number
  lastMessage: string
  enabled: boolean
  soundEnabled: boolean
  busy: boolean
  cameraEnabled: boolean
  cameraPaused: boolean
  cameraSessionKey: string
  onToggleEnabled: () => void
  onToggleCamera: () => void
  onUnlockSound: () => void
  onManualSubmit: (barcode: string) => Promise<ShopCameraSubmitOutcome>
  onCameraSubmit: (barcode: string) => Promise<ShopCameraSubmitOutcome>
}

function getStatusTag(status: StoreOrderScanStatus, enabled: boolean, t: TFunction) {
  switch (status) {
    case 'added':
      return <Tag color="success">{t('shop.scan.added', 'Added')}</Tag>
    case 'multiple':
      return <Tag color="processing">{t('shop.scan.chooseItem', 'Choose Item')}</Tag>
    case 'not_found':
      return <Tag color="warning">{t('shop.scan.notFound', 'Not Found')}</Tag>
    case 'blocked':
      return <Tag color="gold">{t('shop.scan.storeRequired', 'Store Required')}</Tag>
    case 'error':
      return <Tag color="error">{t('shop.scan.error', 'Error')}</Tag>
    case 'scanning':
      return <Tag color="blue">{t('shop.scan.scanning', 'Scanning')}</Tag>
    default:
      return enabled
        ? <Tag color="cyan">{t('shop.scan.ready', 'Ready')}</Tag>
        : <Tag color="default">{t('shop.scan.paused', 'Paused')}</Tag>
  }
}

export default function ShopScanBar({
  status,
  lastScannedCode,
  lastProductName,
  lastProductImage,
  lastItemNumber,
  lastQuantity,
  lastCartTotalQuantity,
  lastMessage,
  enabled,
  soundEnabled,
  busy,
  cameraEnabled,
  cameraPaused,
  cameraSessionKey,
  onToggleEnabled,
  onToggleCamera,
  onUnlockSound,
  onManualSubmit,
  onCameraSubmit,
}: ShopScanBarProps) {
  const { t } = useTranslation()
  const [manualValue, setManualValue] = useState('')
  const [scannerVisible, setScannerVisible] = useState(false)

  const helperText = cameraEnabled
    ? t('shop.scan.cameraActiveHint', 'Camera scanning is active; scanner and manual input are paused.')
    : enabled
      ? t('shop.scan.listeningHint', 'Scanner is listening when no text input is focused.')
      : t('shop.scan.pausedHint', 'Scanner is paused.')

  const hasProduct = status === 'added' || status === 'multiple'
  const manualDisabled = cameraEnabled || cameraPaused

  const submitManualValue = () => {
    const nextValue = manualValue.trim()
    if (!nextValue) {
      return
    }

    void onManualSubmit(nextValue).then((outcome) => {
      if (outcome !== 'ignored') {
        setManualValue((current) => current.trim() === nextValue ? '' : current)
      }
    })
  }

  return (
    <>
      <Button
        className="shop-scan-toggle-btn"
        icon={<ScanOutlined />}
        aria-expanded={scannerVisible}
        aria-controls="shop-scan-panel"
        onClick={() => {
          // 收起扫码区域时同步释放相机，避免隐藏面板继续占用设备。
          if (scannerVisible && cameraEnabled) {
            onToggleCamera()
          }
          setScannerVisible((current) => !current)
        }}
      >
        {scannerVisible ? t('shop.scan.hideScanner', 'Hide Scanner') : t('shop.scan.barcodeScan', 'Barcode Scan')}
      </Button>
      <Card
        id="shop-scan-panel"
        className={`shop-scan-bar${scannerVisible ? ' shop-scan-bar-visible' : ''}`}
        bordered={false}
      >
        <div className="shop-scan-bar-header">
          <div>
            <div className="shop-scan-bar-title">
              <ScanOutlined />
              <span>{t('shop.scan.barcodeScan', 'Barcode Scan')}</span>
            </div>
            <Text type="secondary">{helperText}</Text>
          </div>
          <Space wrap>
            {getStatusTag(status, enabled, t)}
            <Button
              icon={enabled ? <PauseCircleOutlined /> : <PlayCircleOutlined />}
              disabled={cameraEnabled}
              onClick={onToggleEnabled}
            >
              {enabled ? t('shop.scan.pause', 'Pause') : t('shop.scan.resume', 'Resume')}
            </Button>
            <Button
              icon={<CameraOutlined />}
              type={cameraEnabled ? 'primary' : 'default'}
              disabled={!cameraEnabled && (busy || cameraPaused)}
              onClick={onToggleCamera}
            >
              {cameraEnabled
                ? t('shop.scan.cameraStop', 'Close Camera')
                : t('shop.scan.cameraStart', 'Open Camera')}
            </Button>
            <Button icon={<SoundOutlined />} type={soundEnabled ? 'default' : 'primary'} onClick={onUnlockSound}>
              {soundEnabled ? t('shop.scan.soundReady', 'Sound Ready') : t('shop.scan.enableSound', 'Enable Sound')}
            </Button>
          </Space>
        </div>

        {cameraEnabled ? (
          <ShopCameraScanner
            paused={cameraPaused}
            sessionKey={cameraSessionKey}
            onRequestClose={onToggleCamera}
            onSubmit={onCameraSubmit}
          />
        ) : null}

        <div className="shop-scan-bar-body">
          <div className="shop-scan-bar-feedback">
            <div className="shop-scan-bar-row">
              <Text type="secondary">{t('shop.scan.lastBarcode', 'Last barcode')}:</Text>
              <Text strong>{lastScannedCode || '-'}</Text>
            </div>
            <div className="shop-scan-bar-row">
              <Text type="secondary">{t('shop.scan.result', 'Result')}:</Text>
              <Text strong>{lastMessage}</Text>
            </div>
            {hasProduct && (
              <div className="shop-scan-bar-product">
                {lastProductImage && (
                  <Image
                    src={lastProductImage}
                    alt={lastProductName}
                    width={56}
                    height={56}
                    style={{ borderRadius: 8, objectFit: 'cover' }}
                    fallback="data:image/svg+xml;base64,PHN2ZyB3aWR0aD0iNTYiIGhlaWdodD0iNTYiIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyI+PHJlY3Qgd2lkdGg9IjU2IiBoZWlnaHQ9IjU2IiBmaWxsPSIjZjBmMGYwIi8+PHRleHQgeD0iMjgiIHk9IjMwIiBmb250LXNpemU9IjEyIiBmaWxsPSIjY2NjIiB0ZXh0LWFuY2hvcj0ibWlkZGxlIj5ObyBJbWc8L3RleHQ+PC9zdmc+"
                    preview={false}
                  />
                )}
                <div className="shop-scan-bar-product-info">
                  {lastItemNumber && (
                    <Text type="secondary" style={{ fontSize: 13 }}>{lastItemNumber}</Text>
                  )}
                  <Text strong ellipsis>{lastProductName || '-'}</Text>
                  {typeof lastQuantity === 'number' ? <Tag color="green">+{lastQuantity}</Tag> : null}
                  {typeof lastCartTotalQuantity === 'number' ? (
                    <Tag color="blue">{t('shop.cart', 'Cart')}: {lastCartTotalQuantity}</Tag>
                  ) : null}
                </div>
              </div>
            )}
          </div>

          <div className="shop-scan-bar-manual">
            <Input
              value={manualValue}
              disabled={manualDisabled}
              onChange={(event) => setManualValue(event.target.value)}
              placeholder={t('shop.scan.manualInput', 'Manual barcode input')}
              prefix={<AudioOutlined />}
              onPressEnter={submitManualValue}
            />
            <Button
              type="primary"
              loading={busy}
              disabled={manualDisabled}
              onClick={submitManualValue}
            >
              {t('common.search', 'Search')}
            </Button>
          </div>
        </div>
      </Card>
    </>
  )
}
