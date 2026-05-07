import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { App as AntdApp, ConfigProvider, Result, Spin, theme } from 'antd'
import { useEffect } from 'react'
import { useLocation } from 'react-router-dom'
import AdminLayout from './layout/AdminLayout'
import LoginPage from './pages/Login'
import { useAuthStore } from './store/auth'

function AppBootstrap() {
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
      <Route
        path="/login"
        element={currentUser ? <Navigate to="/dashboard" replace /> : <LoginPage />}
      />
      <Route
        path="/*"
        element={currentUser ? <AdminLayout /> : <Navigate to="/login" replace />}
      />
      <Route
        path="*"
        element={<Result status="404" title="404" subTitle="页面不存在" />}
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
