import {
  AppstoreOutlined,
  DownOutlined,
  GiftOutlined,
  MenuOutlined,
  ShoppingCartOutlined,
  UserOutlined,
} from '@ant-design/icons'
import { Alert, Badge, Button, Drawer, Dropdown, Input, Menu, Modal, Select, Space, Spin, message } from 'antd'
import type { MenuProps } from 'antd'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, Outlet, useLocation, useNavigate } from 'react-router-dom'
import LanguageSwitch from '../components/LanguageSwitch'
import ShopCartDrawer from '../components/ShopCartDrawer'
import ShopCartSummary from '../components/ShopCartSummary'
import { getUserStores } from '../services/userService'
import { getCategoryTree, type WarehouseCategoryNode } from '../services/warehouseCategoryService'
import { getActiveStoreOrderCart, getActiveStoreOrderCartSummary } from '../services/storeOrderService'
import {
  canBypassPreorderGate,
  getActivePreorders,
  resolveEffectivePreorderGateBlocked,
} from '../services/preorderService'
import { useAuthStore } from '../store/auth'
import { useShopStore } from '../store/shop'
import { resolveShopBannerCopy } from './shopBannerCopy'
import {
  resolvePreorderPromptPresentation,
  resolveShopPreorderNavigation,
} from '../pages/ShopPreorder/preorderNavigation'
import { changeStoreAfterDurableLeave, runAfterDurableLeave, usePreorderLeave } from '../pages/ShopPreorder/preorderLeaveContext'

const { Search } = Input
const PREORDER_GATE_TIMEOUT_MS = 8_000

function supportsHover() {
  if (typeof window !== 'undefined' && window.matchMedia) {
    return window.matchMedia('(hover: hover)').matches
  }

  return true
}

