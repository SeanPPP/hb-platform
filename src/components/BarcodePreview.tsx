import { CopyOutlined } from '@ant-design/icons'
import { Button, Space, Tooltip, Typography } from 'antd'
import { useEffect, useRef } from 'react'
import type { BarcodeOptions } from '../utils/barcode'
import { renderBarcodeToCanvas } from '../utils/barcode'
import { copyTextToClipboard } from '../utils/clipboard'
import { useTranslation } from 'react-i18next'

interface BarcodePreviewProps {
  value?: string
  options?: BarcodeOptions
  align?: 'left' | 'center'
  textMaxWidth?: number
  showText?: boolean
  showCopy?: boolean
  compactCopy?: boolean
}

export default function BarcodePreview({
  value,
  options,
  align = 'center',
  textMaxWidth,
  showText = true,
  showCopy = true,
  compactCopy = false,
}: BarcodePreviewProps) {
  const { t } = useTranslation()
  const canvasRef = useRef<HTMLCanvasElement | null>(null)

  useEffect(() => {
    if (!canvasRef.current || !value) {
      return
    }

    try {
      const context = canvasRef.current.getContext('2d')
      context?.clearRect(0, 0, canvasRef.current.width, canvasRef.current.height)
      renderBarcodeToCanvas(canvasRef.current, value, {
        width: 1,
        height: 30,
        displayValue: false,
        margin: 0,
        ...options,
      })
    } catch (error) {
      console.error('渲染条码失败', error)
    }
  }, [options, value])

  if (!value) {
    return <>--</>
  }

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: align === 'left' ? 'flex-start' : 'center',
        gap: 4,
      }}
    >
      <canvas ref={canvasRef} />
      {showText ? (
        <Space size={4} wrap>
          <Typography.Text
            style={textMaxWidth ? { maxWidth: textMaxWidth } : undefined}
            ellipsis={Boolean(textMaxWidth) ? { tooltip: value } : false}
          >
            {value}
          </Typography.Text>
          {showCopy ? (
            compactCopy ? (
              <Tooltip title={t('common.copy', '复制')}>
                <Button
                  size="small"
                  type="text"
                  icon={<CopyOutlined />}
                  onClick={() => void copyTextToClipboard(value)}
                />
              </Tooltip>
            ) : (
              <Button size="small" type="link" onClick={() => void copyTextToClipboard(value)}>
                {t('common.copy', '复制')}
              </Button>
            )
          ) : null}
        </Space>
      ) : null}
    </div>
  )
}
