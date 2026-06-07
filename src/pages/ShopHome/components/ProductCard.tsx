import {
  ClockCircleOutlined,
  DeleteOutlined,
  ShoppingCartOutlined,
} from '@ant-design/icons'
import { Badge, Button, Card, Image, InputNumber, Space, Typography } from 'antd'
import { useMemo, useState } from 'react'
import type { StoreOrderDynamicData, StoreOrderProductItem } from '../../../types/storeOrder'
import { PRODUCT_GRADE_CONFIG } from '../../../types/productGrade'

const { Paragraph, Text, Title } = Typography

interface ProductCardProps {
  product: StoreOrderProductItem
  dynamicData?: StoreOrderDynamicData
  onAddToCart: (product: StoreOrderProductItem, quantity: number) => Promise<void> | void
  onRemoveFromCart?: (product: StoreOrderProductItem) => Promise<void> | void
  loading?: boolean
}

export default function ProductCard({
  product,
  dynamicData,
  onAddToCart,
  onRemoveFromCart,
  loading,
}: ProductCardProps) {
  const minOrderQuantity = product.minOrderQuantity || 1
  const [quantity, setQuantity] = useState<number>(minOrderQuantity)

  const imageSrc = useMemo(() => {
    return product.productImage || 'https://via.placeholder.com/200x200?text=No+Image'
  }, [product.productImage])

  const gradeColor = product.grade
    ? (PRODUCT_GRADE_CONFIG[product.grade as keyof typeof PRODUCT_GRADE_CONFIG]?.color || '#999')
    : undefined

  return (
    <div style={{ position: 'relative' }}>
      {product.grade && (
        <div
          style={{
            position: 'absolute',
            top: 0,
            right: 0,
            zIndex: 10,
            background: gradeColor,
            color: '#fff',
            fontSize: 12,
            fontWeight: 700,
            lineHeight: '20px',
            padding: '0 8px',
            borderRadius: '0 0 0 8px',
            boxShadow: '0 2px 4px rgba(0,0,0,0.15)',
          }}
        >
          Grade {product.grade}
        </div>
      )}
      <Badge.Ribbon
        text={`In Cart: ${dynamicData?.cartQuantity || 0}`}
        color="green"
        style={{ display: dynamicData?.cartQuantity ? 'block' : 'none', top: 24 }}
      >
        <Card
          hoverable
          className="shop-product-card"
          cover={
            <div className="shop-product-card-cover" style={{ position: 'relative' }}>
              <Image
                alt={product.productName}
                src={imageSrc}
                loading="lazy"
                height="100%"
                width="100%"
                style={{ objectFit: 'contain' }}
                preview={{ mask: 'Preview' }}
                fallback="https://via.placeholder.com/200x200?text=No+Image"
              />
            </div>
          }
        actions={[
          <div className="shop-product-card-actions" key="actions">
            {onRemoveFromCart && dynamicData?.cartQuantity ? (
              <Button
                type="text"
                danger
                icon={<DeleteOutlined />}
                onClick={() => {
                  void onRemoveFromCart(product)
                }}
                size="small"
                title="Remove from cart"
              />
            ) : (
              <span />
            )}
            <InputNumber
              min={minOrderQuantity}
              step={minOrderQuantity}
              value={quantity}
              onChange={(value) => setQuantity(Number(value ?? minOrderQuantity))}
              style={{ width: 72 }}
            />
            <Button
              type="primary"
              icon={<ShoppingCartOutlined />}
              onClick={() => {
                void onAddToCart(product, quantity)
              }}
              loading={loading}
            >
              Add
            </Button>
          </div>,
        ]}
      >
        <Card.Meta
          title={
            <Paragraph className="shop-product-card-title" ellipsis={{ rows: 2 }}>
              {product.productName}
            </Paragraph>
          }
          description={
            <div className="shop-product-card-desc">
              <div>
                <Text type="secondary">Item No: </Text>
                <Text strong copyable>
                  {product.itemNumber}
                </Text>
              </div>

              {dynamicData?.lastOrderDate ? (
                <div className="shop-product-last-order">
                  <Space direction="vertical" size={0}>
                    <Text type="warning" style={{ fontSize: 12 }}>
                      <ClockCircleOutlined /> Last Order:{' '}
                      {new Date(dynamicData.lastOrderDate).toLocaleDateString()}
                    </Text>
                    <Text type="secondary" style={{ fontSize: 12 }}>
                      Order{' '}
                      <Text style={{ color: (dynamicData.lastQuantity || 0) === 0 ? '#f5222d' : undefined }}>
                        {dynamicData.lastQuantity?.toFixed(0) || 0}
                      </Text>{' '}
                      / Send{' '}
                      <Text
                        style={{
                          color: (dynamicData.lastAllocQuantity || 0) === 0 ? '#f5222d' : '#52c41a',
                        }}
                      >
                        {dynamicData.lastAllocQuantity?.toFixed(0) || 0}
                      </Text>
                    </Text>
                  </Space>
                </div>
              ) : null}

              <div className="shop-product-price-row">
                <div />
                <div className="shop-product-price">
                  <Title level={4} style={{ margin: 0, color: '#f5222d' }}>
                    ${product.oemPrice?.toFixed(2)}
                  </Title>
                  <Text type="secondary" style={{ fontSize: 12 }}>
                    RRP
                  </Text>
                </div>
              </div>
            </div>
          }
        />
      </Card>
    </Badge.Ribbon>
    </div>
  )
}
