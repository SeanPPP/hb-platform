import { ShoppingCartOutlined } from '@ant-design/icons'
import { Breadcrumb, Button, Empty, Pagination, Select, Space, Spin, Tag, Tooltip, message } from 'antd'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
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
  updateStoreOrderCartItem,
} from '../../services/storeOrderService'
import { getCategoryTree, type WarehouseCategoryNode } from '../../services/warehouseCategoryService'
import {
  buildWarehouseCategoryLookup,
  getWarehouseProductCategoryTooltip,
  type WarehouseCategoryLookup,
} from '../Warehouse/Products/categoryPath'
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

function getShopHomePerfNow() {
  return typeof performance !== 'undefined' ? performance.now() : Date.now()
}

function logShopHomePerf(stage: string, payload: Record<string, unknown>) {
  if (typeof console === 'undefined') {
    return
  }

  const isDebugEnabled =
    import.meta.env.DEV ||
    (typeof window !== 'undefined' && window.localStorage.getItem('shopHomePerf') === '1')

  if (!isDebugEnabled) {
    return
  }

  console.info('[shop-home-perf]', stage, payload)
}

// 商城列表页大小必须与顶部筛选和底部分页保持一致。
const SHOP_HOME_PAGE_SIZE_OPTIONS = [50, 100, 200, 500]
// 商品卡连续点击时只在用户短暂停顿后提交最终数量，避免并发请求覆盖最新输入。
const SHOP_PRODUCT_QUANTITY_UPDATE_DEBOUNCE_MS = 300

