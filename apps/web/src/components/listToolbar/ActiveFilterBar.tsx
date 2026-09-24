import { CloseOutlined } from '@ant-design/icons'
import { Button, Typography } from 'antd'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import './listToolbar.css'

export interface ActiveFilterItem {
  key: string
  /** 条件名，如「分类」「零售价」。 */
  label: string
  /** 条件值的可读摘要，如「跑马节帽子」「≥ 5.00」。 */
  value: ReactNode
  /** 来源：顶部筛选栏或列头放大镜。列头来源用不同底色区分。 */
  source: 'toolbar' | 'column'
  onRemove: () => void
}

interface ActiveFilterBarProps {
  items: ActiveFilterItem[]
  onClearAll: () => void
  /** 行尾附加内容，如「只看未分类」这类快捷开关。 */
  extra?: ReactNode
}

/**
 * 已生效筛选条：把顶部筛选栏与列头筛选里当前生效的条件汇总成可单独移除的标签。
 * 原先列头放大镜里的条件在界面上不可见，用户常常不知道列表为什么少了数据。
 */
export default function ActiveFilterBar({ items, onClearAll, extra }: ActiveFilterBarProps) {
  const { t } = useTranslation()

  return (
    <div className="list-toolbar-active-bar">
      <span className="list-toolbar-active-label">{t('common.listToolbar.activeFilters', '已生效')}</span>
      {items.length ? (
        <>
          {items.map((item) => (
            <span
              key={item.key}
              className={`list-toolbar-chip ${item.source === 'column' ? 'list-toolbar-chip-column' : ''}`}
            >
              {item.source === 'column' ? (
                <span className="list-toolbar-chip-source">{t('common.listToolbar.columnFilterSource', '列')}</span>
              ) : null}
              <span className="list-toolbar-chip-text">
                {item.label}：{item.value}
              </span>
              <button
                type="button"
                className="list-toolbar-chip-remove"
                aria-label={t('common.listToolbar.removeFilter', { label: item.label, defaultValue: '移除筛选条件：{{label}}' })}
                onClick={item.onRemove}
              >
                <CloseOutlined />
              </button>
            </span>
          ))}
          <Button type="link" size="small" className="list-toolbar-clear-all" onClick={onClearAll}>
            {t('common.listToolbar.clearAllFilters', '清空全部')}
          </Button>
        </>
      ) : (
        <Typography.Text type="secondary" className="list-toolbar-empty-hint">
          {t('common.listToolbar.noActiveFilters', '暂无筛选条件，列头里设置的条件也会显示在这里')}
        </Typography.Text>
      )}
      {extra ? <span className="list-toolbar-active-extra">{extra}</span> : null}
    </div>
  )
}
