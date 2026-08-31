import { Button, Result, Spin } from 'antd'
import { Component, Suspense } from 'react'
import type { ErrorInfo, ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { reportRuntimeError } from '../utils/centerLogClient'

interface RouteLoadBoundaryProps {
  children: ReactNode
  resetKey: string
}

interface RouteLoadBoundaryState {
  hasError: boolean
}

interface RouteLoadBoundaryInnerProps extends RouteLoadBoundaryProps {
  reloadText: string
  subtitle: string
  title: string
}

class RouteLoadBoundaryInner extends Component<RouteLoadBoundaryInnerProps, RouteLoadBoundaryState> {
  state: RouteLoadBoundaryState = {
    hasError: false,
  }

  static getDerivedStateFromError() {
    return { hasError: true }
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    // 路由 chunk 或页面渲染失败时只替换内容区，并沿用现有中心日志上报链路。
    reportRuntimeError('react-error-boundary', error, {
      componentStack: errorInfo.componentStack,
      pathname: typeof window === 'undefined' ? undefined : window.location.pathname,
      runtimeBoundary: 'route-load',
    })
  }

  componentDidUpdate(previousProps: RouteLoadBoundaryInnerProps) {
    if (this.state.hasError && previousProps.resetKey !== this.props.resetKey) {
      this.setState({ hasError: false })
    }
  }

  render() {
    if (this.state.hasError) {
      return (
        <Result
          status="error"
          title={this.props.title}
          subTitle={this.props.subtitle}
          extra={(
            <Button type="primary" onClick={() => window.location.reload()}>
              {this.props.reloadText}
            </Button>
          )}
        />
      )
    }

    return (
      <Suspense
        fallback={(
          <div className="app-loading">
            <Spin size="large" />
          </div>
        )}
      >
        {this.props.children}
      </Suspense>
    )
  }
}

export default function RouteLoadBoundary({ children, resetKey }: RouteLoadBoundaryProps) {
  const { t } = useTranslation()

  return (
    <RouteLoadBoundaryInner
      resetKey={resetKey}
      title={t('system.centerLogs.runtimeErrorTitle')}
      subtitle={t('system.centerLogs.runtimeErrorSubtitle')}
      reloadText={t('common.refresh')}
    >
      {children}
    </RouteLoadBoundaryInner>
  )
}
