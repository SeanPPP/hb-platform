import {
  AudioOutlined,
  PauseCircleOutlined,
  PlayCircleOutlined,
  ScanOutlined,
  SoundOutlined,
} from '@ant-design/icons'
import { Button, Card, Image, Input, Space, Tag, Typography } from 'antd'
import { useState } from 'react'
import type { StoreOrderScanStatus } from '../types/storeOrder'

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
  onToggleEnabled: () => void
  onUnlockSound: () => void
  onManualSubmit: (barcode: string) => void
}

function getStatusTag(status: StoreOrderScanStatus, enabled: boolean) {
  switch (status) {
    case 'added':
      return <Tag color="success">Added</Tag>
    case 'multiple':
      return <Tag color="processing">Choose Item</Tag>
    case 'not_found':
      return <Tag color="warning">Not Found</Tag>
    case 'blocked':
      return <Tag color="gold">Store Required</Tag>
    case 'error':
      return <Tag color="error">Error</Tag>
    case 'scanning':
      return <Tag color="blue">Scanning</Tag>
    default:
      return enabled
        ? <Tag color="cyan">Ready</Tag>
        : <Tag color="default">Paused</Tag>
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
  onToggleEnabled,
  onUnlockSound,
  onManualSubmit,
}: ShopScanBarProps) {
  const [manualValue, setManualValue] = useState('')
  const [desktopVisible, setDesktopVisible] = useState(false)

  const helperText = enabled
    ? 'Scanner is listening when no text input is focused.'
    : 'Scanner is paused.'

  const hasProduct = status === 'added' || status === 'multiple'

  return (
    <>
      <Button
        className="shop-scan-toggle-btn"
        icon={<ScanOutlined />}
        onClick={() => setDesktopVisible((v) => !v)}
      >
        {desktopVisible ? 'Hide Scanner' : 'Scanner'}
      </Button>
      <Card
        className={`shop-scan-bar${desktopVisible ? ' shop-scan-bar-desktop-visible' : ''}`}
        bordered={false}
      >
        <div className="shop-scan-bar-header">
          <div>
            <div className="shop-scan-bar-title">
              <ScanOutlined />
              <span>Barcode Scan</span>
            </div>
            <Text type="secondary">{helperText}</Text>
          </div>
          <Space wrap>
            {getStatusTag(status, enabled)}
            <Button
              icon={enabled ? <PauseCircleOutlined /> : <PlayCircleOutlined />}
              onClick={onToggleEnabled}
            >
              {enabled ? 'Pause' : 'Resume'}
            </Button>
            <Button icon={<SoundOutlined />} type={soundEnabled ? 'default' : 'primary'} onClick={onUnlockSound}>
              {soundEnabled ? 'Sound Ready' : 'Enable Sound'}
            </Button>
          </Space>
        </div>

        <div className="shop-scan-bar-body">
          <div className="shop-scan-bar-feedback">
            <div className="shop-scan-bar-row">
              <Text type="secondary">Last barcode:</Text>
              <Text strong>{lastScannedCode || '-'}</Text>
            </div>
            <div className="shop-scan-bar-row">
              <Text type="secondary">Result:</Text>
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
                    <Tag color="blue">Cart: {lastCartTotalQuantity}</Tag>
                  ) : null}
                </div>
              </div>
            )}
          </div>

          <div className="shop-scan-bar-manual">
            <Input
              value={manualValue}
              onChange={(event) => setManualValue(event.target.value)}
              placeholder="Manual barcode input"
              prefix={<AudioOutlined />}
              onPressEnter={() => {
                const nextValue = manualValue.trim()
                if (!nextValue) {
                  return
                }

                onManualSubmit(nextValue)
                setManualValue('')
              }}
            />
            <Button
              type="primary"
              loading={busy}
              onClick={() => {
                const nextValue = manualValue.trim()
                if (!nextValue) {
                  return
                }

                onManualSubmit(nextValue)
                setManualValue('')
              }}
            >
              Search
            </Button>
          </div>
        </div>
      </Card>
    </>
  )
}
