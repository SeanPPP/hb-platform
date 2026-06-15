const CLIENT_PUBLIC_IP_HEADER = 'X-Client-Public-IP'
const CACHE_KEY = 'hbweb:client-public-ipv4'
const CACHE_TTL_MS = 5 * 60 * 1000
const PUBLIC_IP_ENDPOINTS = [
  'https://api.ipify.org?format=json',
  'https://checkip.amazonaws.com',
]

type CachedPublicIp = {
  ip: string
  expiresAt: number
}

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

async function fetchWithTimeout(url: string) {
  const controller = new AbortController()
  const timeoutId = window.setTimeout(() => controller.abort(), 1500)
  try {
    return await fetch(url, {
      cache: 'no-store',
      signal: controller.signal,
    })
  } finally {
    window.clearTimeout(timeoutId)
  }
}

async function resolveClientPublicIpv4() {
  if (typeof window === 'undefined') {
    return undefined
  }

  const cachedIp = readCachedPublicIp()
  if (cachedIp) {
    return cachedIp
  }

  for (const endpoint of PUBLIC_IP_ENDPOINTS) {
    try {
      const response = await fetchWithTimeout(endpoint)
      if (!response.ok) {
        continue
      }

      const text = await response.text()
      const parsedIp = text.trim().startsWith('{')
        ? (JSON.parse(text) as { ip?: string }).ip
        : text.trim()
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

export async function getClientPublicIpHeaders(): Promise<Record<string, string>> {
  const ip = await resolveClientPublicIpv4()
  return ip ? { [CLIENT_PUBLIC_IP_HEADER]: ip } : {}
}
