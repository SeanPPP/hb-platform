export function selectVisibleSupplierEntries(entries, expanded) {
  const safeEntries = Array.isArray(entries) ? entries : [];
  const grantedCount = safeEntries.filter((entry) => entry?.granted === true).length;

  return {
    visibleEntries: expanded
      ? safeEntries
      : safeEntries.filter((entry) => entry?.granted !== true),
    grantedCount,
    hiddenGrantedCount: expanded ? 0 : grantedCount,
  };
}
