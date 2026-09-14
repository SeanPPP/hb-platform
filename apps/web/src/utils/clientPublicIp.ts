const CLIENT_PUBLIC_IP_HEADER = 'X-Client-Public-IP'
const CACHE_KEY = 'hbweb:client-public-ipv4'
const CACHE_TTL_MS = 5 * 60 * 1000
const FAILURE_COOLDOWN_MS = 30 * 1000
const FETCH_TIMEOUT_MS = 1500
const PUBLIC_IP_ENDPOINTS = [
  'https://api.ipify.org?format=json',
  'https://checkip.amazonaws.com',
]

type CachedPublicIp = {
  ip: string
  expiresAt: number
}

type TimedIpResponse = {
  response: Response
  body: string
}

let publicIpLookup: Promise<string | undefined> | undefined
let lastLookupFailureAt = 0

function isPublicIpv4(value?: string | null) {
  if (!value) {
    return false
  }

  const parts = value.trim().split('.').map((part) => Number(part))
  if (parts.length !== 4 || parts.some((part) => !Number.isInteger(part) || part < 0 || part > 255)) {
    return false
  }

  const [first, second] = parts
  return !(
    first === 10 ||
    first === 127 ||
    first === 0 ||
    first >= 224 ||
    (first === 169 && second === 254) ||
    (first === 172 && second >= 16 && second <= 31) ||
    (first === 192 && second === 168) ||
    (first === 192 && second === 0 && (parts[2] === 0 || parts[2] === 2)) ||
    (first === 192 && second === 88 && parts[2] === 99) ||
    (first === 198 && (second === 18 || second === 19)) ||
    (first === 198 && second === 51 && parts[2] === 100) ||
    (first === 203 && second === 0 && parts[2] === 113) ||
    (first === 100 && second >= 64 && second <= 127)
  )
}

function readCachedPublicIp() {
  try {
    const cached = window.sessionStorage.getItem(CACHE_KEY)
    if (!cached) {
      return undefined
    }

    const parsed = JSON.parse(cached) as CachedPublicIp
    if (parsed.expiresAt > Date.now() && isPublicIpv4(parsed.ip)) {
      return parsed.ip
    }
  } catch {
    return undefined
  }

  return undefined
}

function writeCachedPublicIp(ip: string) {
  try {
    window.sessionStorage.setItem(
      CACHE_KEY,
      JSON.stringify({ ip, expiresAt: Date.now() + CACHE_TTL_MS } satisfies CachedPublicIp),
    )
  } catch {
    // sessionStorage 不可用时跳过缓存，不影响登录。
  }
}

async function fetchWithTimeout(url: string): Promise<TimedIpResponse> {
  const controller = new AbortController()
  let timeoutId: number | undefined
  const timeoutPromise = new Promise<never>((_, reject) => {
    timeoutId = window.setTimeout(() => {
      controller.abort()
      reject(new Error('公网 IP 查询超时'))
    }, FETCH_TIMEOUT_MS)
  })
  const fetchAndRead = (async () => {
    const response = await fetch(url, {
      cache: 'no-store',
      signal: controller.signal,
    })
    // 将 response.text() 放进同一个超时窗口，避免服务端已返回 headers 但 body 挂起时继续阻塞。
    const body = await response.text()
    return { response, body }
  })()
  try {
    return await Promise.race([fetchAndRead, timeoutPromise])
  } finally {
    if (timeoutId !== undefined) {
      window.clearTimeout(timeoutId)
    }
  }
}

async function lookupClientPublicIpv4() {
  for (const endpoint of PUBLIC_IP_ENDPOINTS) {
    try {
      const { response, body } = await fetchWithTimeout(endpoint)
      if (!response.ok) {
        continue
      }

      const parsedIp = body.trim().startsWith('{')
        ? (JSON.parse(body) as { ip?: string }).ip
        : body.trim()
      if (typeof parsedIp === 'string' && isPublicIpv4(parsedIp)) {
        writeCachedPublicIp(parsedIp)
        return parsedIp
      }
    } catch {
      // 单个公网 IP 服务失败时继续尝试下一个。
    }
  }

  return undefined
}

function startBackgroundLookup() {
  if (publicIpLookup) {
    return
  }

  const lookup = lookupClientPublicIpv4()
  publicIpLookup = lookup
  void lookup.then((ip) => {
    if (!ip) {
      lastLookupFailureAt = Date.now()
    }
  }).catch(() => {
    lastLookupFailureAt = Date.now()
  }).finally(() => {
    if (publicIpLookup === lookup) {
      publicIpLookup = undefined
    }
  })
}

async function resolveClientPublicIpv4() {
  if (typeof window === 'undefined') {
    return undefined
  }

  const cachedIp = readCachedPublicIp()
  if (cachedIp) {
    return cachedIp
  }

  if (Date.now() - lastLookupFailureAt < FAILURE_COOLDOWN_MS) {
    return undefined
  }

  // 公网 IP 仅用于审计辅助 header，不应延迟登录或 token refresh。
  startBackgroundLookup()
  return undefined
}

export async function getClientPublicIpHeaders(): Promise<Record<string, string>> {
  const ip = await resolveClientPublicIpv4()
  return ip ? { [CLIENT_PUBLIC_IP_HEADER]: ip } : {}
}
