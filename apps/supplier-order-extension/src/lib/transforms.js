// 安全边界：仅允许固定的声明式 transform，严禁 eval/Function、任意正则或远程 JS 执行。
export const ALLOWED_TRANSFORMS = new Set([
  'trim',
  'uppercase',
  'lowercase',
  'after-colon',
  'underscore-to-slash',
  'after-sku',
]);

export function isTransformAllowed(type) {
  return ALLOWED_TRANSFORMS.has(type);
}

export function normalizeTransform(transform) {
  return typeof transform === 'string' ? { type: transform } : transform;
}

export function applyTransform(value, transform) {
  const normalized = normalizeTransform(transform);
  const t = normalized && normalized.type;
  if (!isTransformAllowed(t)) {
    throw new Error(`unsupported transform: ${String(t)}`);
  }
  const s = value == null ? '' : String(value);
  switch (t) {
    case 'trim':
      return s.trim();
    case 'uppercase':
      return s.toUpperCase();
    case 'lowercase':
      return s.toLowerCase();
    case 'after-colon': {
      const colonIndex = s.indexOf(':');
      return colonIndex === -1 ? '' : s.slice(colonIndex + 1).trim();
    }
    case 'underscore-to-slash':
      return s.replaceAll('_', '/');
    case 'after-sku': {
      // TXK 页面固定显示为 “- SKU 货号”；不接受后台下发任意正则。
      const match = /^\s*-?\s*SKU\s+(.+)$/i.exec(s);
      return match ? match[1].trim() : '';
    }
    default:
      throw new Error(`unsupported transform: ${String(t)}`);
  }
}

export function applyTransforms(value, transforms) {
  let out = value;
  for (const t of transforms || []) {
    out = applyTransform(out, t);
  }
  return out;
}

export function safeTransformList(transforms) {
  if (transforms == null) return true;
  if (!Array.isArray(transforms)) return false;
  return transforms.every((transform) => {
    const normalized = normalizeTransform(transform);
    return !!normalized && isTransformAllowed(normalized.type);
  });
}
