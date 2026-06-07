import type { ApiResponse } from '../types/api'
import request from '../utils/request'

export async function batchTranslate(texts: string[]): Promise<Record<string, string>> {
  if (!texts.length) {
    return {}
  }

  const response = await request<ApiResponse<{ translations?: Record<string, string> }> | { success?: boolean; isSuccess?: boolean; message?: string; data?: { translations?: Record<string, string> } }>(
    '/api/Translation/batch-translate',
    {
      method: 'POST',
      data: { texts },
    },
  )

  if (response.success === false || response.isSuccess === false) {
    throw new Error(response.message || '翻译失败')
  }

  return response.data?.translations ?? {}
}
