import { FilterOutlined } from '@ant-design/icons'
import { Badge, Button, Popover } from 'antd'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import './listToolbar.css'

interface MoreFiltersButtonProps {
  /** 收进弹层里、当前已设置的筛选项数量，显示为按钮角标。 */
  activeCount: number
  children: ReactNode
}

/**
 * 「更多筛选」弹层：低频筛选项收在这里，主筛选栏保持一行。
 * 角标提示弹层内有条件生效，避免用户忘记收起来的条件。
 */
export default function MoreFiltersButton({ activeCount, children }: MoreFiltersButtonProps) {
  const { t } = useTranslation()

  return (
    <Popover
      trigger="click"
      placement="bottomLeft"
      content={<div className="list-toolbar-more-panel">{children}</div>}
    >
      <Button icon={<FilterOutlined />}>
        {t('common.listToolbar.moreFilters', '更多筛选')}
        {activeCount > 0 ? <Badge count={activeCount} size="small" className="list-toolbar-more-badge" /> : null}
      </Button>
    </Popover>
  )
}
