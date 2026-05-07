import { Space, Typography } from 'antd'
import type { PropsWithChildren, ReactNode } from 'react'

interface PageContainerProps extends PropsWithChildren {
  title: string
  subtitle?: string
  extra?: ReactNode
}

export default function PageContainer({ title, subtitle, extra, children }: PageContainerProps) {
  return (
    <div className="page-container">
      <div className="page-header">
        <Space direction="vertical" size={4}>
          <Typography.Title level={4} style={{ margin: 0 }}>
            {title}
          </Typography.Title>
          {subtitle ? (
            <Typography.Text type="secondary">{subtitle}</Typography.Text>
          ) : null}
        </Space>
        {extra}
      </div>
      {children}
    </div>
  )
}
