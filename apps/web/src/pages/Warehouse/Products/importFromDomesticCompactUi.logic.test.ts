import { readFileSync } from 'node:fs'
import path from 'node:path'

const modalFile = path.resolve(process.cwd(), 'src/pages/Warehouse/Products/ImportFromDomesticModal.tsx')
const compactCssFile = path.resolve(process.cwd(), 'src/pages/Warehouse/Products/compact.css')
const packageFile = path.resolve(process.cwd(), 'package.json')

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function readCssRule(source: string, selector: string) {
  // 统一换行，避免 Windows CRLF 让多行选择器匹配失败。
  const normalizedSource = source.replace(/\r\n/g, '\n')
  const escapedSelector = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const match = normalizedSource.match(new RegExp(`${escapedSelector}\\s*\\{([^}]*)\\}`, 'm'))
  return match?.[1] ?? ''
}

async function runTest(name: string, execute: () => void | Promise<void>): Promise<string | null> {
  try {
    await execute()
    console.log(`ok - ${name}`)
    return null
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error)
    console.error(`not ok - ${name}`)
    console.error(reason)
    return `${name}: ${reason}`
  }
}

async function main() {
  const failures: string[] = []

  const sourceFailure = await runTest('从国内导入弹窗使用局部紧凑表格契约', () => {
    const modalSource = readFileSync(modalFile, 'utf8')

    assert(modalSource.includes("import './compact.css'"), '弹窗应引入 Products 局部 compact.css')
    assert(modalSource.includes('wrapClassName="warehouse-import-domestic-modal"'), 'Modal 应使用局部 wrapClassName')
    assert(modalSource.includes('className="warehouse-import-domestic-compact-table"'), 'Table 应使用局部紧凑 class')
    assert(modalSource.includes('size="small"'), 'Table 应启用 Ant Design small 尺寸')
    assert(modalSource.includes('style={{ width: 260 }}'), '搜索框应收窄到紧凑宽度')
    assert(modalSource.includes('width={36}') && modalSource.includes('height={36}'), '商品图片应压缩到 36px')
    assert(modalSource.includes('columnWidth: 42'), '选择列应收窄但保留可点击空间')
    assert(modalSource.includes('scroll={{ x: 1120, y: 560 }}'), '表格滚动尺寸应匹配紧凑列宽和更高可视行数')
  })
  if (sourceFailure) failures.push(sourceFailure)

  const cssFailure = await runTest('从国内导入紧凑样式只作用于局部弹窗', () => {
    const cssSource = readFileSync(compactCssFile, 'utf8')

    const cellRule = readCssRule(cssSource, '.warehouse-import-domestic-compact-table .ant-table-thead > tr > th,\n.warehouse-import-domestic-compact-table .ant-table-tbody > tr > td')
    const inputRule = readCssRule(cssSource, '.warehouse-import-domestic-compact-table .ant-input-number')
    const twoLineRule = readCssRule(cssSource, '.warehouse-import-domestic-compact-table .warehouse-import-domestic-two-line')
    const tagRule = readCssRule(cssSource, '.warehouse-import-domestic-compact-table .ant-tag')
    const paginationRule = readCssRule(cssSource, '.warehouse-import-domestic-compact-table .ant-pagination')

    assert(cssSource.includes('.warehouse-import-domestic-modal'), '样式必须限定在当前导入弹窗前缀下')
    assert(!/^\s*\.warehouse-import-domestic-two-line\s*\{/m.test(cssSource), '两行文本样式不应使用裸全局选择器')
    assert(cellRule.includes('padding: 3px 6px !important'), '表格单元格应使用紧凑 padding')
    assert(cellRule.includes('line-height: 1.2'), '表格行高应压缩')
    assert(inputRule.includes('width: 100%'), '价格输入框应使用列内自适应宽度')
    assert(inputRule.includes('max-width: 100%'), '价格输入框不应超过列内容区')
    assert(inputRule.includes('min-width: 0'), '价格输入框应允许在紧凑列内收缩')
    assert(inputRule.includes('height: 24px'), '价格输入框高度应压缩')
    assert(twoLineRule.includes('-webkit-line-clamp: 2'), '商品名/供应商应允许两行截断')
    assert(tagRule.includes('line-height: 18px'), '结构标签应压缩高度')
    assert(paginationRule.includes('margin: 6px 0 0'), '分页区应减少顶部间距')
  })
  if (cssFailure) failures.push(cssFailure)

  const packageFailure = await runTest('从国内导入紧凑测试应接入 npm test', () => {
    const packageSource = readFileSync(packageFile, 'utf8')

    assert(packageSource.includes('test:warehouse-products'), 'package.json 应声明仓库商品测试脚本')
    assert(packageSource.includes('importFromDomesticCompactUi.logic.test.ts'), '仓库商品测试脚本应包含紧凑 UI 测试')
    assert(packageSource.includes('importFromDomesticSelection.logic.test.ts'), '仓库商品测试脚本应保留当前页选择逻辑测试')
    assert(packageSource.includes('npm run test:warehouse-products'), 'npm test 应接入仓库商品测试脚本')
  })
  if (packageFailure) failures.push(packageFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('importFromDomesticCompactUi.logic.test: ok')
}

await main()
