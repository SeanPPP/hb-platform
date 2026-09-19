import {
  normalizePromoPosterDefaults,
  parsePosterPageCount,
  parsePosterPdfFileName,
  type PromoPosterDefaults,
  type PromoPosterPdfRequest,
} from '../pages/PosAdmin/StoreProductPrice/promoPosterLogic'
import type { ApiResponse } from '../types/api'
import request, { RequestError, unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/promo-posters'
const API_BASE_URL = (((import.meta as ImportMeta & { env?: ImportMetaEnv }).env?.VITE_API_BASE_URL) || '').trim()

function buildPdfUrl() {
  return `${API_BASE_URL}${API_BASE}/pdf`.replace(/([^:]\/)\/+/g, '$1')
}

export interface PromoPosterPdfResult {
  blob: Blob
  /** Content-Disposition 里的文件名；跨域未暴露时为 null，由调用方兜底。 */
  fileName: string | null
  /** 响应头 X-Poster-Page-Count；跨域未暴露时为 null，由调用方用本地估算兜底。 */
  pageCount: number | null
}

/** 单个商品的海报默认值（门店价、折后价、清仓价、多件价促销、建议英文名）。 */
export async function fetchPromoPosterDefaults(
  storeCode: string,
  productCode: string,
  signal?: AbortSignal,
): Promise<PromoPosterDefaults> {
  const response = await request.get<ApiResponse<unknown>>(`${API_BASE}/defaults`, {
    params: { storeCode, productCode },
    signal,
  })
  const defaults = normalizePromoPosterDefaults(unwrapApiData(response))
  if (!defaults) {
    throw new RequestError('海报默认值格式无效', 200, response)
  }
  return defaults
}

function getJsonErrorMessage(payload: unknown, status: number) {
  if (payload && typeof payload === 'object') {
    const message = (payload as ApiResponse<unknown>).message
    if (typeof message === 'string' && message.trim()) return message
  }
  return `生成海报失败 (${status})`
}

/**
 * 生成海报 PDF。request 封装只解析 JSON / 文本，这里直接用 fetch 拿二进制：
 * 成功为 application/pdf；400/401/403 为 JSON 信封 { success:false, message }（403 可能无响应体）。
 */
export async function createPromoPosterPdf(
  body: PromoPosterPdfRequest,
  signal?: AbortSignal,
): Promise<PromoPosterPdfResult> {
  const response = await fetch(buildPdfUrl(), {
    method: 'POST',
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', Accept: 'application/pdf, application/json' },
    body: JSON.stringify(body),
    signal,
  })
  const contentType = (response.headers.get('content-type') || '').toLowerCase()

  if (contentType.includes('json')) {
    // 即使 HTTP 200，只要回的是 JSON 就不是 PDF，按业务失败处理
    const payload = await response.json().catch(() => null)
    throw new RequestError(getJsonErrorMessage(payload, response.status), response.status, payload)
  }
  if (!response.ok) {
    // 403（Forbid）通常没有响应体；非 JSON 的错误页不直接展示给用户，只带状态码
    throw new RequestError(`生成海报失败 (${response.status})`, response.status)
  }
  if (!contentType.includes('pdf')) {
    throw new RequestError('生成海报失败：响应不是 PDF', response.status)
  }

  return {
    blob: await response.blob(),
    fileName: parsePosterPdfFileName(response.headers.get('content-disposition')),
    pageCount: parsePosterPageCount(response.headers.get('x-poster-page-count')),
  }
}
