import {
  DeleteOutlined,
  DeleteTwoTone,
  ShoppingCartOutlined,
} from '@ant-design/icons'
import {
  Avatar,
  Button,
  Drawer,
  Empty,
  Input,
  InputNumber,
  List,
  Modal,
  Pagination,
  Popconfirm,
  Space,
  Typography,
  message,
} from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  clearActiveStoreOrderCart,
  removeStoreOrderCartItem,
  submitActiveStoreOrder,
  updateStoreOrderCartItem,
} from '../services/storeOrderService'
import type { StoreOrderCart } from '../types/storeOrder'

const { Text, Title } = Typography

interface ShopCartDrawerProps {
  open: boolean
  onClose: () => void
  cart: StoreOrderCart | null
  onCartChanged: () => Promise<void>
}

export default function ShopCartDrawer({
  open,
  onClose,
  cart,
  onCartChanged,
}: ShopCartDrawerProps) {
  const { t } = useTranslation()
  const cartItems = cart?.items ?? []
  const totalQuantity = cart?.totalQuantity ?? 0
  const totalImportAmount = cart?.totalImportAmount ?? 0
  const [loadingMap, setLoadingMap] = useState<Record<string, boolean>>({})
  const [remarks, setRemarks] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [currentPage, setCurrentPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)

  useEffect(() => {
    if (!cartItems.length) {
      setCurrentPage(1)
      return
    }

    const totalPages = Math.ceil(cartItems.length / pageSize)
    setCurrentPage(totalPages || 1)
  }, [cartItems, pageSize])

  const paginatedItems = useMemo(() => {
    if (!cartItems.length) {
      return []
    }

    const startIndex = (currentPage - 1) * pageSize
    return cartItems.slice(startIndex, startIndex + pageSize)
  }, [cartItems, currentPage, pageSize])

  const handleRemove = async (detailGUID: string) => {
    if (!cart?.storeCode) {
      return
    }

    setLoadingMap((prev) => ({ ...prev, [detailGUID]: true }))
    try {
      await removeStoreOrderCartItem({
        storeCode: cart.storeCode,
        detailGUID,
      })
      message.success(t('shop.cartItemRemoved', 'Item removed'))
      await onCartChanged()
    } catch (error) {
      message.error(t('shop.cartRemoveFailed', 'Failed to remove item'))
    } finally {
      setLoadingMap((prev) => ({ ...prev, [detailGUID]: false }))
    }
  }

  const handleUpdateQuantity = async (
    productCode: string,
    quantity: number,
    minOrderQuantity: number,
    detailGUID: string,
  ) => {
    if (!cart?.storeCode) {
      return
    }

    setLoadingMap((prev) => ({ ...prev, [detailGUID]: true }))
    try {
      await updateStoreOrderCartItem({
        storeCode: cart.storeCode,
        productCode,
        quantity: quantity || minOrderQuantity || 1,
      })
      await onCartChanged()
    } catch (error) {
      message.error(t('shop.cartUpdateFailed', 'Failed to update quantity'))
    } finally {
      setLoadingMap((prev) => ({ ...prev, [detailGUID]: false }))
    }
  }

  const handleSubmitOrder = async () => {
    if (!cart?.storeCode) {
      return
    }

    setSubmitting(true)
    try {
      await submitActiveStoreOrder({
        storeCode: cart.storeCode,
        remarks: remarks.trim() || undefined,
      })
      message.success(t('shop.orderSubmitted', 'Order submitted successfully'))
      setRemarks('')
      await onCartChanged()
      onClose()
    } catch (error) {
      message.error(t('shop.orderSubmitFailed', 'Failed to submit order'))
    } finally {
      setSubmitting(false)
    }
  }

  const handleSubmitWithConfirm = () => {
    if (!cart?.storeCode) {
      return
    }

    Modal.confirm({
      title: t('shop.confirmOrderSubmission', 'Confirm Order Submission'),
      content: (
        <div>
          <p>
            {t('common.store', 'Store')}: <strong>{cart.storeName || cart.storeCode}</strong>
          </p>
          <p>
            {t('shop.totalQuantity', 'Total Quantity')}: <strong>{cart.totalQuantity}</strong>
          </p>
          <p>
            {t('shop.estimatedTotal', 'Estimated Total')}: <strong>${cart.totalImportAmount.toFixed(2)}</strong>
          </p>
          {remarks.trim() ? (
            <p>
              {t('common.remarks', 'Remarks')}: <em>{remarks.trim()}</em>
            </p>
          ) : null}
        </div>
      ),
      okText: t('shop.submitOrder', 'Submit Order'),
      cancelText: t('common.cancel', 'Cancel'),
      onOk: () => {
        void handleSubmitOrder()
      },
    })
  }

  const handleClearCart = () => {
    if (!cart?.storeCode || !cartItems.length) {
      return
    }

    Modal.confirm({
      title: t('shop.clearCart', 'Clear Cart'),
      content: t('shop.clearCartConfirm', 'Remove all {{count}} items from the cart?', { count: cartItems.length }),
      okText: t('shop.clearCart', 'Clear Cart'),
      okButtonProps: { danger: true },
      cancelText: t('common.cancel', 'Cancel'),
      onOk: async () => {
        try {
          const storeCode = cart.storeCode
          if (!storeCode) {
            return
          }

          await clearActiveStoreOrderCart(storeCode)
          message.success(t('shop.cartCleared', 'Cart cleared'))
          await onCartChanged()
        } catch (error) {
          message.error(t('shop.cartClearFailed', 'Failed to clear cart'))
        }
      },
    })
  }

  return (
    <Drawer
      title={
        <Space>
          <ShoppingCartOutlined />
          <span>{t('shop.shoppingCart', 'Shopping Cart')}</span>
          {cart?.totalQuantity ? (
            <span className="shop-cart-drawer-title-note">({t('shop.itemsCount', '{{count}} items', { count: cart.totalQuantity })})</span>
          ) : null}
          {cartItems.length ? (
            <Button
              type="text"
              size="small"
              danger
              icon={<DeleteTwoTone twoToneColor="#cf1322" />}
              onClick={handleClearCart}
            >
              {t('common.clear', 'Clear')}
            </Button>
          ) : null}
        </Space>
      }
      placement="right"
      onClose={onClose}
      open={open}
      width={460}
      extra={<Button onClick={onClose}>{t('common.close', 'Close')}</Button>}
      footer={
        cartItems.length ? (
          <div className="shop-cart-drawer-footer">
            <div className="shop-cart-drawer-total-row">
              <Text>{t('shop.totalQuantity', 'Total Quantity')}</Text>
              <Text strong>{totalQuantity}</Text>
            </div>
            <div className="shop-cart-drawer-total-row">
              <Text>{t('shop.estimatedTotal', 'Estimated Total')}</Text>
              <Title level={4} className="shop-cart-drawer-amount">
                ${totalImportAmount.toFixed(2)}
              </Title>
            </div>
            <Input.TextArea
              placeholder={t('shop.remarksPlaceholder', 'Remarks (optional)')}
              value={remarks}
              onChange={(event) => setRemarks(event.target.value)}
              rows={2}
              maxLength={500}
              showCount
            />
            <Button type="primary" size="large" block onClick={handleSubmitWithConfirm} loading={submitting}>
              {t('shop.submitOrder', 'Submit Order')}
            </Button>
          </div>
        ) : null
      }
    >
      {cartItems.length ? (
        <>
          <List
            itemLayout="horizontal"
            dataSource={paginatedItems}
            renderItem={(item, index) => {
              const startIndex = (currentPage - 1) * pageSize
              const globalIndex = startIndex + index

              return (
                <List.Item
                  actions={[
                    <Popconfirm
                      key={item.detailGUID}
                      title={t('shop.removeItem', 'Remove Item')}
                      description={t('shop.removeItemConfirm', 'Remove this item from the cart?')}
                      onConfirm={() => {
                        void handleRemove(item.detailGUID)
                      }}
                      okText={t('common.remove', 'Remove')}
                      cancelText={t('common.cancel', 'Cancel')}
                    >
                      <Button
                        type="text"
                        danger
                        icon={<DeleteOutlined />}
                        loading={loadingMap[item.detailGUID]}
                      />
                    </Popconfirm>,
                  ]}
                >
                  <div className="shop-cart-drawer-index">{globalIndex + 1}.</div>
                  <List.Item.Meta
                    avatar={
                      <Avatar
                        src={item.productImage}
                        shape="square"
                        size={64}
                        style={{ border: '1px solid #f0f0f0' }}
                      />
                    }
                    title={
                      <Text ellipsis style={{ width: 180 }} strong>
                        {item.productName}
                      </Text>
                    }
                    description={
                      <div className="shop-cart-drawer-item-desc">
                        <Text type="secondary" style={{ fontSize: 12 }}>
                          {item.itemNumber}
                        </Text>
                        <Text type="secondary" style={{ fontSize: 12 }}>
                          {t('shop.importPrice', 'Import')}: ${item.importPrice?.toFixed(2)}
                        </Text>
                        <div className="shop-cart-drawer-item-row">
                          <Space>
                            <Text style={{ fontSize: 12 }}>{t('shop.qty', 'Qty')}:</Text>
                            <InputNumber
                              size="small"
                              min={item.minOrderQuantity || 1}
                              step={item.minOrderQuantity || 1}
                              value={item.quantity}
                              onChange={(value) => {
                                void handleUpdateQuantity(
                                  item.productCode,
                                  Number(value ?? item.minOrderQuantity ?? 1),
                                  item.minOrderQuantity,
                                  item.detailGUID,
                                )
                              }}
                              disabled={loadingMap[item.detailGUID]}
                              style={{ width: 80 }}
                            />
                          </Space>
                          <Text strong>${item.importAmount?.toFixed(2)}</Text>
                        </div>
                      </div>
                    }
                  />
                </List.Item>
              )
            }}
          />
          <div className="shop-cart-drawer-pagination">
            <Pagination
              current={currentPage}
              pageSize={pageSize}
              total={cartItems.length}
              onChange={(page, size) => {
                setCurrentPage(page)
                if (size && size !== pageSize) {
                  setPageSize(size)
                }
              }}
              showSizeChanger
              showQuickJumper
              showTotal={(total, range) => `${range[0]}-${range[1]} / ${total}`}
              pageSizeOptions={['50', '100']}
            />
          </div>
        </>
      ) : (
        <Empty description={t('shop.emptyCart', 'Your cart is empty')} image={Empty.PRESENTED_IMAGE_SIMPLE} />
      )}
    </Drawer>
  )
}
