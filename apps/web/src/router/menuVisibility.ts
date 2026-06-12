interface LocalMenuRouteVisibility {
  hasRouteChildren: boolean
  hasVisibleChildren: boolean
  hasSelfAccess: boolean
}

export function shouldIncludeLocalMenuRoute({
  hasRouteChildren,
  hasVisibleChildren,
  hasSelfAccess,
}: LocalMenuRouteVisibility): boolean {
  if (hasRouteChildren) {
    return hasVisibleChildren
  }

  return hasSelfAccess
}
