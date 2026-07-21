import {
  ClockCircleOutlined,
  DeleteOutlined,
  ShoppingCartOutlined,
} from '@ant-design/icons'
import { Badge, Button, Card, Image, InputNumber, Space, Tooltip, Typography } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import type { StoreOrderDynamicData, StoreOrderProductItem } from '../../../types/storeOrder'
import { PRODUCT_GRADE_CONFIG } from '../../../types/productGrade'

const { Paragraph, Text, Title } = Typography

interface ProductCardProps {
  product: StoreOrderProductItem
  dynamicData?: StoreOrderDynamicData
  categoryPath?: string
  onCategoryPathClick?: (product: StoreOrderProductItem) => void
  onAddToCart: (product: StoreOrderProductItem, quantity: number) => Promise<void> | void
  onQuantityChange: (product: StoreOrderProductItem, quantity: number) => Promise<void> | void
  onRemoveFromCart?: (product: StoreOrderProductItem) => Promise<void> | void
  loading?: boolean
  removing?: boolean
}

export default function ProductCard({
  product,
  dynamicData,
  categoryPath,
  onCategoryPathClick,
  onAddToCart,
  onQuantityChange,
  onRemoveFromCart,
  loading,
  removing = false,
}: ProductCardProps) {
  const stepQuantity = product.minOrderQuantity > 0 ? product.minOrderQuantity : 1
  const cartQuantity = dynamicData?.cartQuantity ?? 0
  const [quantity, setQuantity] = useState<number>(0)

  const imageSrc = useMemo(() => {
    return product.productImage || 'https://via.placeholder.com/200x200?text=No+Image'
  }, [product.productImage])

  const gradeColor = product.grade
    ? (PRODUCT_GRADE_CONFIG[product.grade as keyof typeof PRODUCT_GRADE_CONFIG]?.color || '#999')
    : undefined
  const canClickCategoryPath = Boolean(categoryPath && onCategoryPathClick)

  useEffect(() => {
    setQuantity(cartQuantity)
  }, [cartQuantity, product.productCode])

  const normalizeQuantity = (value: number | null | undefined) => {
    const numericValue = Number(value ?? 0)
    if (!Number.isFinite(numericValue) || numericValue <= 0) {
      return 0
    }

    return Math.floor(numericValue)
  }

  const applyQuantityChange = (nextQuantity: number) => {
    if (removing) {
      return
    }
    const normalizedQuantity = normalizeQuantity(nextQuantity)
    setQuantity(normalizedQuantity)

    if (normalizedQuantity === cartQuantity) {
      return
    }

    // 商品卡数量直接代表购物车数量；0 只在已有购物车数量时提交，用于触发后端删除明细。
    if (normalizedQuantity > 0 || cartQuantity > 0) {
      void onQuantityChange(product, normalizedQuantity)
    }
  }

  const handleAddToCart = () => {
    if (removing) {
      return
    }
    const addQuantity = quantity > 0 ? quantity : stepQuantity
    setQuantity(addQuantity)
    void onAddToCart(product, addQuantity)
  }

  const handleQuickPackQuantity = (packCount: number) => {
    if (removing) {
      return
    }

    // 快捷按钮表示设置总份数，不是在当前数量上累加。
    const quickQuantity = packCount * stepQuantity
    applyQuantityChange(quickQuantity)
  }

  const handleCategoryPathActivate = () => {
    if (!canClickCategoryPath || !onCategoryPathClick) {
      return
    }

    onCategoryPathClick(product)
  }

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
          <div
            className="shop-product-card-actions"
            key="actions"
          >
            <div className="shop-product-card-action-slot shop-product-card-action-slot--left">
              {onRemoveFromCart && cartQuantity > 0 ? (
                <Button
                  type="text"
                  danger
                  icon={<DeleteOutlined />}
                  onClick={() => {
                    void onRemoveFromCart(product)
                  }}
                  disabled={loading}
                  size="small"
                  title="Remove from cart"
                  aria-label="Remove product from cart"
                />
              ) : null}
            </div>
            <div className="shop-product-quantity-stepper">
              <Button
                size="small"
                onClick={() => applyQuantityChange(quantity - stepQuantity)}
                disabled={removing || quantity <= 0}
                aria-label="Decrease quantity"
                title="Decrease quantity"
                className="shop-product-quantity-button"
              >
                -
              </Button>
              <InputNumber
                size="small"
                min={0}
                precision={0}
                step={stepQuantity}
                controls={false}
                value={quantity}
                disabled={removing}
                onChange={(value) => setQuantity(normalizeQuantity(value))}
                onBlur={() => applyQuantityChange(quantity)}
                onPressEnter={() => applyQuantityChange(quantity)}
                className="shop-product-quantity-input"
              />
              <Button
                size="small"
                type="primary"
                onClick={() => applyQuantityChange(quantity + stepQuantity)}
                disabled={removing}
                aria-label="Increase quantity"
                title="Increase quantity"
                className="shop-product-quantity-button"
              >
                +
              </Button>
            </div>
            {[2, 3, 4].map((packCount) => {
              const quickQuantity = packCount * stepQuantity
              return (
                <Button
                  key={packCount}
                  size="small"
                  onClick={() => handleQuickPackQuantity(packCount)}
                  disabled={removing}
                  aria-label={`Set total quantity to ${packCount} packs (${quickQuantity})`}
                  title={`Set total quantity to ${packCount} packs (${quickQuantity})`}
                  className="shop-product-quick-pack-button"
                >
                  {packCount}
                </Button>
              )
            })}
            <div className="shop-product-card-action-slot shop-product-card-action-slot--right">
              {cartQuantity <= 0 ? (
                <Button
                  type="primary"
                  size="small"
                  icon={<ShoppingCartOutlined />}
                  onClick={handleAddToCart}
                  loading={loading}
                  disabled={removing}
                  aria-label="Add product to cart"
                  title="Add product to cart"
                  className="shop-product-cart-button"
                />
              ) : null}
            </div>
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

              {categoryPath ? (
                <Tooltip title={categoryPath}>
                  {/* 搜索结果卡片空间有限，分类路径保留两行并通过悬浮显示完整内容。 */}
                  <Paragraph
                    className={[
                      'shop-product-category-path',
                      canClickCategoryPath ? 'shop-product-category-path--clickable' : '',
                    ].filter(Boolean).join(' ')}
                    type="secondary"
                    ellipsis={{ rows: 2 }}
                    role={canClickCategoryPath ? 'button' : undefined}
                    tabIndex={canClickCategoryPath ? 0 : undefined}
                    onClick={handleCategoryPathActivate}
                    onKeyDown={(event) => {
                      if (event.key === 'Enter' || event.key === ' ') {
                        event.preventDefault()
                        handleCategoryPathActivate()
                      }
                    }}
                  >
                    {categoryPath}
                  </Paragraph>
                </Tooltip>
              ) : null}

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
