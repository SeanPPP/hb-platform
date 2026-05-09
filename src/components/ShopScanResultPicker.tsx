import { CheckOutlined } from '@ant-design/icons'
import { Button, Image, Modal, Space, Tag, Typography } from 'antd'
import type { StoreOrderDynamicData, StoreOrderProductItem } from '../types/storeOrder'

const { Text } = Typography

interface ShopScanResultPickerProps {
  open: boolean
  loading: boolean
  barcode?: string
  items: StoreOrderProductItem[]
  dynamicDataMap: Record<string, StoreOrderDynamicData>
  onCancel: () => void
  onSelect: (product: StoreOrderProductItem) => void
}

export default function ShopScanResultPicker({
  open,
  loading,
  barcode,
  items,
  dynamicDataMap,
  onCancel,
  onSelect,
}: ShopScanResultPickerProps) {
  return (
    <Modal
      title={`Multiple Matches${barcode ? `: ${barcode}` : ''}`}
      open={open}
      onCancel={onCancel}
      footer={null}
      width={760}
      destroyOnClose
    >
      <div className="shop-scan-picker-list">
        {items.map((item) => {
          const dynamicData = dynamicDataMap[item.productCode]
          const effectiveMinOrderQuantity = item.minOrderQuantity > 0 ? item.minOrderQuantity : 1

          return (
            <div key={item.productCode} className="shop-scan-picker-item">
              <div className="shop-scan-picker-main">
                <Image
                  src={item.productImage}
                  alt={item.productName}
                  width={72}
                  height={72}
                  className="shop-scan-picker-image"
                  fallback="data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs="
                  preview={false}
                />
                <div className="shop-scan-picker-meta">
                  <div className="shop-scan-picker-name">{item.productName || item.productCode}</div>
                  <Space size={[8, 8]} wrap>
                    <Tag>Item: {item.itemNumber || '-'}</Tag>
                    <Tag color="blue">Barcode: {item.barcode || '-'}</Tag>
                    <Tag color="gold">MOQ: {effectiveMinOrderQuantity}</Tag>
                    <Tag color="green">In Cart: {dynamicData?.cartQuantity ?? 0}</Tag>
                  </Space>
                  <div className="shop-scan-picker-prices">
                    <Text type="secondary">RRP:</Text> <Text strong>${item.oemPrice?.toFixed(2) ?? '0.00'}</Text>
                    <Text type="secondary"> Import:</Text>{' '}
                    <Text strong>${item.importPrice?.toFixed(2) ?? '0.00'}</Text>
                  </div>
                </div>
              </div>
              <Button
                type="primary"
                icon={<CheckOutlined />}
                loading={loading}
                onClick={() => onSelect(item)}
              >
                Add
              </Button>
            </div>
          )
        })}
      </div>
    </Modal>
  )
}
