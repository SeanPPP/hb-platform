import { CopyOutlined } from '@ant-design/icons'
import { Button, Space, Tooltip, Typography } from 'antd'
import { useEffect, useRef, type MouseEvent } from 'react'
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
  className?: string
  gap?: number
  textNoWrap?: boolean
}

export default function BarcodePreview({
  value,
  options,
  align = 'center',
  textMaxWidth,
  showText = true,
  showCopy = true,
  compactCopy = false,
  className,
  gap = 4,
  textNoWrap = false,
}: BarcodePreviewProps) {
  const { t } = useTranslation()
  const canvasRef = useRef<HTMLCanvasElement | null>(null)
  // 调用方普遍以内联对象字面量传 options，引用每次渲染都会变。
  // 用序列化后的内容作依赖，保证只有条码值或绘制参数真正变化时才重新编码与绘制。
  const optionsKey = JSON.stringify(options ?? null)

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
    // options 的内容变化已由 optionsKey 表达，这里刻意不依赖 options 的引用。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [optionsKey, value])

  if (!value) {
    return <>--</>
  }

  const handleCopyClick = (event: MouseEvent<HTMLElement>) => {
    // 条码预览常被包在可双击编辑的单元格内，复制操作不能冒泡触发编辑态。
    event.stopPropagation()
    void copyTextToClipboard(value)
  }

  const stopCopyDoubleClick = (event: MouseEvent<HTMLElement>) => {
    event.stopPropagation()
  }

  return (
    <div
      className={className}
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: align === 'left' ? 'flex-start' : 'center',
        gap,
      }}
    >
      <canvas ref={canvasRef} />
      {showText ? (
        <Space size={4} wrap={!textNoWrap}>
          <Typography.Text
            style={{
              ...(textMaxWidth ? { maxWidth: textMaxWidth } : {}),
              ...(textNoWrap ? { whiteSpace: 'nowrap' } : {}),
            }}
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
                  onClick={handleCopyClick}
                  onDoubleClick={stopCopyDoubleClick}
                />
              </Tooltip>
            ) : (
              <Button size="small" type="link" onClick={handleCopyClick} onDoubleClick={stopCopyDoubleClick}>
                {t('common.copy', '复制')}
              </Button>
            )
          ) : null}
        </Space>
      ) : null}
    </div>
  )
}
