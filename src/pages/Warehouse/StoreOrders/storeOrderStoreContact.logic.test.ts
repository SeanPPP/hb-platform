import { resolveStoreContactDraftValue } from './storeOrderStoreContact'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function runTest(name: string, execute: () => void) {
  execute()
  console.log(`ok - ${name}`)
}

runTest('当前地址为空时切换分店应自动带入新分店默认地址', () => {
  assertEqual(
    resolveStoreContactDraftValue({
      currentValue: '',
      previousStoreValue: 'Old Address',
      nextStoreValue: 'New Address',
    }),
    'New Address',
    '空地址应自动切换为新分店默认值',
  )
})

runTest('当前邮箱仍等于上一个分店默认值时切换分店应自动覆盖', () => {
  assertEqual(
    resolveStoreContactDraftValue({
      currentValue: 'old@store.com',
      previousStoreValue: 'old@store.com',
      nextStoreValue: 'new@store.com',
    }),
    'new@store.com',
    '仍是旧分店默认邮箱时应替换为新分店默认邮箱',
  )
})

runTest('当前值是用户自定义内容时切换分店不应覆盖', () => {
  assertEqual(
    resolveStoreContactDraftValue({
      currentValue: 'custom@example.com',
      previousStoreValue: 'old@store.com',
      nextStoreValue: 'new@store.com',
    }),
    'custom@example.com',
    '用户自定义邮箱应保留',
  )
})

runTest('上一个分店默认值为空时也应保留用户自定义内容', () => {
  assertEqual(
    resolveStoreContactDraftValue({
      currentValue: '18 Test Road',
      previousStoreValue: '',
      nextStoreValue: '22 New Road',
    }),
    '18 Test Road',
    '只有空值或等于旧默认值时才应自动替换',
  )
})

console.log('storeOrderStoreContact.logic.test: ok')
