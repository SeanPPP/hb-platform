// Safari 18.4 以前的 storage.onChanged 可能缺失 areaName；仅对目标键做兼容放行。
export function matchesStorageArea(areaName, expectedArea) {
  return areaName === undefined || areaName === expectedArea;
}

export function getPendingLocateChange(changes, areaName) {
  if (!matchesStorageArea(areaName, 'session')) return null;
  return changes?.pendingLocate?.newValue ?? null;
}
