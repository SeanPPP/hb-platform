import { Button } from 'antd'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import './listToolbar.css'

interface SelectionActionBarProps {
  selectedCount: number
  onClearSelection: () => void
  /** 只对选中行生效的操作按钮。 */
  children: ReactNode
}

/**
 * 勾选后操作条：只在有选中行时出现。
 * 原先批量按钮常驻页头，未勾选时是一排灰色禁用按钮，既占位置又看不出何时可用。
 */
export default function SelectionActionBar({ selectedCount, onClearSelection, children }: SelectionActionBarProps) {
  const { t } = useTranslation()
  if (selectedCount <= 0) {
    return null
  }

  return (
    <div className="list-toolbar-selection-bar" role="region" aria-live="polite">
      <strong className="list-toolbar-selection-count">
        {t('common.listToolbar.selectedCount', { count: selectedCount, defaultValue: '已选 {{count}} 项' })}
      </strong>
      <div className="list-toolbar-selection-actions">{children}</div>
      <Button type="link" size="small" className="list-toolbar-selection-clear" onClick={onClearSelection}>
        {t('common.listToolbar.cancelSelection', '取消选择')}
      </Button>
    </div>
  )
}
