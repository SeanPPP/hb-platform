import assert from 'node:assert/strict'
import { withShopBarcodeRequestTimeout } from './shopBarcodeRequestTimeout'

let completedSignal: AbortSignal | undefined
const completedValue = await withShopBarcodeRequestTimeout(async (signal) => {
  completedSignal = signal
  return 'ok'
}, 10)
assert.equal(completedValue, 'ok', '正常完成的请求应返回原结果')
await new Promise((resolve) => setTimeout(resolve, 20))
assert.equal(completedSignal?.aborted, false, '正常完成后必须清除超时定时器')

let timeoutSignal: AbortSignal | undefined
await assert.rejects(
  withShopBarcodeRequestTimeout(
    (signal) => {
      timeoutSignal = signal
      return new Promise((_resolve, reject) => {
        signal.addEventListener('abort', () => reject(Object.assign(new Error('aborted'), { name: 'AbortError' })))
      })
    },
    5,
  ),
  { name: 'AbortError' },
  '挂起请求超过时限后应被中止',
)
assert.equal(timeoutSignal?.aborted, true, '超时必须触发 AbortSignal')

console.log('shopBarcodeRequestTimeout.test.ts: ok')
