import type { PreorderPasteRow } from '../../../types/preorder'

export interface ParsedPreorderPaste {
  rows: PreorderPasteRow[]
  errors: string[]
}

export type PreorderPasteMessageKey = 'missingFields' | 'invalidMoq' | 'moqConflict' | 'empty'
export type PreorderPasteMessageFormatter = (
  key: PreorderPasteMessageKey,
  values?: { lineNumber?: number; itemNumber?: string },
) => string

export interface PreorderPasteEditorState<T> {
  text: string
  items: T[]
  errors: string[]
}

interface PreorderTemplateSaveItem {
  valid: boolean
  productCode?: string | null
}

export function applyPreorderPasteTextChange<T>(
  state: PreorderPasteEditorState<T>,
  nextText: string,
): PreorderPasteEditorState<T> {
  if (state.text === nextText) return state

  // 用户修改原始粘贴内容后，旧解析结果不再具有保存资格。
  return { text: nextText, items: [], errors: [] }
}

export function canSavePreorderTemplate(
  items: readonly PreorderTemplateSaveItem[],
  errors: readonly string[],
) {
  return errors.length === 0
    && items.length > 0
    && items.every((item) => item.valid && Boolean(item.productCode))
}

export function removePreorderPasteItem<T extends { lineNumber: number }>(
  state: Pick<PreorderPasteEditorState<T>, 'items' | 'errors'>,
  lineNumber: number,
  linePrefix: string,
) {
  // 预览行与错误提示共用原始 Excel 行号，移除时必须同步更新，避免残留错误阻止保存。
  return {
    items: state.items.filter((item) => item.lineNumber !== lineNumber),
    errors: state.errors.filter((error) => !error.startsWith(linePrefix)),
  }
}

function isHeader(cells: string[]) {
  const first = (cells[0] || '').toLowerCase()
  const second = (cells[1] || '').toLowerCase()
  return /item|货号/.test(first) && /moq|minimum|最小/.test(second)
}

export function parsePreorderPaste(
  text: string,
  formatMessage: PreorderPasteMessageFormatter,
): ParsedPreorderPaste {
  const rows: PreorderPasteRow[] = []
  const errors: string[] = []
  const seen = new Map<string, number>()

  text.split(/\r?\n/).forEach((rawLine, index) => {
    const lineNumber = index + 1
    const cells = rawLine.split('\t').map((cell) => cell.trim())
    if (!cells.some(Boolean) || (lineNumber === 1 && isHeader(cells))) return

    if (cells.length < 2 || !cells[0]) {
      errors.push(formatMessage('missingFields', { lineNumber }))
      return
    }

    const minimumOrderQuantity = Number(cells[1])
    if (!Number.isInteger(minimumOrderQuantity) || minimumOrderQuantity <= 0) {
      errors.push(formatMessage('invalidMoq', { lineNumber }))
      return
    }

    // 与后端 ItemNumber Ordinal 精确匹配保持一致，大小写不同视为不同货号。
    const key = cells[0]
    const existing = seen.get(key)
    if (existing !== undefined) {
      if (existing !== minimumOrderQuantity) {
        errors.push(formatMessage('moqConflict', { lineNumber, itemNumber: cells[0] }))
      }
      return
    }

    seen.set(key, minimumOrderQuantity)
    rows.push({ lineNumber, itemNumber: cells[0], minimumOrderQuantity })
  })

  if (!rows.length && !errors.length) errors.push(formatMessage('empty'))
  return { rows, errors }
}
