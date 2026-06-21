/**
 * EditableCells — 单据编辑内联编辑单元格组件
 *
 * 职责边界：
 * - EditableTextCell：双击编辑文本，Enter 提交 / Esc 取消
 * - EditableNumberCell：双击编辑数字，Enter 提交 / Esc 取消
 * - EditableBooleanCell：双击切换布尔值
 * - 纯展示组件，所有保存逻辑由父组件通过 onSave 回调注入
 * - 不接管任何数据加载或业务逻辑
 */

import { Input, InputNumber, Tag, Tooltip } from 'antd'
import { useEffect, useState, type CSSProperties, type KeyboardEvent, type ReactNode } from 'react'
import type { InvoiceDetailInlineEditableField } from './inlineEdit'

// ---- 类型 ----

type InlineCellSaveHandler = (
  detailGuid: string,
  field: InvoiceDetailInlineEditableField,
  value: unknown,
) => void

// ---- 工具 ----

function formatAmount(value?: number) {
  if (value === undefined || value === null) return '--'
  return value.toFixed(2)
}

// ---- EditableTextCell ----

export function EditableTextCell({
  value,
  detailGuid,
  field,
  onSave,
  display,
  style,
}: {
  value?: string
  detailGuid: string
  field: InvoiceDetailInlineEditableField
  onSave: InlineCellSaveHandler
  display?: ReactNode
  style?: CSSProperties
}) {
  const [editing, setEditing] = useState(false)
  const [inputValue, setInputValue] = useState(value ?? '')

  useEffect(() => {
    setInputValue(value ?? '')
  }, [value])

  const commit = () => {
    onSave(detailGuid, field, inputValue)
    setEditing(false)
  }

  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') {
      event.preventDefault()
      commit()
    }
    if (event.key === 'Escape') {
      setInputValue(value ?? '')
      setEditing(false)
    }
  }

  if (editing) {
    return (
      <Input
        autoFocus
        size="small"
        value={inputValue}
        onChange={(event) => setInputValue(event.target.value)}
        onBlur={commit}
        onKeyDown={handleKeyDown}
      />
    )
  }

  return (
    <span style={{ ...style, cursor: 'pointer' }} onDoubleClick={() => setEditing(true)}>
      {display ?? value ?? '--'}
    </span>
  )
}

// ---- EditableNumberCell ----

export function EditableNumberCell({
  value,
  detailGuid,
  field,
  onSave,
  displayValue,
  style,
  min = 0,
  max,
  precision = 2,
  addonAfter,
}: {
  value?: number | null
  detailGuid: string
  field: InvoiceDetailInlineEditableField
  onSave: InlineCellSaveHandler
  displayValue?: ReactNode
  style?: CSSProperties
  min?: number
  max?: number
  precision?: number
  addonAfter?: ReactNode
}) {
  const [editing, setEditing] = useState(false)
  const [inputValue, setInputValue] = useState<number | null>(value ?? null)

  useEffect(() => {
    setInputValue(value ?? null)
  }, [value])

  const commit = () => {
    if (inputValue == null) {
      setEditing(false)
      return
    }
    onSave(detailGuid, field, inputValue)
    setEditing(false)
  }

  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') {
      event.preventDefault()
      commit()
    }
    if (event.key === 'Escape') {
      setInputValue(value ?? null)
      setEditing(false)
    }
  }

  if (editing) {
    return (
      <InputNumber
        autoFocus
        size="small"
        min={min}
        max={max}
        precision={precision}
        addonAfter={addonAfter}
        value={inputValue}
        onChange={(nextValue) => setInputValue(nextValue)}
        onBlur={commit}
        onKeyDown={handleKeyDown}
        style={{ width: addonAfter ? 110 : 90 }}
      />
    )
  }

  return (
    <span style={{ ...style, cursor: 'pointer' }} onDoubleClick={() => setEditing(true)}>
      {displayValue ?? formatAmount(value ?? undefined)}
    </span>
  )
}

// ---- EditableBooleanCell ----

export function EditableBooleanCell({
  value,
  detailGuid,
  field,
  onSave,
  trueLabel,
  falseLabel,
  trueColor,
}: {
  value?: boolean | null
  detailGuid: string
  field: InvoiceDetailInlineEditableField
  onSave: InlineCellSaveHandler
  trueLabel: string
  falseLabel: string
  trueColor: string
}) {
  const actualValue = Boolean(value)

  return (
    <Tooltip title="双击切换">
      <Tag
        color={actualValue ? trueColor : 'default'}
        style={{ cursor: 'pointer' }}
        onDoubleClick={() => onSave(detailGuid, field, !actualValue)}
      >
        {actualValue ? trueLabel : falseLabel}
      </Tag>
    </Tooltip>
  )
}