export default function ShopHomePage() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const categoryId = searchParams.get('category')
  const keyword = searchParams.get('keyword')

  const [products, setProducts] = useState<StoreOrderProductItem[]>([])
  const [dynamicDataMap, setDynamicDataMap] = useState<Record<string, StoreOrderDynamicData>>({})
  const [loading, setLoading] = useState(false)
  const [total, setTotal] = useState(0)
  const [currentPage, setCurrentPage] = useState(1)
  const [pageSize, setPageSize] = useState(200)
  const [categoryName, setCategoryName] = useState('')
  const [categoryTree, setCategoryTree] = useState<WarehouseCategoryNode[]>([])
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
  const [quantityLoadingMap, setQuantityLoadingMap] = useState<Record<string, boolean>>({})
  const [optimisticCartQuantityMap, setOptimisticCartQuantityMap] = useState<Record<string, number>>({})
  const [removingCartProductMap, setRemovingCartProductMap] = useState<Record<string, boolean>>({})
  const [cartOnlyFilter, setCartOnlyFilter] = useState(false)
  const quantityUpdateTimersRef = useRef<Record<string, number>>({})
  const quantityUpdateVersionRef = useRef<Record<string, number>>({})
  const selectedStore = useShopStore((state) => state.selectedStore)
  const cart = useShopStore((state) => state.cart)
  const setCart = useShopStore((state) => state.setCart)
  const shouldShowCategoryPath = Boolean(keyword)

  useEffect(() => {
    return () => {
      Object.values(quantityUpdateTimersRef.current).forEach((timer) => window.clearTimeout(timer))
      quantityUpdateTimersRef.current = {}
      quantityUpdateVersionRef.current = {}
    }
  }, [])

  const categoryLookup = useMemo<WarehouseCategoryLookup | null>(() => {
    if (!categoryTree.length) {
      return null
    }

    return buildWarehouseCategoryLookup(categoryTree)
  }, [categoryTree])

  const categoryPathMap = useMemo<Record<string, string>>(() => {
    if (!shouldShowCategoryPath || !categoryLookup) {
      return {}
    }

    return products.reduce<Record<string, string>>((acc, product) => {
      // 搜索结果需要展示完整分类路径，优先用 GUID，缺失时沿用分类名称兜底。
      const categoryPath = getWarehouseProductCategoryTooltip(product, categoryLookup, i18n.language)
      if (categoryPath) {
        acc[product.productCode] = categoryPath
      }
      return acc
    }, {})
  }, [categoryLookup, i18n.language, products, shouldShowCategoryPath])

  const cartProductItems = useMemo<StoreOrderProductItem[]>(() => {
    return (cart?.items ?? []).map((item) => ({
      productCode: item.productCode,
      itemNumber: item.itemNumber,
      barcode: item.barcode,
      productName: item.productName,
      productImage: item.productImage,
      // 购物车明细没有完整商品 DTO；复用购物车价格和最小订量即可驱动商品卡展示与数量更新。
      oemPrice: item.price,
      importPrice: item.importPrice,
      minOrderQuantity: item.minOrderQuantity || 1,
      stockQuantity: 0,
      isInStock: item.isActive,
    }))
  }, [cart?.items])

  const cartProductDynamicDataMap = useMemo<Record<string, StoreOrderDynamicData>>(() => {
    return (cart?.items ?? []).reduce<Record<string, StoreOrderDynamicData>>((acc, item) => {
      acc[item.productCode] = {
        productCode: item.productCode,
        cartQuantity: item.quantity,
      }
      return acc
    }, {})
  }, [cart?.items])

  const cartQuantityByProductCode = useMemo<Record<string, number>>(() => {
    return (cart?.items ?? []).reduce<Record<string, number>>((acc, item) => {
      acc[item.productCode] = item.quantity
      return acc
    }, {})
  }, [cart?.items])

  const cartProductPageItems = useMemo(() => {
    const startIndex = (currentPage - 1) * pageSize
    return cartProductItems.slice(startIndex, startIndex + pageSize)
  }, [cartProductItems, currentPage, pageSize])

  const displayProducts = cartOnlyFilter ? cartProductPageItems : products
  const displayTotal = cartOnlyFilter ? cartProductItems.length : total
  const cartProductCount = cart?.items.length ?? 0

  const pageTitle = useMemo(() => {
    if (cartOnlyFilter) {
      return t('shop.cartProductsTitle')
    }

    if (keyword) {
      return t('shop.searchTitle', { keyword })
    }

    if (categoryId) {
      return categoryName || t('shop.categoryProducts')
    }

    return t('shop.allProducts')
  }, [cartOnlyFilter, categoryId, categoryName, keyword, t])

  useEffect(() => {
    setCurrentPage(1)
  }, [categoryId, keyword])

  useEffect(() => {
    if (!cartOnlyFilter) {
      return
    }

    const totalPages = Math.max(1, Math.ceil(cartProductItems.length / pageSize))
    if (currentPage > totalPages) {
      setCurrentPage(totalPages)
    }
  }, [cartOnlyFilter, cartProductItems.length, currentPage, pageSize])

  useEffect(() => {
    if (cart?.items.length) {
      return
    }

    Object.values(quantityUpdateTimersRef.current).forEach((timer) => window.clearTimeout(timer))
    quantityUpdateTimersRef.current = {}
    quantityUpdateVersionRef.current = {}
    setOptimisticCartQuantityMap({})
    setRemovingCartProductMap({})
    setQuantityLoadingMap({})
  }, [cart?.items.length])

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
      if (!selectedStore?.storeCode) {
        setProducts([])
        setTotal(0)
        setLoading(false)
        return
      }

      setLoading(true)
      const startedAt = getShopHomePerfNow()
      try {
        const result = await getStoreOrderProducts({
          storeCode: selectedStore.storeCode,
          pageNumber: currentPage,
          pageSize,
          categoryGUID: categoryId || undefined,
          itemNumber: keyword || undefined,
          sortBy: 'Default',
          grade: gradeFilter.length ? [...gradeFilter].sort().join(',') : undefined,
        })

        if (cancelled) {
          return
        }

        setProducts(result.items)
        setTotal(result.total)
        logShopHomePerf('products.done', {
          storeCode: selectedStore.storeCode,
          pageNumber: currentPage,
          pageSize,
          itemCount: result.items.length,
          total: result.total,
          elapsedMs: Math.round(getShopHomePerfNow() - startedAt),
        })

        if (categoryId) {
          setCategoryName(result.items[0]?.categoryName || t('shop.categoryProducts'))
        } else {
          setCategoryName('')
        }
      } catch (error) {
        if (!cancelled) {
          logShopHomePerf('products.error', {
            storeCode: selectedStore.storeCode,
            pageNumber: currentPage,
            pageSize,
            elapsedMs: Math.round(getShopHomePerfNow() - startedAt),
          })
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
  }, [categoryId, currentPage, keyword, pageSize, selectedStore?.storeCode, gradeFilter, t])

  useEffect(() => {
    let cancelled = false

    const loadCategoryTree = async () => {
      if (!categoryId && !keyword) {
        setCategoryTree([])
        return
      }

      try {
        const tree = await getCategoryTree()
        if (!cancelled) {
          setCategoryTree(tree)
        }
      } catch (error) {
        if (!cancelled) {
          setCategoryTree([])
        }
      }
    }

    void loadCategoryTree()

    return () => {
      cancelled = true
    }
  }, [categoryId, keyword])

  useEffect(() => {
    if (!categoryId) {
      setParentChain([])
      return
    }

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

    dfs(categoryTree)
    setParentChain(path)
  }, [categoryId, categoryTree])

  useEffect(() => {
    let cancelled = false

    const fetchDynamicData = async () => {
      if (!selectedStore?.storeCode || !products.length) {
        setDynamicDataMap({})
        return
      }

      try {
        const startedAt = getShopHomePerfNow()
        const nextMap = await loadDynamicDataMap(products.map((item) => item.productCode))
        if (!cancelled) {
          setDynamicDataMap(nextMap)
          logShopHomePerf('dynamic-data.done', {
            storeCode: selectedStore.storeCode,
            requestCount: products.length,
            resultCount: Object.keys(nextMap).length,
            elapsedMs: Math.round(getShopHomePerfNow() - startedAt),
          })
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

  const refreshDynamicDataForProducts = useCallback(
    async (productCodes: string[]) => {
      const normalizedCodes = productCodes
        .filter((code) => Boolean(code))
        .filter((code, index, allCodes) => allCodes.indexOf(code) === index)

      if (!selectedStore?.storeCode || !normalizedCodes.length) {
        return
      }

      const nextMap = await loadDynamicDataMap(normalizedCodes)
      setDynamicDataMap((prev) => ({ ...prev, ...nextMap }))
      setPickerDynamicDataMap((prev) => ({ ...prev, ...nextMap }))
    },
    [loadDynamicDataMap, selectedStore?.storeCode],
  )

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
        const nextCart = await addStoreOrderCartItem({
          storeCode: selectedStore.storeCode,
          productCode: product.productCode,
          quantity,
        })
        setCart(nextCart)

        const cartTotalQty = nextCart?.items.find((ci) => ci.productCode === product.productCode)?.quantity

        updateScanFeedback('added', t('shop.scan.addedProduct', { name: product.productName || product.productCode }), {
          barcode,
          productName: product.productName || product.productCode,
          productImage: product.productImage,
          itemNumber: product.itemNumber,
          quantity,
          cartTotalQuantity: cartTotalQty,
          tone: 'success',
        })
        refreshDynamicDataForProducts([product.productCode]).catch(() => {})
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
    [refreshDynamicDataForProducts, selectedStore?.storeCode, setCart, t, updateScanFeedback],
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

  const handleCategoryPathClick = useCallback(
    (product: StoreOrderProductItem) => {
      if (!product.warehouseCategoryGUID) {
        return
      }

      // 点击搜索结果里的分类路径时进入对应分类页，同时清除当前搜索关键词。
      navigate(`/shop?category=${encodeURIComponent(product.warehouseCategoryGUID)}`)
    },
    [navigate],
  )

  const handleAddToCart = useCallback(
    async (product: StoreOrderProductItem, quantity: number) => {
      if (!selectedStore?.storeCode) {
        message.warning(t('shop.selectStoreFirst'))
        return
      }

      const addQuantity = Math.max(1, Math.floor(Number.isFinite(quantity) ? quantity : 0))
      setOptimisticCartQuantityMap((prev) => ({ ...prev, [product.productCode]: addQuantity }))
      setQuantityLoadingMap((prev) => ({ ...prev, [product.productCode]: true }))
      try {
        const nextCart = await addStoreOrderCartItem({
          storeCode: selectedStore.storeCode,
          productCode: product.productCode,
          quantity: addQuantity,
        })
        setCart(nextCart)
        await refreshDynamicDataForProducts([product.productCode]).catch(() => {})
        setOptimisticCartQuantityMap((prev) => {
          const next = { ...prev }
          delete next[product.productCode]
          return next
        })
      } catch (error) {
        setOptimisticCartQuantityMap((prev) => {
          const next = { ...prev }
          delete next[product.productCode]
          return next
        })
        message.error(t('shop.addToCartFailed'))
      } finally {
        setQuantityLoadingMap((prev) => {
          const next = { ...prev }
          delete next[product.productCode]
          return next
        })
      }
    },
    [dynamicDataMap, refreshDynamicDataForProducts, selectedStore?.storeCode, setCart, t],
  )

  const handleProductQuantityChange = useCallback(
    (product: StoreOrderProductItem, quantity: number) => {
      if (!selectedStore?.storeCode) {
        message.warning(t('shop.selectStoreFirst'))
        return
      }

      const productCode = product.productCode
      const normalizedQuantity = Math.max(0, Math.floor(Number.isFinite(quantity) ? quantity : 0))
      const currentCartQuantity = cartQuantityByProductCode[productCode] ?? 0
      const hasPendingQuantityWork =
        optimisticCartQuantityMap[productCode] !== undefined ||
        quantityUpdateTimersRef.current[productCode] !== undefined ||
        quantityLoadingMap[productCode]

      if (normalizedQuantity === currentCartQuantity && !hasPendingQuantityWork) {
        return
      }

      if (normalizedQuantity <= 0 && currentCartQuantity <= 0 && !hasPendingQuantityWork) {
        return
      }

      if (quantityUpdateTimersRef.current[productCode]) {
        window.clearTimeout(quantityUpdateTimersRef.current[productCode])
      }

      const updateVersion = (quantityUpdateVersionRef.current[productCode] ?? 0) + 1
      quantityUpdateVersionRef.current[productCode] = updateVersion
      setOptimisticCartQuantityMap((prev) => ({ ...prev, [productCode]: normalizedQuantity }))
      setQuantityLoadingMap((prev) => ({ ...prev, [productCode]: true }))

      quantityUpdateTimersRef.current[productCode] = window.setTimeout(() => {
        delete quantityUpdateTimersRef.current[productCode]
        void (async () => {
          if (quantityUpdateVersionRef.current[productCode] !== updateVersion) return

          try {
            // 商品卡数量就是购物车数量，连续点击时只提交最终数量，避免旧请求覆盖新数量。
            const nextCart = await updateStoreOrderCartItem({
              storeCode: selectedStore.storeCode,
              productCode,
              quantity: normalizedQuantity,
            })

            if (quantityUpdateVersionRef.current[productCode] !== updateVersion) return

            setCart(nextCart)
            await refreshDynamicDataForProducts([productCode]).catch(() => {})

            if (quantityUpdateVersionRef.current[productCode] !== updateVersion) return

            // 后端确认保存后再提示；同一商品复用 message key，连续点击只保留最终数量提示。
            message.success({
              content: t('shop.cartQuantityUpdated', { quantity: normalizedQuantity }),
              key: `shop-product-quantity-${productCode}`,
            })

            setOptimisticCartQuantityMap((prev) => {
              const next = { ...prev }
              delete next[productCode]
              return next
            })
          } catch (error) {
            if (quantityUpdateVersionRef.current[productCode] === updateVersion) {
              setOptimisticCartQuantityMap((prev) => {
                const next = { ...prev }
                delete next[productCode]
                return next
              })
              message.error(t('shop.cartUpdateFailed', 'Failed to update quantity'))
            }
          } finally {
            if (quantityUpdateVersionRef.current[productCode] === updateVersion) {
              setQuantityLoadingMap((prev) => {
                const next = { ...prev }
                delete next[productCode]
                return next
              })
            }
          }
        })()
      }, SHOP_PRODUCT_QUANTITY_UPDATE_DEBOUNCE_MS)
    },
    [
      cartQuantityByProductCode,
      optimisticCartQuantityMap,
      quantityLoadingMap,
      refreshDynamicDataForProducts,
      selectedStore?.storeCode,
      setCart,
      t,
    ],
  )

  const handleRemoveFromCart = async (product: StoreOrderProductItem) => {
    if (!selectedStore?.storeCode) {
      message.warning(t('shop.selectStoreFirst'))
      return
    }

    const productCode = product.productCode
    if (removingCartProductMap[productCode]) {
      return
    }

    // 删除按钮点击后先让卡片退出已入车状态，避免重复点击触发第二次删除。
    setRemovingCartProductMap((prev) => ({ ...prev, [productCode]: true }))
    setOptimisticCartQuantityMap((prev) => ({ ...prev, [productCode]: 0 }))

    try {
      const cart = await getActiveStoreOrderCart(selectedStore.storeCode)
      const cartItem = cart?.items.find((item) => item.productCode === productCode)

      if (!cartItem) {
        setOptimisticCartQuantityMap((prev) => {
          const next = { ...prev }
          delete next[productCode]
          return next
        })
        message.warning(t('shop.itemNotFoundInCart'))
        return
      }

      await removeStoreOrderCartItem({
        storeCode: selectedStore.storeCode,
        detailGUID: cartItem.detailGUID,
      })
      message.success(t('shop.removedFromCart', { name: product.productName }))
      await Promise.all([refreshCart(), refreshDynamicData()])
      setOptimisticCartQuantityMap((prev) => {
        const next = { ...prev }
        delete next[productCode]
        return next
      })
    } catch (error) {
      setOptimisticCartQuantityMap((prev) => {
        const next = { ...prev }
        delete next[productCode]
        return next
      })
      message.error(t('shop.cartRemoveFailed'))
    } finally {
      setRemovingCartProductMap((prev) => {
        const next = { ...prev }
        delete next[productCode]
        return next
      })
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
            {t('shop.paginationInfo', { total: displayTotal, page: currentPage })}
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
              <Button
                size="small"
                type={cartOnlyFilter ? 'primary' : 'default'}
                icon={<ShoppingCartOutlined />}
                onClick={() => {
                  setCartOnlyFilter((current) => !current)
                  setCurrentPage(1)
                }}
              >
                {t('shop.cartProductsFilter', { count: cartProductCount })}
              </Button>
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
              options={SHOP_HOME_PAGE_SIZE_OPTIONS.map((value) => ({ value, label: String(value) }))}
            />
          </div>
        </div>
      </div>

      {loading && !cartOnlyFilter ? (
        <div className="shop-home-loading">
          <Spin size="large" />
        </div>
      ) : displayProducts.length ? (
        <>
          <div className="shop-home-grid">
            {displayProducts.map((product) => {
              const dynamicData = cartOnlyFilter
                ? cartProductDynamicDataMap[product.productCode]
                : dynamicDataMap[product.productCode]
              const syncedDynamicData: StoreOrderDynamicData = {
                ...(dynamicData ?? {
                  productCode: product.productCode,
                }),
                // 商品卡购物车数量以全局 cart 快照为准，避免购物车抽屉清空后页面动态数据滞后。
                productCode: dynamicData?.productCode ?? product.productCode,
                cartQuantity: cartQuantityByProductCode[product.productCode] ?? 0,
              }
              const isRemovingFromCart = Boolean(removingCartProductMap[product.productCode])
              const optimisticCartQuantity = isRemovingFromCart
                ? 0
                : optimisticCartQuantityMap[product.productCode]
              const cardDynamicData =
                optimisticCartQuantity === undefined
                  ? syncedDynamicData
                  : {
                    ...syncedDynamicData,
                    cartQuantity: optimisticCartQuantity,
                  }

              return (
                <ProductCard
                  key={product.productCode}
                  product={product}
                  dynamicData={cardDynamicData}
                  categoryPath={categoryPathMap[product.productCode]}
                  onCategoryPathClick={
                    shouldShowCategoryPath && product.warehouseCategoryGUID
                      ? handleCategoryPathClick
                      : undefined
                  }
                  onAddToCart={handleAddToCart}
                  onQuantityChange={handleProductQuantityChange}
                  onRemoveFromCart={handleRemoveFromCart}
                  loading={Boolean(quantityLoadingMap[product.productCode] || isRemovingFromCart)}
                  removing={isRemovingFromCart}
                />
              )
            })}
          </div>

          <div className="shop-home-pagination">
            <Pagination
              current={currentPage}
              total={displayTotal}
              pageSize={pageSize}
              onChange={(page, size) => {
                setCurrentPage(page)
                if (size && size !== pageSize) {
                  setPageSize(size)
                }
              }}
              pageSizeOptions={SHOP_HOME_PAGE_SIZE_OPTIONS.map(String)}
              showSizeChanger
            />
          </div>
        </>
      ) : (
        <Empty description={cartOnlyFilter ? t('shop.noCartProductsFound') : t('shop.noProductsFound')} />
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
