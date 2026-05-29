import { Breadcrumb, Empty, Pagination, Select, Space, Spin, Tag, Tooltip, message } from 'antd'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router-dom'
import ShopScanBar from '../../components/ShopScanBar'
import ShopScanResultPicker from '../../components/ShopScanResultPicker'
import { PRODUCT_GRADE_CONFIG } from '../../types/productGrade'
import { useBarcodeScanner } from '../../hooks/useBarcodeScanner'
import ProductCard from './components/ProductCard'
import {
  addStoreOrderCartItem,
  getActiveStoreOrderCart,
  getStoreOrderProducts,
  getStoreOrderProductsDynamicData,
  lookupStoreOrderProductsByBarcode,
  removeStoreOrderCartItem,
} from '../../services/storeOrderService'
import { getCategoryTree, type WarehouseCategoryNode } from '../../services/warehouseCategoryService'
import { useShopStore } from '../../store/shop'
import type {
  StoreOrderDynamicData,
  StoreOrderProductItem,
  StoreOrderScanStatus,
} from '../../types/storeOrder'
import {
  isScanFeedbackUnlocked,
  playScanFeedback,
  unlockScanFeedback,
} from '../../utils/scanFeedback'

export default function ShopHomePage() {
  const { t } = useTranslation()
  const [searchParams] = useSearchParams()
  const categoryId = searchParams.get('category')
  const keyword = searchParams.get('keyword')

  const [products, setProducts] = useState<StoreOrderProductItem[]>([])
  const [dynamicDataMap, setDynamicDataMap] = useState<Record<string, StoreOrderDynamicData>>({})
  const [loading, setLoading] = useState(false)
  const [total, setTotal] = useState(0)
  const [currentPage, setCurrentPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [categoryName, setCategoryName] = useState('')
  const [parentChain, setParentChain] = useState<WarehouseCategoryNode[]>([])
  const [scanStatus, setScanStatus] = useState<StoreOrderScanStatus>('ready')
  const [scanEnabled, setScanEnabled] = useState(false)
  const [scanBusy, setScanBusy] = useState(false)
  const [scanMessage, setScanMessage] = useState(t('shop.scan.waiting'))
  const [lastScannedCode, setLastScannedCode] = useState('')
  const [lastScannedProduct, setLastScannedProduct] = useState('')
  const [lastProductImage, setLastProductImage] = useState('')
  const [lastItemNumber, setLastItemNumber] = useState('')
  const [lastAddedQuantity, setLastAddedQuantity] = useState<number>()
  const [lastCartTotalQuantity, setLastCartTotalQuantity] = useState<number>()
  const [soundEnabled, setSoundEnabled] = useState(isScanFeedbackUnlocked())
  const [scanCandidates, setScanCandidates] = useState<StoreOrderProductItem[]>([])
  const [pickerOpen, setPickerOpen] = useState(false)
  const [pickerLoading, setPickerLoading] = useState(false)
  const [pickerDynamicDataMap, setPickerDynamicDataMap] = useState<Record<string, StoreOrderDynamicData>>({})
  const [gradeFilter, setGradeFilter] = useState<string[]>([])
  const selectedStore = useShopStore((state) => state.selectedStore)
  const setCart = useShopStore((state) => state.setCart)

  const pageTitle = useMemo(() => {
    if (keyword) {
      return t('shop.searchTitle', { keyword })
    }

    if (categoryId) {
      return categoryName || t('shop.categoryProducts')
    }

    return t('shop.allProducts')
  }, [categoryId, categoryName, keyword, t])

  useEffect(() => {
    setCurrentPage(1)
  }, [categoryId, keyword])

  const loadDynamicDataMap = useCallback(
    async (productCodes: string[]) => {
      if (!selectedStore?.storeCode || !productCodes.length) {
        return {}
      }

      const result = await getStoreOrderProductsDynamicData({
        storeCode: selectedStore.storeCode,
        productCodes,
      })

      return result.reduce<Record<string, StoreOrderDynamicData>>((acc, item) => {
        acc[item.productCode] = item
        return acc
      }, {})
    },
    [selectedStore?.storeCode],
  )

  useEffect(() => {
    let cancelled = false

    const fetchProducts = async () => {
      setLoading(true)
      try {
        const result = await getStoreOrderProducts({
          pageNumber: currentPage,
          pageSize,
          categoryGUID: categoryId || undefined,
          itemNumber: keyword || undefined,
          sortBy: 'productName',
          grade: gradeFilter.length ? [...gradeFilter].sort().join(',') : undefined,
        })

        if (cancelled) {
          return
        }

        setProducts(result.items)
        setTotal(result.total)

        if (categoryId) {
          setCategoryName(result.items[0]?.categoryName || t('shop.categoryProducts'))
        } else {
          setCategoryName('')
        }
      } catch (error) {
        if (!cancelled) {
          message.error(t('shop.loadProductsFailed'))
          setProducts([])
          setTotal(0)
        }
      } finally {
        if (!cancelled) {
          setLoading(false)
        }
      }
    }

    void fetchProducts()

    return () => {
      cancelled = true
    }
  }, [categoryId, currentPage, keyword, pageSize, gradeFilter, t])

  useEffect(() => {
    let cancelled = false

    const buildChain = async () => {
      if (!categoryId) {
        setParentChain([])
        return
      }

      try {
        const tree = await getCategoryTree()
        const path: WarehouseCategoryNode[] = []

        const dfs = (nodes: WarehouseCategoryNode[]): boolean => {
          for (const node of nodes) {
            path.push(node)
            if (node.categoryGUID === categoryId) {
              return true
            }
            if (node.children?.length && dfs(node.children)) {
              return true
            }
            path.pop()
          }
          return false
        }

        dfs(tree)
        if (!cancelled) {
          setParentChain(path)
        }
      } catch (error) {
        if (!cancelled) {
          setParentChain([])
        }
      }
    }

    void buildChain()

    return () => {
      cancelled = true
    }
  }, [categoryId])

  useEffect(() => {
    let cancelled = false

    const fetchDynamicData = async () => {
      if (!selectedStore?.storeCode || !products.length) {
        setDynamicDataMap({})
        return
      }

      try {
        const nextMap = await loadDynamicDataMap(products.map((item) => item.productCode))
        if (!cancelled) {
          setDynamicDataMap(nextMap)
        }
      } catch (error) {
        if (!cancelled) {
          setDynamicDataMap({})
        }
      }
    }

    void fetchDynamicData()

    return () => {
      cancelled = true
    }
  }, [loadDynamicDataMap, products, selectedStore?.storeCode])

  useEffect(() => {
    let cancelled = false

    const loadPickerDynamicData = async () => {
      if (!pickerOpen || !selectedStore?.storeCode || !scanCandidates.length) {
        setPickerDynamicDataMap({})
        return
      }

      try {
        const nextMap = await loadDynamicDataMap(scanCandidates.map((item) => item.productCode))
        if (!cancelled) {
          setPickerDynamicDataMap(nextMap)
        }
      } catch (error) {
        if (!cancelled) {
          setPickerDynamicDataMap({})
        }
      }
    }

    void loadPickerDynamicData()

    return () => {
      cancelled = true
    }
  }, [loadDynamicDataMap, pickerOpen, scanCandidates, selectedStore?.storeCode])

  const refreshCart = useCallback(async () => {
    if (!selectedStore?.storeCode) {
      setCart(null)
      return
    }

    const cart = await getActiveStoreOrderCart(selectedStore.storeCode)
    setCart(cart)
  }, [selectedStore?.storeCode, setCart])

  const refreshDynamicData = useCallback(async () => {
    if (!selectedStore?.storeCode || !products.length) {
      setDynamicDataMap({})
      return
    }

    const nextMap = await loadDynamicDataMap(products.map((item) => item.productCode))
    setDynamicDataMap(nextMap)
  }, [loadDynamicDataMap, products, selectedStore?.storeCode])

  const refreshPickerDynamicData = useCallback(async () => {
    if (!selectedStore?.storeCode || !scanCandidates.length) {
      setPickerDynamicDataMap({})
      return
    }

    const nextMap = await loadDynamicDataMap(scanCandidates.map((item) => item.productCode))
    setPickerDynamicDataMap(nextMap)
  }, [loadDynamicDataMap, scanCandidates, selectedStore?.storeCode])

  const updateScanFeedback = useCallback(
    (
      nextStatus: StoreOrderScanStatus,
      nextMessage: string,
      options?: {
        barcode?: string
        productName?: string
        productImage?: string
        itemNumber?: string
        quantity?: number
        cartTotalQuantity?: number
        tone?: Parameters<typeof playScanFeedback>[0]
      },
    ) => {
      setScanStatus(nextStatus)
      setScanMessage(nextMessage)
      setLastScannedCode(options?.barcode ?? '')
      setLastScannedProduct(options?.productName ?? '')
      setLastProductImage(options?.productImage ?? '')
      setLastItemNumber(options?.itemNumber ?? '')
      setLastAddedQuantity(options?.quantity)
      setLastCartTotalQuantity(options?.cartTotalQuantity)

      if (options?.tone) {
        playScanFeedback(options.tone)
      }
    },
    [],
  )

  const addScannedProductToCart = useCallback(
    async (product: StoreOrderProductItem, barcode: string) => {
      if (!selectedStore?.storeCode) {
        updateScanFeedback('blocked', t('shop.scan.selectStoreFirst'), {
          barcode,
          tone: 'blocked',
        })
        return false
      }

      const quantity = product.minOrderQuantity > 0 ? product.minOrderQuantity : 1

      try {
        await addStoreOrderCartItem({
          storeCode: selectedStore.storeCode,
          productCode: product.productCode,
          quantity,
        })

        let cartTotalQty: number | undefined
        try {
          const cart = await getActiveStoreOrderCart(selectedStore.storeCode)
          setCart(cart)
          cartTotalQty = cart?.items.find((ci) => ci.productCode === product.productCode)?.quantity
        } catch {}

        updateScanFeedback('added', t('shop.scan.addedProduct', { name: product.productName || product.productCode }), {
          barcode,
          productName: product.productName || product.productCode,
          productImage: product.productImage,
          itemNumber: product.itemNumber,
          quantity,
          cartTotalQuantity: cartTotalQty,
          tone: 'success',
        })
        Promise.all([refreshDynamicData(), refreshPickerDynamicData()]).catch(() => {})
        return true
      } catch (error) {
        updateScanFeedback('error', t('shop.scan.addItemFailed'), {
          barcode,
          productName: product.productName || product.productCode,
          tone: 'error',
        })
        return false
      }
    },
    [refreshCart, refreshDynamicData, refreshPickerDynamicData, selectedStore?.storeCode, t, updateScanFeedback],
  )

  const handleBarcodeSubmit = useCallback(
    async (rawBarcode: string) => {
      const barcode = rawBarcode.trim()
      if (!barcode || scanBusy) {
        return
      }

      setScanBusy(true)
      setScanStatus('scanning')
      setScanMessage(t('shop.scan.lookingUp', { barcode }))

      if (!selectedStore?.storeCode) {
        updateScanFeedback('blocked', t('shop.scan.selectStoreFirst'), {
          barcode,
          tone: 'blocked',
        })
        setScanBusy(false)
        return
      }

      try {
        const result = await lookupStoreOrderProductsByBarcode(barcode)

        if (!result.items.length) {
          updateScanFeedback('not_found', t('shop.scan.barcodeNotFound'), {
            barcode,
            tone: 'not-found',
          })
          return
        }

        if (result.items.length === 1) {
          await addScannedProductToCart(result.items[0], barcode)
          return
        }

        setScanCandidates(result.items)
        setPickerOpen(true)
        updateScanFeedback('multiple', t('shop.scan.multipleMatchesFound'), {
          barcode,
          tone: 'multiple',
        })
      } catch (error) {
        updateScanFeedback('error', t('shop.scan.lookupFailed'), {
          barcode,
          tone: 'error',
        })
      } finally {
        setScanBusy(false)
      }
    },
    [addScannedProductToCart, scanBusy, selectedStore?.storeCode, t, updateScanFeedback],
  )

  const handleUnlockSound = useCallback(async () => {
    const success = await unlockScanFeedback()
    setSoundEnabled(success)
    setScanMessage(success ? t('shop.scan.soundFeedbackReady') : t('shop.scan.soundUnavailable'))
  }, [t])

  useBarcodeScanner({
    enabled: scanEnabled && !scanBusy,
    idleMs: 300,
    resetMs: 2000,
    onScan: (value) => {
      void handleBarcodeSubmit(value)
    },
  })

  const handleAddToCart = async (product: StoreOrderProductItem, quantity: number) => {
    if (!selectedStore?.storeCode) {
      message.warning(t('shop.selectStoreFirst'))
      return
    }

    try {
      await addStoreOrderCartItem({
        storeCode: selectedStore.storeCode,
        productCode: product.productCode,
        quantity,
      })
      message.success(t('shop.addedToCart', { quantity, name: product.productName }))
      await Promise.all([refreshCart(), refreshDynamicData()])
    } catch (error) {
      message.error(t('shop.addToCartFailed'))
    }
  }

  const handleRemoveFromCart = async (product: StoreOrderProductItem) => {
    if (!selectedStore?.storeCode) {
      message.warning(t('shop.selectStoreFirst'))
      return
    }

    const cart = await getActiveStoreOrderCart(selectedStore.storeCode)
    const cartItem = cart?.items.find((item) => item.productCode === product.productCode)

    if (!cartItem) {
      message.warning(t('shop.itemNotFoundInCart'))
      return
    }

    try {
      await removeStoreOrderCartItem({
        storeCode: selectedStore.storeCode,
        detailGUID: cartItem.detailGUID,
      })
      message.success(t('shop.removedFromCart', { name: product.productName }))
      await Promise.all([refreshCart(), refreshDynamicData()])
    } catch (error) {
      message.error(t('shop.cartRemoveFailed'))
    }
  }

  return (
    <div className="shop-home-page">
      <ShopScanBar
        status={scanStatus}
        lastScannedCode={lastScannedCode}
        lastProductName={lastScannedProduct}
        lastProductImage={lastProductImage}
        lastItemNumber={lastItemNumber}
        lastQuantity={lastAddedQuantity}
        lastCartTotalQuantity={lastCartTotalQuantity}
        lastMessage={scanMessage}
        enabled={scanEnabled}
        soundEnabled={soundEnabled}
        busy={scanBusy}
        onToggleEnabled={() => setScanEnabled((current) => !current)}
        onUnlockSound={() => {
          void handleUnlockSound()
        }}
        onManualSubmit={(barcode) => {
          void handleBarcodeSubmit(barcode)
        }}
      />

      <div className="shop-home-header">
        {categoryId && parentChain.length ? (
          <Breadcrumb
            items={parentChain.map((item) => ({
              title: <Link to={`/shop?category=${item.categoryGUID}`}>{item.categoryName}</Link>,
            }))}
          />
        ) : null}

        <h2>{pageTitle}</h2>

        <div className="shop-home-controls">
          <div className="shop-home-pagination-info">
            {t('shop.paginationInfo', { total, page: currentPage })}
          </div>
          <div className="shop-home-filters">
            <Space size={4} wrap>
              <span className="shop-home-filter-label">{t('shop.grade')}:</span>
              <Tag.CheckableTag
                checked={gradeFilter.length === 0}
                onChange={() => {
                  setGradeFilter([])
                  setCurrentPage(1)
                }}
                style={{ padding: '2px 8px', borderRadius: 4 }}
              >
                {t('common.all')}
              </Tag.CheckableTag>
              {(Object.entries(PRODUCT_GRADE_CONFIG) as [string, typeof PRODUCT_GRADE_CONFIG[keyof typeof PRODUCT_GRADE_CONFIG]][]).map(([key, cfg]) => (
                <Tooltip key={key} title={t(`shop.gradeTooltip.${key}`)}>
                  <Tag.CheckableTag
                    checked={gradeFilter.includes(key)}
                    onChange={(checked) => {
                      setGradeFilter((prev) =>
                        checked
                          ? [...prev, key]
                          : prev.filter((g) => g !== key),
                      )
                      setCurrentPage(1)
                    }}
                    style={{
                      padding: '2px 8px',
                      borderRadius: 4,
                      border: `1px solid ${cfg.color}`,
                      color: gradeFilter.includes(key) ? '#fff' : cfg.color,
                      background: gradeFilter.includes(key) ? cfg.color : 'transparent',
                    }}
                  >
                    {key} - {t(`shop.gradeLabel.${key}`)}
                  </Tag.CheckableTag>
                </Tooltip>
              ))}
            </Space>
          </div>
          <div className="shop-home-filters">
            <span className="shop-home-filter-label">{t('shop.itemsPerPage')}:</span>
            <Select
              value={pageSize}
              className="shop-home-filter-select"
              onChange={(value) => {
                setCurrentPage(1)
                setPageSize(value)
              }}
              options={[
                { value: 50, label: '50' },
                { value: 100, label: '100' },
                { value: 200, label: '200' },
              ]}
            />
          </div>
        </div>
      </div>

      {loading ? (
        <div className="shop-home-loading">
          <Spin size="large" />
        </div>
      ) : products.length ? (
        <>
          <div className="shop-home-grid">
            {products.map((product) => (
              <ProductCard
                key={product.productCode}
                product={product}
                dynamicData={dynamicDataMap[product.productCode]}
                onAddToCart={handleAddToCart}
                onRemoveFromCart={handleRemoveFromCart}
              />
            ))}
          </div>

          <div className="shop-home-pagination">
            <Pagination
              current={currentPage}
              total={total}
              pageSize={pageSize}
              onChange={(page, size) => {
                setCurrentPage(page)
                if (size && size !== pageSize) {
                  setPageSize(size)
                }
              }}
              pageSizeOptions={['50', '100', '200']}
              showSizeChanger
            />
          </div>
        </>
      ) : (
        <Empty description={t('shop.noProductsFound')} />
      )}

      <ShopScanResultPicker
        open={pickerOpen}
        loading={pickerLoading}
        barcode={lastScannedCode}
        items={scanCandidates}
        dynamicDataMap={pickerDynamicDataMap}
        onCancel={() => {
          setPickerOpen(false)
          setPickerLoading(false)
        }}
        onSelect={(product) => {
          setPickerLoading(true)
          void addScannedProductToCart(product, lastScannedCode).finally(() => {
            setPickerLoading(false)
            setPickerOpen(false)
          })
        }}
      />
    </div>
  )
}
