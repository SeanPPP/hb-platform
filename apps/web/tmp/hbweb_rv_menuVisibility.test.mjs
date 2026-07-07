// src/router/menuVisibility.ts
function shouldIncludeLocalMenuRoute({
  hasRouteChildren,
  hasVisibleChildren,
  hasSelfAccess
}) {
  if (hasRouteChildren) {
    return hasVisibleChildren;
  }
  return hasSelfAccess;
}

// src/router/menuVisibility.test.ts
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
assertEqual(
  shouldIncludeLocalMenuRoute({
    hasRouteChildren: true,
    hasVisibleChildren: false,
    hasSelfAccess: true
  }),
  false,
  "Parent menu routes should be hidden when all children are hidden"
);
assertEqual(
  shouldIncludeLocalMenuRoute({
    hasRouteChildren: true,
    hasVisibleChildren: true,
    hasSelfAccess: false
  }),
  true,
  "Parent menu routes should be shown when at least one child is visible"
);
assertEqual(
  shouldIncludeLocalMenuRoute({
    hasRouteChildren: false,
    hasVisibleChildren: false,
    hasSelfAccess: true
  }),
  true,
  "Leaf menu routes should still follow their own access"
);
console.log("menuVisibility.test: ok");
