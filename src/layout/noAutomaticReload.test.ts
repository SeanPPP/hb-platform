import { readdirSync, readFileSync, statSync } from 'node:fs'
import path from 'node:path'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function collectSourceFiles(directory: string): string[] {
  const entries = readdirSync(directory)
  const files: string[] = []

  for (const entry of entries) {
    const fullPath = path.join(directory, entry)
    const stat = statSync(fullPath)

    if (stat.isDirectory()) {
      files.push(...collectSourceFiles(fullPath))
      continue
    }

    if (/\.(tsx?|jsx?)$/.test(entry)) {
      files.push(fullPath)
    }
  }

  return files
}

const root = process.cwd()
const pagesDirectory = path.join(root, 'src/pages')
const pageReloadFiles = collectSourceFiles(pagesDirectory).filter((file) =>
  readFileSync(file, 'utf8').includes('window.location.reload'),
)

assert(
  pageReloadFiles.length === 0,
  `页面文件不应自动 hard reload: ${pageReloadFiles.map((file) => path.relative(root, file)).join(', ')}`,
)

const mobileLayoutSource = readFileSync(path.join(root, 'src/layout/MobileLayout.tsx'), 'utf8')
const errorBoundarySource = readFileSync(path.join(root, 'src/components/GlobalErrorBoundary.tsx'), 'utf8')
const containerDetailSource = readFileSync(path.join(root, 'src/pages/Warehouse/ContainerDetail/index.tsx'), 'utf8')

assert(
  mobileLayoutSource.includes('window.location.reload()'),
  '移动端手动刷新按钮应继续保留主动刷新能力',
)
assert(
  errorBoundarySource.includes('window.location.reload()'),
  '错误恢复按钮应继续保留主动刷新能力',
)

const viewportHookMatch = containerDetailSource.match(/function useContainerDetailViewport\(\) \{[\s\S]*?\n\}/)
if (viewportHookMatch) {
  assert(
    !viewportHookMatch[0].includes('loadData'),
    '货柜明细横竖屏监听只能更新视口状态，不应触发 loadData',
  )
}
assert(
  /void loadHeader\(shouldShowInitialLoading\)[\s\S]{0,220}\}, \[active, containerGuid\]\)/.test(containerDetailSource),
  '货柜明细表头首次加载 effect 应只依赖 active 和 containerGuid，避免横竖屏切换重新加载',
)
assert(
  /void loadDetailChunk\(1, 'reset'\)[\s\S]{0,520}\}, \[active, detailQueryKey\]\)/.test(containerDetailSource),
  '货柜明细远程分页加载 effect 应只依赖 active 和 detailQueryKey，避免横竖屏切换重新加载',
)

console.log('noAutomaticReload.test: ok')
