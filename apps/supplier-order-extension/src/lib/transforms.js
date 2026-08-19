// 仅允许与后端契约一致的声明式 transform：trim/uppercase/lowercase。
// 严禁 eval/Function/远程 JS 执行。
export const ALLOWED_TRANSFORMS = new Set(['trim', 'uppercase', 'lowercase']);

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
