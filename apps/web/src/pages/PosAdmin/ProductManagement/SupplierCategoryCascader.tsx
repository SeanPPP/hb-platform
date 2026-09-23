import { Cascader, theme } from 'antd'
import type { CSSProperties } from 'react'
import { formatCascaderDisplayLabels } from './supplierCategoryFilter'
import { SUPPLIER_CATEGORY_RETRY_VALUE, type SupplierCategoryCascaderOption } from './supplierCategoryOptions'

export interface SupplierCategoryCascaderProps {
  options: SupplierCategoryCascaderOption[]
  value?: string[]
  onChange: (value: string[] | undefined) => void
  /** 展开某个供应商且其分类树未加载时触发。 */
  onLoadSupplier: (supplierCode: string) => void
  /** 点击「加载失败，点击重试」伪节点时触发，不改变筛选条件。 */
  onRetrySupplier: (supplierCode: string) => void
  placeholder?: string
  style?: CSSProperties
}

/**
 * 顶部「供应商分类」级联筛选：第一层供应商，下面是该供应商的分类树（200 为仓库分类）。
 * 与现有顶部商品分类 Cascader 一致用 changeOnSelect，不开 showSearch（与 loadData 懒加载互斥）。
 */
export default function SupplierCategoryCascader({
  options,
  value,
  onChange,
  onLoadSupplier,
  onRetrySupplier,
  placeholder,
  style,
}: SupplierCategoryCascaderProps) {
  const { token } = theme.useToken()

  const handleChange = (nextValue: unknown) => {
    const path = Array.isArray(nextValue) ? nextValue.map(String) : []
    // 重试伪节点只触发重新加载，不能写进筛选条件。
    if (path[path.length - 1] === SUPPLIER_CATEGORY_RETRY_VALUE) {
      if (path[0]) onRetrySupplier(path[0])
      return
    }
    onChange(path.length ? path : undefined)
  }

  return (
    <Cascader
      allowClear
      changeOnSelect
      placeholder={placeholder}
      style={style}
      options={options}
      value={value}
      onChange={handleChange}
      loadData={(selectedOptions) => {
        const supplierCode = selectedOptions[0]?.value
        if (supplierCode !== undefined && supplierCode !== null) onLoadSupplier(String(supplierCode))
      }}
      displayRender={(labels) => formatCascaderDisplayLabels(labels.map(String))}
      optionRender={(option) => {
        const { kind, label } = option as SupplierCategoryCascaderOption
        if (kind === 'unassigned') {
          return <span className="pos-products-supplier-category-unassigned-option">{label}</span>
        }
        if (kind === 'retry') {
          return <span style={{ color: token.colorError }}>{label}</span>
        }
        return label
      }}
    />
  )
}
