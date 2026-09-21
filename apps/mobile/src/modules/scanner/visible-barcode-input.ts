const VISIBLE_BARCODE_PATTERN = /^\d{8,18}$/;

function sharedEdgeLengths(left: string, right: string) {
  let prefix = 0;
  while (prefix < left.length && prefix < right.length && left[prefix] === right[prefix]) {
    prefix += 1;
  }

  let suffix = 0;
  const maxSuffix = Math.min(left.length, right.length) - prefix;
  while (
    suffix < maxSuffix &&
    left[left.length - 1 - suffix] === right[right.length - 1 - suffix]
  ) {
    suffix += 1;
  }
  return { prefix, suffix };
}

/**
 * 从可见搜索框的一次 onChangeText 中提取批量写入的纯数字条码。
 * 慢速手输、删除和单字符替换都返回 null，避免输入过程中自动查询。
 */
export function extractVisibleBarcodeInput(previousValue: string, nextValue: string): string | null {
  if (previousValue === nextValue) return null;

  const { prefix, suffix } = sharedEdgeLengths(previousValue, nextValue);
  if (nextValue.length > previousValue.length && prefix + suffix === previousValue.length) {
    const inserted = nextValue.slice(prefix, nextValue.length - suffix);
    return VISIBLE_BARCODE_PATTERN.test(inserted) ? inserted : null;
  }

  // 纯删除即使留下了合法长度数字，也不是一次新的扫码输入。
  if (previousValue.length > nextValue.length && prefix + suffix === nextValue.length) return null;
  if (!VISIBLE_BARCODE_PATTERN.test(nextValue)) return null;

  if (previousValue.length === nextValue.length) {
    let changedCharacters = 0;
    for (let index = 0; index < nextValue.length; index += 1) {
      if (previousValue[index] !== nextValue[index]) changedCharacters += 1;
    }
    if (changedCharacters <= 1) return null;
  }

  return nextValue;
}
