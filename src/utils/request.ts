import type { ApiResponse, PagedResult } from '../types/api'

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE' | 'PATCH'
  params?: Record<string, unknown>
  data?: unknown
  headers?: Record<string, string>
  signal?: AbortSignal
}

export class RequestError extends Error {
  status: number
  payload?: unknown

  constructor(message: string, status: number, payload?: unknown) {
    super(message)
    this.name = 'RequestError'
    this.status = status
    this.payload = payload
  }
}

function buildQueryString(params?: Record<string, unknown>) {
  if (!params) {
    return ''
  }

  const searchParams = new URLSearchParams()

  Object.entries(params).forEach(([key, value]) => {
    if (value === undefined || value === null || value === '') {
      return
    }

    if (Array.isArray(value)) {
      value.forEach((item) => {
        if (item !== undefined && item !== null && item !== '') {
          searchParams.append(key, String(item))
        }
      })
      return
    }

    searchParams.append(key, String(value))
  })

  const query = searchParams.toString()
  return query ? `?${query}` : ''
}

async function parseResponse<T>(response: Response): Promise<T> {
  const contentType = response.headers.get('content-type') || ''
  if (contentType.includes('application/json')) {
    return (await response.json()) as T
  }

  return (await response.text()) as T
}

async function request<T>(url: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', params, data, headers, signal } = options
  const response = await fetch(`${url}${buildQueryString(params)}`, {
    method,
    credentials: 'include',
    headers: {
      ...(data ? { 'Content-Type': 'application/json' } : {}),
      ...headers,
    },
    body: data ? JSON.stringify(data) : undefined,
    signal,
  })

  const payload = await parseResponse<unknown>(response)

  if (!response.ok) {
    const message =
      typeof payload === 'object' &&
      payload !== null &&
      'message' in payload &&
      typeof payload.message === 'string'
        ? payload.message
        : `请求失败 (${response.status})`
    throw new RequestError(message, response.status, payload)
  }

  return payload as T
}

export function unwrapApiData<T>(payload: ApiResponse<T> | T): T {
  if (payload && typeof payload === 'object' && 'data' in payload) {
    return (payload as ApiResponse<T>).data as T
  }
  return payload as T
}

export function unwrapPagedResult<T>(payload: ApiResponse<PagedResult<T>> | PagedResult<T>) {
  const result = unwrapApiData(payload)
  return {
    items: result.items ?? [],
    total: result.total ?? result.totalCount ?? 0,
    page: result.page ?? result.pageIndex ?? 1,
    pageSize: result.pageSize ?? 10,
    totalPages: result.totalPages,
  }
}

request.get = <T>(url: string, options?: Omit<RequestOptions, 'method'>) =>
  request<T>(url, { ...options, method: 'GET' })
request.post = <T>(url: string, data?: unknown, options?: Omit<RequestOptions, 'method' | 'data'>) =>
  request<T>(url, { ...options, method: 'POST', data })
request.put = <T>(url: string, data?: unknown, options?: Omit<RequestOptions, 'method' | 'data'>) =>
  request<T>(url, { ...options, method: 'PUT', data })
request.delete = <T>(url: string, options?: Omit<RequestOptions, 'method'>) =>
  request<T>(url, { ...options, method: 'DELETE' })

export default request as typeof request & {
  get: typeof request.get
  post: typeof request.post
  put: typeof request.put
  delete: typeof request.delete
}
