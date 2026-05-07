import { useRef } from 'react'
import { useLocation } from 'react-router-dom'
import { resolveRoute, type ResolvedRoute } from '../router/routes'

export function useStableRouteContext(): ResolvedRoute | null {
  const location = useLocation()
  const stablePathRef = useRef(location.pathname)
  const routeRef = useRef<ResolvedRoute | null>(resolveRoute(stablePathRef.current))

  return routeRef.current
}
