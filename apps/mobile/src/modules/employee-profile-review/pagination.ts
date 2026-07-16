export function mergeUniqueEmployeeProfileReviewPages<T extends { requestId: number }>(
  pages: ReadonlyArray<{ items: ReadonlyArray<T> }>
) {
  const seen = new Set<number>();
  const merged: T[] = [];
  for (const page of pages) {
    for (const item of page.items) {
      if (seen.has(item.requestId)) {
        continue;
      }
      seen.add(item.requestId);
      merged.push(item);
    }
  }
  return merged;
}
