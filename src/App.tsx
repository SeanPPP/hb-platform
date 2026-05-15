import { App as AntdApp, ConfigProvider, Result, Spin, theme } from 'antd'
import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { BrowserRouter, Navigate, Route, Routes, useLocation } from 'react-router-dom'
import AdminLayout from './layout/AdminLayout'
import ShopLayout from './layout/ShopLayout'
import LoginPage from './pages/Login'
import ShopBestSellersPage from './pages/ShopBestSellers'
import ShopComingSoonPage from './pages/ShopComingSoon'
import ShopHomePage from './pages/ShopHome'
import ShopOrderDetailPage from './pages/ShopOrderDetail'
import ShopOrdersPage from './pages/ShopOrders'
import { useAuthStore } from './store/auth'

function AppBootstrap() {
  const { t } = useTranslation()
  const { initialized, loading, currentUser, fetchCurrentUser } = useAuthStore()
  const location = useLocation()
  const isLoginPath = location.pathname === '/login'

  useEffect(() => {
    if (!initialized && !loading && !isLoginPath) {
      void fetchCurrentUser()
    }
  }, [fetchCurrentUser, initialized, isLoginPath, loading])

  if ((!initialized || loading) && !isLoginPath) {
    return (
      <div className="app-loading">
        <Spin size="large" fullscreen />
      </div>
    )
  }

  return (
    <Routes>
      <Route path="/" element={<Navigate to="/dashboard" replace />} />
      <Route
        path="/login"
        element={currentUser ? <Navigate to="/dashboard" replace /> : <LoginPage />}
      />
      <Route
        path="/shop"
        element={currentUser ? <ShopLayout /> : <Navigate to="/login" replace />}
      >
        <Route index element={<ShopHomePage />} />
        <Route path="best-sellers" element={<ShopBestSellersPage />} />
        <Route path="coming-soon" element={<ShopComingSoonPage />} />
        <Route path="orders" element={<ShopOrdersPage />} />
        <Route path="orders/:id" element={<ShopOrderDetailPage />} />
      </Route>
      <Route
        path="/*"
        element={currentUser ? <AdminLayout /> : <Navigate to="/login" replace />}
      />
      <Route
        path="*"
        element={<Result status="404" title="404" subTitle={t('menu.pageNotFound', '页面不存在')} />}
      />
    </Routes>
  )
}

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
        <BrowserRouter>
          <AppBootstrap />
        </BrowserRouter>
      </AntdApp>
    </ConfigProvider>
  )
}
