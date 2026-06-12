import { Button, Result } from 'antd'
import type { ErrorInfo, ReactNode } from 'react'
import { Component } from 'react'
import { useTranslation } from 'react-i18next'
import { reportRuntimeError } from '../utils/centerLogClient'

interface GlobalErrorBoundaryProps {
  children: ReactNode
}

interface GlobalErrorBoundaryState {
  hasError: boolean
}

class GlobalErrorBoundaryInner extends Component<
  GlobalErrorBoundaryProps & {
    title: string
    subtitle: string
    reloadText: string
  },
  GlobalErrorBoundaryState
> {
  state: GlobalErrorBoundaryState = {
    hasError: false,
  }

  static getDerivedStateFromError() {
    return { hasError: true }
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    // React 渲染期异常不会进入 window.onerror，这里单独补一条兜底上报。
    reportRuntimeError('react-error-boundary', error, {
      componentStack: errorInfo.componentStack,
    })
  }

  render() {
    if (this.state.hasError) {
      return (
        <Result
          status="error"
          title={this.props.title}
          subTitle={this.props.subtitle}
          extra={
            <Button type="primary" onClick={() => window.location.reload()}>
              {this.props.reloadText}
            </Button>
          }
        />
      )
    }

    return this.props.children
  }
}

export default function GlobalErrorBoundary({ children }: GlobalErrorBoundaryProps) {
  const { t } = useTranslation()

  return (
    <GlobalErrorBoundaryInner
      title={t('system.centerLogs.runtimeErrorTitle')}
      subtitle={t('system.centerLogs.runtimeErrorSubtitle')}
      reloadText={t('common.refresh')}
    >
      {children}
    </GlobalErrorBoundaryInner>
  )
}
