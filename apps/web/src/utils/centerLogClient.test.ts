import {
  buildExternalFetchErrorLog,
  summarizeExternalResponsePayloadForLog,
  summarizeResponsePayloadForLog,
} from './centerLogClient'

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

const externalFetchLog = buildExternalFetchErrorLog({
  url: 'https://cdn.example.com/upload/banner.png?token=secret-token&signature=abc123',
  method: 'PUT',
  statusCode: 403,
  error: new Error('Upload failed: 403'),
  responsePayload: {
    success: false,
    message: 'signature expired',
    code: 'SignatureExpired',
    token: 'should-not-log',
    detail: {
      presignedUrl: 'https://cdn.example.com/upload/banner.png?token=secret-token',
    },
  },
  properties: {
    uploadUrl: 'https://cdn.example.com/upload/banner.png?token=secret-token&signature=abc123',
    authorizationCode: 'auth-code-should-not-log',
    fileUri: 'file:///tmp/banner.png?token=secret-token',
    objectKey: 'upload/banner.png',
  },
})

assertDeepEqual(
  externalFetchLog,
  {
    level: 'Warning',
    sourceType: 'frontend-external-request',
    message: 'Upload failed: 403',
    exceptionType: 'Error',
    exceptionMessage: 'Upload failed: 403',
    stackTrace: externalFetchLog.stackTrace,
    requestPath: '/upload/banner.png',
    requestMethod: 'PUT',
    statusCode: 403,
    properties: {
      uploadUrl: 'https://cdn.example.com/upload/banner.png',
      fileUriTail: 'banner.png',
      objectKey: 'upload/banner.png',
      responsePayload: {
        success: false,
        message: 'signature expired',
        code: 'SignatureExpired',
      },
    },
  },
  '外部 fetch 失败日志只能记录脱敏后的路径与响应摘要',
)

if (externalFetchLog.requestPath?.includes('token=') || externalFetchLog.requestPath?.includes('signature=')) {
  throw new Error('外部 fetch 失败日志不应泄露 URL query')
}

const serializedExternalFetchLog = JSON.stringify(externalFetchLog)
if (serializedExternalFetchLog.includes('secret-token') || serializedExternalFetchLog.includes('auth-code-should-not-log')) {
  throw new Error('外部 fetch 失败日志 properties 不应泄露签名 token 或授权码')
}

assertDeepEqual(
  summarizeExternalResponsePayloadForLog('token=secret-token&raw=full-response-body'),
  {
    type: 'string',
    length: 41,
  },
  '外部字符串响应体只能记录长度摘要，不能直接上报正文',
)

console.log('centerLogClient.test: ok')
