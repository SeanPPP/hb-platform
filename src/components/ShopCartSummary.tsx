import { Typography } from 'antd'
import type { StoreOrderCart } from '../types/storeOrder'

const { Text } = Typography

interface ShopCartSummaryProps {
  cart: StoreOrderCart | null
}

export default function ShopCartSummary({ cart }: ShopCartSummaryProps) {
  const skuCount = cart?.items?.length ?? 0

  return (
    <div className="shop-cart-summary">
      <div>
        <Text type="secondary">SKU:</Text> <Text strong>{skuCount}</Text>
      </div>
      <div>
        <Text type="secondary">Total:</Text>{' '}
        <Text strong style={{ color: '#1677ff' }}>
          ${(cart?.totalImportAmount ?? 0).toFixed(2)}
        </Text>
      </div>
      <div>
        <Text type="secondary">CBM:</Text>{' '}
        <Text strong>{(cart?.totalVolume ?? 0).toFixed(4)} m3</Text>
      </div>
    </div>
  )
}
