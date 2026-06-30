import {
  AppstoreOutlined,
  DownOutlined,
  MenuOutlined,
  ShoppingCartOutlined,
  UserOutlined,
} from '@ant-design/icons'
import { Badge, Button, Drawer, Dropdown, Input, Menu, Select, Spin, message } from 'antd'
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
import { useAuthStore } from '../store/auth'
import { useShopStore } from '../store/shop'
import { resolveShopBannerCopy } from './shopBannerCopy'

const { Search } = Input

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
  const { t } = useTranslation()
  const isShopHomePage = location.pathname === '/shop'
  const isBestSellersPage = location.pathname.startsWith('/shop/best-sellers')
  const isComingSoonPage = location.pathname.startsWith('/shop/coming-soon')
  const isOrdersPage = location.pathname.startsWith('/shop/orders')
  const shopBannerCopy = useMemo(() => resolveShopBannerCopy(location.pathname), [location.pathname])

  const userStores = useShopStore((state) => state.userStores)
  const selectedStore = useShopStore((state) => state.selectedStore)
  const cart = useShopStore((state) => state.cart)
  const setUserStores = useShopStore((state) => state.setUserStores)
  const setSelectedStore = useShopStore((state) => state.setSelectedStore)
  const setCart = useShopStore((state) => state.setCart)
  const resetShop = useShopStore((state) => state.reset)

  const [categories, setCategories] = useState<WarehouseCategoryNode[]>([])
  const [loadingCategories, setLoadingCategories] = useState(false)
  const [cartDrawerOpen, setCartDrawerOpen] = useState(false)
  const [cartDrawerLoading, setCartDrawerLoading] = useState(false)
  const [mobileCategoryVisible, setMobileCategoryVisible] = useState(false)
  const [isHoverSupported, setIsHoverSupported] = useState(true)
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
  }, [refreshCartSummary, refreshFullCart, setCart])

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
    }

    const refreshFocusedCart = () => {
      void (cartDrawerOpen ? refreshFullCart() : refreshCartSummary())
    }

    window.addEventListener('focus', refreshFocusedCart)
    document.addEventListener('visibilitychange', refreshVisibleCart)

    return () => {
      window.removeEventListener('focus', refreshFocusedCart)
      document.removeEventListener('visibilitychange', refreshVisibleCart)
    }
  }, [cartDrawerOpen, refreshCartSummary, refreshFullCart, selectedStore?.storeCode])

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
    await logout()
    resetShop()
    navigate('/login', { replace: true })
  }

  const handleSearch = (value: string) => {
    const keyword = value.trim()
    if (!keyword) {
      navigate('/shop')
      return
    }
    navigate(`/shop?keyword=${encodeURIComponent(keyword)}`)
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
                onChange={(value) => {
                  const nextStore = userStores.find((item) => item.storeCode === value) ?? null
                  setSelectedStore(nextStore)
                }}
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
              onChange={(value) => {
                const nextStore = userStores.find((item) => item.storeCode === value) ?? null
                setSelectedStore(nextStore)
              }}
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
        <Outlet />
      </div>

      <div className="shop-footer">{t('shop.footer', '© 2026 Hotbargain International. All rights reserved.')}</div>

      <ShopCartDrawer
        open={cartDrawerOpen}
        onClose={() => setCartDrawerOpen(false)}
        cart={cart}
        loading={cartDrawerLoading}
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
