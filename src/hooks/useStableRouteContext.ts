import { useRef } from 'react'
import { useLocation } from 'react-router-dom'
import { resolveRoute, type ResolvedRoute } from '../router/routes'

export function useStableRouteContext(): ResolvedRoute | null {
  const location = useLocation()
  const pathnameRef = useRef(location.pathname)
  const routeRef = useRef<ResolvedRoute | null>(resolveRoute(pathnameRef.current))

  if (location.pathname !== pathnameRef.current) {
    pathnameRef.current = location.pathname
    routeRef.current = resolveRoute(pathnameRef.current)
  }

  return routeRef.current
}
