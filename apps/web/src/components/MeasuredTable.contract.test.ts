import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function collectSourceFiles(directory: string): string[] {
  return readdirSync(directory).flatMap((name) => {
    const path = join(directory, name)
    const stat = statSync(path)
    if (stat.isDirectory()) {
      return collectSourceFiles(path)
    }
    return /\.(?:ts|tsx)$/.test(name) ? [path] : []
  })
}

function importsAntdTable(source: string) {
  const namedImports = source.match(/import\s+(?:type\s+)?\{[\s\S]*?\}\s+from\s+['"]antd['"]/g) ?? []
  const hasNamedTable = namedImports.some((statement) => {
    const names = statement.slice(statement.indexOf('{') + 1, statement.lastIndexOf('}'))
    return names.split(',').some((name) => /^Table(?:\s+as\s+\w+)?$/.test(name.trim()))
  })
  return hasNamedTable || /import\s+\w+\s+from\s+['"]antd\/(?:es\/)?table['"]/.test(source)
}

const sourceRoot = join(process.cwd(), 'src')
const directImports = collectSourceFiles(sourceRoot)
  .filter((path) => !path.endsWith('MeasuredTable.tsx'))
  .filter((path) => importsAntdTable(readFileSync(path, 'utf8')))
  .map((path) => relative(process.cwd(), path))

assert(
  directImports.length === 0,
  `禁止从 antd 直接导入 Table，仍有 ${directImports.length} 个文件：${directImports.slice(0, 8).join(', ')}`,
)

const measuredTablePath = join(sourceRoot, 'components/MeasuredTable.tsx')
assert(existsSync(measuredTablePath), '必须提供统一 MeasuredTable 组件')
const measuredTableSource = readFileSync(measuredTablePath, 'utf8')

assert(/metricId:\s*string/.test(measuredTableSource), 'MeasuredTable 必须要求非可选 metricId')
assert(
  measuredTableSource.includes('<Profiler') && measuredTableSource.includes('onRender='),
  'MeasuredTable 必须通过 React Profiler 记录 commit 用时',
)
assert(
  /requestAnimationFrame\([\s\S]*requestAnimationFrame\(/.test(measuredTableSource),
  'MeasuredTable 数据更新必须等待双 requestAnimationFrame 后记录',
)
assert(
  measuredTableSource.includes('WEB_TABLE_REACT_COMMIT_METRIC') &&
    measuredTableSource.includes('WEB_TABLE_RENDER_TO_PAINT_METRIC'),
  'MeasuredTable 必须上报两个固定白名单指标',
)
assert(
  !measuredTableSource.includes('window.location.pathname') &&
    !measuredTableSource.includes('currentRoute') &&
    !measuredTableSource.includes('route:'),
  'MeasuredTable 只能发送稳定 metricId/outcome，不得读取或上报动态 route',
)

console.log('MeasuredTable contract tests: ok')
