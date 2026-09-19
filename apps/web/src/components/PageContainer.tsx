import { Space, Typography } from 'antd'
import type { PropsWithChildren, ReactNode } from 'react'

interface PageContainerProps extends PropsWithChildren {
  title: string
  subtitle?: string
  extra?: ReactNode
  /**
   * 紧凑页头：副标题（如记录总数）与标题同行、上下留白收窄，用于数据密集的列表页。
   * 默认关闭，不传时与原有页头完全一致。
   */
  compact?: boolean
}

export default function PageContainer({ title, subtitle, extra, compact = false, children }: PageContainerProps) {
  return (
    <div className="page-container">
      <div className={compact ? 'page-header page-header-compact' : 'page-header'}>
        {compact ? (
          <div className="page-header-compact-title">
            <Typography.Title level={4} style={{ margin: 0 }}>
              {title}
            </Typography.Title>
            {subtitle ? (
              <Typography.Text type="secondary" className="page-header-compact-subtitle">
                {subtitle}
              </Typography.Text>
            ) : null}
          </div>
        ) : (
          <Space direction="vertical" size={4}>
            <Typography.Title level={4} style={{ margin: 0 }}>
              {title}
            </Typography.Title>
            {subtitle ? (
              <Typography.Text type="secondary">{subtitle}</Typography.Text>
            ) : null}
          </Space>
        )}
        {extra}
      </div>
      {children}
    </div>
  )
}
