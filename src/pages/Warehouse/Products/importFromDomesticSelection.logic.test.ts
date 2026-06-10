import {
  mergeCurrentPageSelection,
  removeCurrentPageSelection,
} from './ImportFromDomesticModal'

function assertEqualArray(actual: string[], expected: string[], message: string) {
  if (actual.length !== expected.length || actual.some((item, index) => item !== expected[index])) {
    throw new Error(`${message}。Expected: ${expected.join(',')}; received: ${actual.join(',')}`)
  }
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

  const mergeFailure = await runTest('全选当前页会合并当前页商品且保留其他页选择', () => {
    const actual = mergeCurrentPageSelection(['A001', 'B002'], [
      { productCode: 'B002' },
      { productCode: 'C003' },
    ])

    assertEqualArray(actual.map(String), ['A001', 'B002', 'C003'], '全选当前页应合并去重')
  })
  if (mergeFailure) failures.push(mergeFailure)

  const removeFailure = await runTest('取消当前页选择只移除当前页商品', () => {
    const actual = removeCurrentPageSelection(['A001', 'B002', 'C003'], [
      { productCode: 'B002' },
      { productCode: 'C003' },
    ])

    assertEqualArray(actual.map(String), ['A001'], '取消当前页选择不应影响其他页')
  })
  if (removeFailure) failures.push(removeFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('importFromDomesticSelection.logic.test: ok')
}

await main()
