const PUBLIC_APP_PATHS = new Set(['/login', '/privacy/mobile'])

export function isPublicAppPath(pathname: string) {
  const normalizedPath = pathname.length > 1 ? pathname.replace(/\/+$/, '') : pathname
  return PUBLIC_APP_PATHS.has(normalizedPath)
}
