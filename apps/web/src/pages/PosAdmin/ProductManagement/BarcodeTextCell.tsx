import { BarcodeOutlined, CopyOutlined } from '@ant-design/icons'
import { Button, Popover, Tooltip } from 'antd'
import { memo } from 'react'
import { useTranslation } from 'react-i18next'
import BarcodePreview from '../../../components/BarcodePreview'
import { copyTextToClipboard } from '../../../utils/clipboard'

// 悬停时条形码的绘制参数。定义在模块级，保证引用稳定。
const HOVER_BARCODE_OPTIONS = { height: 48, width: 2, margin: 0 }

interface BarcodeTextCellProps {
  value?: string
}

/**
 * 商品列表的条码单元格：表格内只显示等宽数字，悬停条码图标时才绘制条形码。
 *
 * 原先每行都常驻一个 JsBarcode canvas，一页 50 行就是 50 次编码与绘制，
 * 虚拟滚动复用行时还会反复重画。列表里真正需要扫码的场景很少，
 * 因此改为按需绘制：Popover 内容只在首次打开时挂载，关闭后销毁。
 */
function BarcodeTextCell({ value }: BarcodeTextCellProps) {
  const { t } = useTranslation()

  if (!value) {
    return <>--</>
  }

  return (
    <span className="pos-products-barcode-text-cell">
      <span className="pos-products-barcode-text" title={value}>
        {value}
      </span>
      <Popover
        destroyOnHidden
        mouseEnterDelay={0.15}
        content={<BarcodePreview value={value} options={HOVER_BARCODE_OPTIONS} showCopy={false} />}
      >
        <Button
          size="small"
          type="text"
          icon={<BarcodeOutlined />}
          aria-label={t('posAdmin.products.showBarcodeImage', '查看条形码')}
        />
      </Popover>
      <Tooltip title={t('common.copy', '复制')}>
        <Button
          size="small"
          type="text"
          icon={<CopyOutlined />}
          aria-label={t('common.copy', '复制')}
          onClick={() => void copyTextToClipboard(value)}
        />
      </Tooltip>
    </span>
  )
}

export default memo(BarcodeTextCell)
