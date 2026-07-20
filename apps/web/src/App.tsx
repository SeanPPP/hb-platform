import { App as AntdApp, ConfigProvider, Result, Spin, theme } from 'antd'
import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { createBrowserRouter, Navigate, Route, RouterProvider, Routes, useLocation } from 'react-router-dom'
import GlobalErrorBoundary from './components/GlobalErrorBoundary'
import AdminLayout from './layout/AdminLayout'
import ShopLayout from './layout/ShopLayout'
import LoginPage from './pages/Login'
import ShopBestSellersPage from './pages/ShopBestSellers'
import ShopComingSoonPage from './pages/ShopComingSoon'
import ShopHomePage from './pages/ShopHome'
import ShopOrderDetailPage from './pages/ShopOrderDetail'
import ShopOrdersPage from './pages/ShopOrders'
import ShopPreorderPage from './pages/ShopPreorder'
import { ShopPreorderLeaveProvider } from './pages/ShopPreorder/preorderLeaveContext'
import ForbiddenPage from './pages/Forbidden'
import WebAccessDeniedPage from './pages/WebAccessDenied'
import { useAuthStore } from './store/auth'
import { AUTH_EXPIRED_EVENT } from './utils/request'
import { getDefaultWebPath, WEB_NO_ACCESS_PATH } from './utils/webPortalAccess'

function AppBootstrap() {
  const { t } = useTranslation()
  const { initialized, loading, currentUser, access, fetchCurrentUser, clearAuth } = useAuthStore()
  const location = useLocation()
  const isLoginPath = location.pathname === '/login'

  useEffect(() => {
    if (!initialized && !loading && !isLoginPath) {
      void fetchCurrentUser()
    }
  }, [fetchCurrentUser, initialized, isLoginPath, loading])

  useEffect(() => {
    window.addEventListener(AUTH_EXPIRED_EVENT, clearAuth)
    return () => window.removeEventListener(AUTH_EXPIRED_EVENT, clearAuth)
  }, [clearAuth])

  const homePage = getDefaultWebPath(access)
  const portalDeniedPage = homePage === WEB_NO_ACCESS_PATH
    ? <Navigate to={WEB_NO_ACCESS_PATH} replace />
    : <ForbiddenPage />

  if ((!initialized || loading) && !isLoginPath) {
    return (
      <div className="app-loading">
        <Spin size="large" fullscreen />
      </div>
    )
  }

  return (
    <Routes>
      <Route path="/" element={<Navigate to={homePage} replace />} />
      <Route
        path="/login"
        element={currentUser ? <Navigate to={homePage} replace /> : <LoginPage />}
      />
      <Route
        path={WEB_NO_ACCESS_PATH}
        element={
          currentUser
            ? (homePage === WEB_NO_ACCESS_PATH ? <WebAccessDeniedPage /> : <Navigate to={homePage} replace />)
            : <Navigate to="/login" replace />
        }
      />
      <Route
        path="/shop"
        element={
          currentUser
            ? (access.canAccessOrderFront ? <ShopPreorderLeaveProvider><ShopLayout /></ShopPreorderLeaveProvider> : portalDeniedPage)
            : <Navigate to="/login" replace />
        }
      >
        <Route index element={<ShopHomePage />} />
        <Route path="best-sellers" element={<ShopBestSellersPage />} />
        <Route path="coming-soon" element={<ShopComingSoonPage />} />
        <Route path="orders" element={<ShopOrdersPage />} />
        <Route path="orders/:id" element={<ShopOrderDetailPage />} />
        <Route path="preorders/:activationGuid" element={<ShopPreorderPage />} />
      </Route>
      <Route
        path="/*"
        element={
          currentUser
            ? (access.canAccessAdminShell ? <AdminLayout /> : portalDeniedPage)
            : <Navigate to="/login" replace />
        }
      />
      <Route
        path="*"
        element={<Result status="404" title="404" subTitle={t('menu.pageNotFound', '页面不存在')} />}
      />
    </Routes>
  )
}

// 使用 Data Router 承载现有 Routes，页面才能使用官方 useBlocker 在站内离页前保护未保存数据。
const router = createBrowserRouter([{ path: '*', element: <AppBootstrap /> }])

export default function App() {
  return (
    <ConfigProvider
      theme={{
        algorithm: theme.defaultAlgorithm,
        token: {
          colorPrimary: '#1677ff',
          borderRadius: 10,
        },
      }}
    >
      <AntdApp>
        <GlobalErrorBoundary>
          <RouterProvider router={router} />
        </GlobalErrorBoundary>
      </AntdApp>
    </ConfigProvider>
  )
}
