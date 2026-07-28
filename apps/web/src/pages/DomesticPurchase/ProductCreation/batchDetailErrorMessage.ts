function getMessage(value: unknown): string | undefined {
  if (!value || typeof value !== 'object' || !('message' in value)) return undefined
  const message = value.message
  return typeof message === 'string' && message.trim() ? message : undefined
}

export function getBatchDetailErrorMessage(error: unknown, fallback: string): string {
  // RequestError.payload 保留服务端原始业务消息，应优先于通用网络错误。
  if (error && typeof error === 'object' && 'payload' in error) {
    const payloadMessage = getMessage(error.payload)
    if (payloadMessage) return payloadMessage
  }

  if (error instanceof Error && error.message.trim()) return error.message
  return getMessage(error) || fallback
}