export default function ShopLayout() {
  const navigate = useNavigate()
  const location = useLocation()
  const { currentUser, access, logout } = useAuthStore()
  const { requestPreorderDurableLeave } = usePreorderLeave()
  const { t, i18n } = useTranslation()
  const isShopHomePage = location.pathname === '/shop'
  const isPreorderPage = location.pathname.startsWith('/shop/preorders/')
  const isBestSellersPage = location.pathname.startsWith('/shop/best-sellers')
  const isComingSoonPage = location.pathname.startsWith('/shop/coming-soon')
  const isOrdersPage = location.pathname.startsWith('/shop/orders')
  const shopBannerCopy = useMemo(() => resolveShopBannerCopy(location.pathname), [location.pathname])
  const preorderDateTimeFormatter = useMemo(
    () => new Intl.DateTimeFormat(i18n.resolvedLanguage || i18n.language, { dateStyle: 'medium', timeStyle: 'short' }),
    [i18n.language, i18n.resolvedLanguage],
  )

  const userStores = useShopStore((state) => state.userStores)
  const selectedStore = useShopStore((state) => state.selectedStore)
  const cart = useShopStore((state) => state.cart)
  const setUserStores = useShopStore((state) => state.setUserStores)
  const setSelectedStore = useShopStore((state) => state.setSelectedStore)
  const setCart = useShopStore((state) => state.setCart)
  const preorderActivations = useShopStore((state) => state.preorderActivations)
  const preorderBlocked = useShopStore((state) => state.preorderBlocked)
  const preorderGateLoading = useShopStore((state) => state.preorderGateLoading)
  const preorderGateError = useShopStore((state) => state.preorderGateError)
  const setPreorderGate = useShopStore((state) => state.setPreorderGate)
  const beginPreorderGateRequest = useShopStore((state) => state.beginPreorderGateRequest)
  const isPreorderGateRequestCurrent = useShopStore((state) => state.isPreorderGateRequestCurrent)
  const resetShop = useShopStore((state) => state.reset)
  const preorderGateBypassed = canBypassPreorderGate(access)
  const effectivePreorderBlocked = resolveEffectivePreorderGateBlocked(
    preorderBlocked || preorderGateLoading || preorderGateError,
    preorderGateBypassed,
  )
  const preorderPrompt = resolvePreorderPromptPresentation({
    storeCode: selectedStore?.storeCode,
    activationGuids: preorderActivations.map((item) => item.activationGuid),
    loading: preorderGateLoading,
    error: preorderGateError,
    bypassed: preorderGateBypassed,
    onPreorderPage: isPreorderPage,
  })

  const [categories, setCategories] = useState<WarehouseCategoryNode[]>([])
  const [loadingCategories, setLoadingCategories] = useState(false)
  const [cartDrawerOpen, setCartDrawerOpen] = useState(false)
  const [cartDrawerLoading, setCartDrawerLoading] = useState(false)
  const [mobileCategoryVisible, setMobileCategoryVisible] = useState(false)
  const [isHoverSupported, setIsHoverSupported] = useState(true)
  const [dismissedPreorderPromptKey, setDismissedPreorderPromptKey] = useState<string | null>(null)
  const preorderPromptOpen = preorderPrompt.mode === 'pending'
    && dismissedPreorderPromptKey !== preorderPrompt.key
  const selectedStoreCodeRef = useRef<string | null>(null)
  const cartDrawerOpenRef = useRef(false)
  const fullCartRequestVersionRef = useRef(0)
  selectedStoreCodeRef.current = selectedStore?.storeCode ?? null
  cartDrawerOpenRef.current = cartDrawerOpen

  const selectedCategory = useMemo(() => {
    return new URLSearchParams(location.search).get('category') || ''
  }, [location.search])

  useEffect(() => {
    setIsHoverSupported(supportsHover())
  }, [])

  useEffect(() => {
    let cancelled = false

    const fetchStores = async () => {
      if (!currentUser?.userGUID) {
        resetShop()
        return
      }

      try {
        const stores = (await getUserStores(currentUser.userGUID)).slice().sort((left, right) =>
          (left.storeName || left.storeCode || '').localeCompare(right.storeName || right.storeCode || '', undefined, {
            sensitivity: 'base',
          }),
        )
        if (cancelled) {
          return
        }

        setUserStores(stores)
        if (!selectedStore && stores.length === 1) {
          setSelectedStore(stores[0])
        }
      } catch (error) {
        if (!cancelled) {
          message.error(t('shop.loadStoresFailed', 'Failed to load stores'))
        }
      }
    }

    void fetchStores()

    return () => {
      cancelled = true
    }
  }, [currentUser?.userGUID, resetShop, selectedStore, setSelectedStore, setUserStores, t])

  useEffect(() => {
    let cancelled = false

    const fetchCategories = async () => {
      setLoadingCategories(true)
      try {
        const tree = await getCategoryTree()
        if (cancelled) {
          return
        }

        const allNode = tree.find((item) => item.categoryName.toLowerCase().includes('all'))
        const displayCategories = allNode?.children?.length ? allNode.children : tree
        setCategories(displayCategories)
      } catch (error) {
        if (!cancelled) {
          setCategories([])
        }
      } finally {
        if (!cancelled) {
          setLoadingCategories(false)
        }
      }
    }

    void fetchCategories()

    return () => {
      cancelled = true
    }
  }, [])

  const refreshCartSummary = useCallback(async () => {
    const storeCode = selectedStore?.storeCode ?? null
    if (!storeCode) {
      setCart(null)
      setCartDrawerLoading(false)
      return
    }

    try {
      const nextCart = await getActiveStoreOrderCartSummary(storeCode)
      if (selectedStoreCodeRef.current === storeCode) {
        setCart(nextCart)
      }
    } catch (error) {
      if (selectedStoreCodeRef.current === storeCode) {
        setCart(null)
      }
    }
  }, [selectedStore?.storeCode, setCart])

  const refreshPreorderGate = useCallback(async () => {
    const storeCode = selectedStore?.storeCode ?? null
    const requestToken = beginPreorderGateRequest()
    if (!storeCode) {
      if (isPreorderGateRequestCurrent(requestToken) && selectedStoreCodeRef.current === null) {
        setPreorderGate({ preorderActivations: [], preorderBlocked: true, preorderGateLoading: false, preorderGateError: false })
      }
      return
    }

    // 门禁刷新期间先保持关闭，避免切店时短暂沿用上一家分店的可订货状态。
    if (!isPreorderGateRequestCurrent(requestToken) || selectedStoreCodeRef.current !== storeCode) return
    setPreorderGate({ preorderActivations: [], preorderBlocked: true, preorderGateLoading: true, preorderGateError: false })
    const controller = new AbortController()
    // 远端数据库异常时及时结束全页等待，转为页面内错误提示和手动重试。
    const timeoutId = window.setTimeout(() => controller.abort(), PREORDER_GATE_TIMEOUT_MS)
    try {
      const result = await getActivePreorders(storeCode, controller.signal)
      if (isPreorderGateRequestCurrent(requestToken) && selectedStoreCodeRef.current === storeCode) {
        setPreorderGate({
          preorderActivations: result.activations,
          preorderBlocked: result.normalOrderBlocked,
          preorderGateLoading: false,
          preorderGateError: false,
        })
      }
    } catch {
      if (isPreorderGateRequestCurrent(requestToken) && selectedStoreCodeRef.current === storeCode) {
        setPreorderGate({ preorderActivations: [], preorderBlocked: true, preorderGateLoading: false, preorderGateError: true })
      }
    } finally {
      window.clearTimeout(timeoutId)
    }
  }, [beginPreorderGateRequest, isPreorderGateRequestCurrent, selectedStore?.storeCode, setPreorderGate])

  const handleOpenPreorder = useCallback(() => {
    const resolution = resolveShopPreorderNavigation({
      storeCode: selectedStore?.storeCode,
      activationGuid: preorderActivations[0]?.activationGuid,
      loading: preorderGateLoading,
      error: preorderGateError,
    })

    if (resolution.action === 'open') {
      navigate(`/shop/preorders/${resolution.activationGuid}`)
      return
    }
    if (resolution.action === 'select-store') {
      message.warning(t('shop.preorder.selectStoreFirst'))
      return
    }
    if (resolution.action === 'refresh') {
      message.info(t('shop.preorder.gateChecking'))
      void refreshPreorderGate()
      return
    }
    message.info(t('shop.preorder.noActive'))
  }, [navigate, preorderActivations, preorderGateError, preorderGateLoading, refreshPreorderGate, selectedStore?.storeCode, t])

  const refreshFullCart = useCallback(async () => {
    const storeCode = selectedStore?.storeCode ?? null
    if (!storeCode) {
      setCart(null)
      setCartDrawerLoading(false)
      return
    }

    const requestVersion = fullCartRequestVersionRef.current + 1
    fullCartRequestVersionRef.current = requestVersion
    setCartDrawerLoading(true)
    try {
      const nextCart = await getActiveStoreOrderCart(storeCode)
      if (selectedStoreCodeRef.current === storeCode) {
        setCart(nextCart)
      }
    } catch (error) {
      if (selectedStoreCodeRef.current === storeCode) {
        setCart(null)
      }
    } finally {
      if (fullCartRequestVersionRef.current === requestVersion) {
        setCartDrawerLoading(false)
      }
    }
  }, [selectedStore?.storeCode, setCart])

  useEffect(() => {
    // 切换分店先清掉旧购物车；抽屉已打开时直接补新门店明细，否则只拉摘要。
    setCart(null)
    void (cartDrawerOpenRef.current ? refreshFullCart() : refreshCartSummary())
    void refreshPreorderGate()
  }, [refreshCartSummary, refreshFullCart, refreshPreorderGate, setCart])

  useEffect(() => {
    if (!selectedStore?.storeCode) {
      return
    }

    const refreshVisibleCart = () => {
      if (document.visibilityState !== 'visible') {
        return
      }

      // 抽屉已打开时保留明细视图；否则只刷新顶部摘要，避免前台切回拖慢首屏。
      void (cartDrawerOpen ? refreshFullCart() : refreshCartSummary())
      void refreshPreorderGate()
    }

    const refreshFocusedCart = () => {
      void (cartDrawerOpen ? refreshFullCart() : refreshCartSummary())
      void refreshPreorderGate()
    }

    window.addEventListener('focus', refreshFocusedCart)
    document.addEventListener('visibilitychange', refreshVisibleCart)

    return () => {
      window.removeEventListener('focus', refreshFocusedCart)
      document.removeEventListener('visibilitychange', refreshVisibleCart)
    }
  }, [cartDrawerOpen, refreshCartSummary, refreshFullCart, refreshPreorderGate, selectedStore?.storeCode])

  const openCartDrawer = () => {
    setCartDrawerOpen(true)
    void refreshFullCart()
  }

  const buildMenuItems = (nodes: WarehouseCategoryNode[]): MenuProps['items'] => {
    return nodes.map((node) => {
      if (node.children?.length) {
        return {
          key: node.categoryGUID,
          label: node.categoryName,
          children: buildMenuItems(node.children),
        }
      }

      return {
        key: node.categoryGUID,
        label: node.categoryName,
        onClick: () => {
          navigate(`/shop?category=${node.categoryGUID}`)
          setMobileCategoryVisible(false)
        },
      }
    })
  }

  const handleLogout = async () => {
    await runAfterDurableLeave(requestPreorderDurableLeave, async () => {
      await logout()
      resetShop()
      navigate('/login', { replace: true })
    })
  }

  const handleSearch = (value: string) => {
    const keyword = value.trim()
    if (!keyword) {
      navigate('/shop')
      return
    }
    navigate(`/shop?keyword=${encodeURIComponent(keyword)}`)
  }

  const handleStoreChange = async (value?: string) => {
    await changeStoreAfterDurableLeave(value, requestPreorderDurableLeave, (storeCode) => {
      const nextStore = userStores.find((item) => item.storeCode === storeCode) ?? null
      setSelectedStore(nextStore)
    })
  }

  return (
    <div className="shop-layout">
      <div className="shop-top-bar">
        <div className="shop-shell">
          {currentUser ? (
            <span className="shop-account-info">{t('shop.account', 'ACCOUNT')}: {currentUser.username}</span>
          ) : (
            <Link to="/login">{t('login.submit', 'Login')}</Link>
          )}
          {access.canAccessDashboard && (
            <span onClick={() => window.open('/dashboard', '_blank')}>{t('menu.dashboard', 'Dashboard')}</span>
          )}
          <span onClick={() => navigate('/shop/best-sellers')}>{t('shop.bestSellers', 'Best Sellers')}</span>
          <span onClick={() => navigate('/shop/coming-soon')}>{t('shop.comingSoon', 'Coming Soon')}</span>
          <span onClick={() => navigate('/shop/orders')}>{t('shop.orderHistory')}</span>
          <span onClick={() => void handleLogout()}>{t('layout.logout', 'Log Out')}</span>
          <LanguageSwitch className="shop-top-language-switch" size="small" />
        </div>
      </div>

      <div className="shop-main-header">
        <div className="shop-shell">
          <div className="shop-logo" onClick={() => navigate('/shop')}>
            <div className="shop-logo-inner">
              <div className="shop-hb-logo">
                <div className="shop-hb-circle" />
                <div className="shop-hb-text">HB</div>
              </div>
              <div className="shop-brand-text">
                <div className="shop-brand-main">
                  <span className="shop-brand-hot">HOT</span>
                  <span className="shop-brand-bargain">BARGAIN</span>
                </div>
                <div className="shop-brand-sub">Platform</div>
              </div>
            </div>
          </div>

          <div className="shop-header-actions">
            <div className="shop-cart-entry" onClick={openCartDrawer}>
              <div className="shop-cart-entry-label">
                <Badge count={cart?.totalQuantity ?? 0} size="small" offset={[8, -8]}>
                  <ShoppingCartOutlined style={{ fontSize: 20 }} />
                </Badge>
                <span>{t('shop.shoppingCart', 'Shopping Cart')}</span>
              </div>
              <div className="shop-cart-entry-value">
                <ShopCartSummary cart={cart} />
              </div>
            </div>

            <div className="shop-selector-wrap">
              <Select
                placeholder={t('shop.selectStore', 'Select Store')}
                className="shop-selector"
                value={selectedStore?.storeCode}
                onChange={(value) => void handleStoreChange(value)}
                allowClear
                options={userStores.map((item) => ({
                  value: item.storeCode,
                  label: item.storeName,
                }))}
              />
            </div>

            <Button className="shop-checkout-btn" onClick={openCartDrawer}>
              {t('shop.checkout', 'Checkout')} »
            </Button>

            <Search
              placeholder={t('shop.productSearch', 'Product Search')}
              onSearch={handleSearch}
              className="shop-search-bar"
              enterButton
            />
          </div>
        </div>
      </div>

      <div className="shop-mobile-header">
        <div className="shop-mobile-top-row">
          <div className="shop-mobile-logo" onClick={() => navigate('/shop')}>
            <div className="shop-hb-logo">
              <div className="shop-hb-circle" />
              <div className="shop-hb-text">HB</div>
            </div>
          </div>
          <div className="shop-mobile-search">
            <Search placeholder={t('shop.productSearch', 'Product Search')} onSearch={handleSearch} enterButton />
          </div>
          <LanguageSwitch className="shop-mobile-language-switch" size="small" compact />
        </div>
        <div className="shop-mobile-grid">
          <div className="shop-mobile-grid-item" onClick={() => setMobileCategoryVisible(true)}>
            <MenuOutlined className="icon" />
            <span>{t('shop.products', 'Products')}</span>
          </div>
          <div className="shop-mobile-grid-item" onClick={handleOpenPreorder}>
            <GiftOutlined className="icon" />
            <span>{t('shop.preorder.navigation', 'Preorder')}</span>
          </div>
          <div className="shop-mobile-grid-item" onClick={() => navigate('/shop/best-sellers')}>
            <AppstoreOutlined className="icon" />
            <span>{t('shop.bestSellers', 'Best Sellers')}</span>
          </div>
          <div className="shop-mobile-grid-item" onClick={() => navigate('/shop/coming-soon')}>
            <AppstoreOutlined className="icon" />
            <span>{t('shop.comingSoon', 'Coming Soon')}</span>
          </div>
          <div className="shop-mobile-grid-item shop-mobile-store-item">
            <Select
              placeholder={t('common.store', 'Store')}
              className="shop-mobile-store-select"
              value={selectedStore?.storeCode}
              onChange={(value) => void handleStoreChange(value)}
              allowClear
              options={userStores.map((item) => ({
                value: item.storeCode,
                label: item.storeName,
              }))}
            />
          </div>
          <div className="shop-mobile-grid-item" onClick={openCartDrawer}>
            <Badge count={cart?.totalQuantity ?? 0} size="small" offset={[5, -5]}>
              <ShoppingCartOutlined className="icon" />
            </Badge>
            <span>{t('shop.cart', 'Cart')}</span>
          </div>
          <div className="shop-mobile-grid-item" onClick={() => navigate('/shop/orders')}>
            <AppstoreOutlined className="icon" />
            <span>{t('shop.orderHistory', '订单')}</span>
          </div>
          <div className="shop-mobile-grid-item" onClick={() => void handleLogout()}>
            <UserOutlined className="icon" />
            <span>{t('layout.logout', 'Logout')}</span>
          </div>
          {access.canAccessDashboard && (
            <div className="shop-mobile-grid-item" onClick={() => window.open('/dashboard', '_blank')}>
              <AppstoreOutlined className="icon" />
              <span>{t('menu.dashboard', 'Dashboard')}</span>
            </div>
          )}
        </div>
      </div>

      <div className="shop-nav-bar">
        <div className="shop-orange-menu">
          <div
            className={`shop-menu-item${isShopHomePage ? ' active' : ''}`}
            onClick={() => navigate('/shop')}
          >
            {t('shop.shopHome', 'Shop Home')}
          </div>
          <div
            className={`shop-menu-item${isPreorderPage ? ' active' : ''}`}
            onClick={handleOpenPreorder}
          >
            {t('shop.preorder.navigation', 'Preorder')}
          </div>
          <div
            className={`shop-menu-item${isBestSellersPage ? ' active' : ''}`}
            onClick={() => navigate('/shop/best-sellers')}
          >
            {t('shop.bestSellers', 'Best Sellers')}
          </div>
          <div
            className={`shop-menu-item${isComingSoonPage ? ' active' : ''}`}
            onClick={() => navigate('/shop/coming-soon')}
          >
            {t('shop.comingSoon', 'Coming Soon')}
          </div>
          <div
            className={`shop-menu-item${isOrdersPage ? ' active' : ''}`}
            onClick={() => navigate('/shop/orders')}
          >
            {t('shop.orderHistory', '历史订单')}
          </div>
        </div>

        {isShopHomePage ? (
          <div className="shop-blue-menu">
            <div className="shop-shell">
              {loadingCategories ? (
                <div className="shop-category-loading">
                  <Spin size="small" /> {t('shop.loadingCategories', 'Loading categories...')}
                </div>
              ) : (
                categories.map((category) => {
                  const childMenus = category.children?.length
                    ? (buildMenuItems(category.children) ?? [])
                    : []
                  const content = (
                    <div
                      className={`shop-category-item${selectedCategory === category.categoryGUID ? ' active' : ''}`}
                      onClick={() => navigate(`/shop?category=${category.categoryGUID}`)}
                    >
                      {category.categoryName}
                      {childMenus.length ? <DownOutlined style={{ fontSize: 10, marginLeft: 4 }} /> : null}
                    </div>
                  )

                  if (!childMenus.length) {
                    return <div key={category.categoryGUID}>{content}</div>
                  }

                  return (
                    <Dropdown
                      key={category.categoryGUID}
                      menu={{
                        items: childMenus,
                        triggerSubMenuAction: isHoverSupported ? 'hover' : 'click',
                      }}
                      overlayClassName="shop-category-dropdown"
                      trigger={isHoverSupported ? ['hover'] : ['click']}
                    >
                      {content}
                    </Dropdown>
                  )
                })
              )}
            </div>
          </div>
        ) : (
          <div className="shop-orders-banner">
            <div className="shop-shell">
              <div className="shop-orders-banner-title">{t(shopBannerCopy.titleKey, shopBannerCopy.titleFallback)}</div>
              <div className="shop-orders-banner-subtitle">
                {t(shopBannerCopy.subtitleKey, shopBannerCopy.subtitleFallback)}
              </div>
            </div>
          </div>
        )}
      </div>

      <div className="shop-content">
        {selectedStore && effectivePreorderBlocked ? (
          <Alert
            className="shop-preorder-gate-alert"
            type={preorderGateError ? 'error' : 'warning'}
            showIcon
            message={preorderGateError ? t('shop.preorder.gateUnavailable') : preorderGateLoading ? t('shop.preorder.gateChecking') : t('shop.preorder.gateBlocked', { count: preorderActivations.length })}
            description={preorderGateError ? t('shop.preorder.gateErrorDescription') : t('shop.preorder.gateBlockedDescription')}
            action={<Space>{preorderGateError ? <Button size="small" onClick={() => void refreshPreorderGate()}>{t('shop.preorder.retry')}</Button> : null}{preorderActivations[0] ? <Button size="small" type="primary" onClick={() => navigate(`/shop/preorders/${preorderActivations[0].activationGuid}`)}>{t('shop.preorder.enterPreorder')}</Button> : null}</Space>}
          />
        ) : null}
        <Outlet />
      </div>

      <div className="shop-footer">{t('shop.footer', '© 2026 Hotbargain International. All rights reserved.')}</div>

      <Modal
        open={preorderPromptOpen}
        title={(
          <Space>
            {preorderPrompt.mode === 'checking' ? <Spin size="small" /> : <GiftOutlined />}
            <span>{preorderPrompt.mode === 'pending'
              ? t('shop.preorder.pendingTitle', { count: preorderActivations.length })
              : preorderPrompt.mode === 'error'
                ? t('shop.preorder.gateUnavailable')
                : t('shop.preorder.gateChecking')}</span>
          </Space>
        )}
        onCancel={() => setDismissedPreorderPromptKey(preorderPrompt.key)}
        footer={[
          <Button key="later" onClick={() => setDismissedPreorderPromptKey(preorderPrompt.key)}>
            {t('shop.preorder.later')}
          </Button>,
          preorderPrompt.mode === 'checking' ? (
            <Button key="checking" type="primary" loading disabled>
              {t('shop.preorder.gateChecking')}
            </Button>
          ) : (
            <Button
              key="action"
              type="primary"
              onClick={() => {
                if (preorderPrompt.mode === 'error') {
                  setDismissedPreorderPromptKey(null)
                  void refreshPreorderGate()
                  return
                }
                handleOpenPreorder()
              }}
            >
              {preorderPrompt.mode === 'error'
                ? t('shop.preorder.retry')
                : t('shop.preorder.enterPreorder')}
            </Button>
          ),
        ]}
      >
        {preorderPrompt.mode === 'pending' ? (
          <Space direction="vertical" size={8}>
            {preorderActivations.map((item) => (
              <div key={item.activationGuid}>
                <strong>{item.templateName} · {t('shop.preorder.period', { sequence: item.sequenceNumber })}</strong>
                <br />
                <span>{t('shop.preorder.deadline', { date: preorderDateTimeFormatter.format(new Date(item.endAtUtc)) })}</span>
              </div>
            ))}
          </Space>
        ) : (
          <span>{preorderPrompt.mode === 'error'
            ? t('shop.preorder.gateErrorDescription')
            : t('shop.preorder.checkingDescription')}</span>
        )}
      </Modal>

      <ShopCartDrawer
        open={cartDrawerOpen}
        onClose={() => setCartDrawerOpen(false)}
        cart={cart}
        loading={cartDrawerLoading}
        preorderBlocked={effectivePreorderBlocked}
        onOpenPreorder={preorderActivations[0] ? () => navigate(`/shop/preorders/${preorderActivations[0].activationGuid}`) : undefined}
        onPreorderRequired={refreshPreorderGate}
        onCartChanged={refreshFullCart}
      />

      <Drawer
        title={t('shop.products', 'Products')}
        placement="left"
        onClose={() => setMobileCategoryVisible(false)}
        open={mobileCategoryVisible}
        width="85%"
      >
        <Menu mode="inline" items={buildMenuItems(categories)} selectedKeys={selectedCategory ? [selectedCategory] : []} />
      </Drawer>
    </div>
  )
}
