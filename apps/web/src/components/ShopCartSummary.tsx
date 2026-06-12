import { Typography } from 'antd'
import { useTranslation } from 'react-i18next'
import type { StoreOrderCart } from '../types/storeOrder'

const { Text } = Typography

interface ShopCartSummaryProps {
  cart: StoreOrderCart | null
}

export default function ShopCartSummary({ cart }: ShopCartSummaryProps) {
  const { t } = useTranslation()
  const skuCount = cart?.items?.length ?? 0

  return (
    <div className="shop-cart-summary">
      <div>
        <Text type="secondary">{t('shop.sku', 'SKU')}:</Text> <Text strong>{skuCount}</Text>
      </div>
      <div>
        <Text type="secondary">{t('shop.total', 'Total')}:</Text>{' '}
        <Text strong style={{ color: '#1677ff' }}>
          ${(cart?.totalImportAmount ?? 0).toFixed(2)}
        </Text>
      </div>
      <div>
        <Text type="secondary">{t('shop.cbm', 'CBM')}:</Text>{' '}
        <Text strong>{(cart?.totalVolume ?? 0).toFixed(4)} m3</Text>
      </div>
    </div>
  )
}
