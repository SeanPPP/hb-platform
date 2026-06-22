import { readFileSync } from 'node:fs'

function assert(condition: boolean, message: string) {
  if (!condition) {
    throw new Error(message)
  }
}

const pageSource = readFileSync('src/pages/Warehouse/ContainerDetail/index.tsx', 'utf8')

assert(
  pageSource.includes("const containerDetailTabKey = containerGuid ? `/warehouse/container/detail/${containerGuid}` : undefined"),
  '货柜明细 Tab 标题应使用当前货柜 GUID 对应的真实 tab key',
)
assert(
  pageSource.includes('if (!active || !containerDetailTabKey || !containerDetailTabTitle)'),
  '货柜明细 Tab 标题只应由当前 active 的 KeepAlive 实例更新，避免旧实例跟随全局 URL 改错标签',
)
assert(
  pageSource.includes('updateTabTitle(containerDetailTabKey, containerDetailTabTitle)'),
  '货柜明细 Tab 标题应通过精确 tab key 更新',
)
assert(
  !pageSource.includes('useDynamicTabTitle(container?.货柜编号'),
  '货柜明细不应再使用基于全局 location 的动态标题 hook',
)

console.log('containerDetailTabTitle.test: ok')
