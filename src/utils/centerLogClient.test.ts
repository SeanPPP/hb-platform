import { summarizeResponsePayloadForLog } from './centerLogClient'

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

assertDeepEqual(
  summarizeResponsePayloadForLog({
    success: false,
    message: '保存失败',
    code: 'VALIDATION_ERROR',
    errorCode: 'PRODUCT_INVALID',
    details: { password: 'secret', token: 'secret-token' },
    data: { customerEmail: 'customer@example.test' },
  }),
  {
    success: false,
    message: '保存失败',
    code: 'VALIDATION_ERROR',
    errorCode: 'PRODUCT_INVALID',
  },
  '日志只应保留业务失败摘要，不能上报完整响应体',
)

assertDeepEqual(
  summarizeResponsePayloadForLog('raw backend error'),
  { message: 'raw backend error' },
  '字符串响应体只应作为 message 摘要上报',
)

console.log('centerLogClient.test: ok')
