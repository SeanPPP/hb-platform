import { safeTransformList } from './transforms.js';

export const ALLOWED_SOURCES = new Set(['attribute', 'text']);
export const ALLOWED_MOUNT_POSITIONS = new Set(['beforebegin', 'afterbegin', 'beforeend', 'afterend']);

const TXK_HTTP_PATTERN = /^http:\/\/txkorders\.inzantsales\.com(?<path>\/[^\s]*)$/i;

function isSafeMatchPattern(value, originOnly = false) {
  if (typeof value !== 'string' || value.length === 0 || value.length > 300) return false;
  const match = /^https:\/\/(?:\*\.)?[A-Za-z0-9.-]+(?::\d+)?(?<path>\/[^\s]*)$/.exec(value)
    || TXK_HTTP_PATTERN.exec(value);
  return !!match && (!originOnly || match.groups.path === '/*');
}

function isSafePagePattern(value) {
  return (
    (typeof value === 'string' && value.startsWith('/') && value.length <= 300)
    || isSafeMatchPattern(value)
  );
}

export function originMatchesAny(origins, origin) {
  return (origins || []).some((pattern) => matchUrlPattern(pattern, `${origin}/`));
}

function escapeRegex(value) {
  return value.replace(/[|\\{}()[\]^$+?.]/g, '\\$&');
}

// 只解释 Chrome 风格 https match pattern 或路径 glob，不把后台字符串当 JavaScript/正则执行。
export function matchUrlPattern(pattern, href) {
  if (typeof pattern !== 'string' || !pattern || typeof href !== 'string') return false;
  let target = href;
  let candidate = pattern;
  if (candidate.startsWith('/')) {
    try {
      target = new URL(href).pathname;
    } catch {
      return false;
    }
  }
  const regex = `^${escapeRegex(candidate).replaceAll('*', '.*')}$`;
  return new RegExp(regex, 'i').test(target);
}

export function matchesListPage(listPagePatterns, href) {
  return (listPagePatterns || []).some((pattern) => matchUrlPattern(pattern, href));
}

// 校验 profile 数据，拒绝任何非声明式 transform
export function validateProfiles(raw) {
  if (!raw || typeof raw !== 'object' || !Array.isArray(raw.profiles)) {
    return { valid: false, profiles: [], errors: ['profiles 必须为 {profiles:[...]} 对象'] };
  }
  const errors = [];
  const out = [];
  raw.profiles.forEach((p, i) => {
    const path = `profiles[${i}]`;
    if (!p || typeof p !== 'object') {
      errors.push(`${path} 不是对象`);
      return;
    }
    const errs = [];
    if (typeof p.supplierCode !== 'string' || !p.supplierCode) errs.push('supplierCode 必填');
    if (typeof p.displayName !== 'string' || !p.displayName) errs.push('displayName 必填');
    if (typeof p.enabled !== 'boolean') errs.push('enabled 必须为 boolean');
    if (!Array.isArray(p.origins) || p.origins.length === 0) {
      errs.push('origins 必须为非空数组');
    } else {
      p.origins.forEach((o, j) => {
        if (!isSafeMatchPattern(o, true)) errs.push(`origins[${j}] 非法`);
      });
    }
    if (!Array.isArray(p.listPagePatterns)) {
      errs.push('listPagePatterns 必须为数组');
    } else {
      p.listPagePatterns.forEach((pattern, j) => {
        if (!isSafePagePattern(pattern)) errs.push(`listPagePatterns[${j}] 非法`);
      });
    }
    if (typeof p.cardSelector !== 'string' || !p.cardSelector) errs.push('cardSelector 必填');
    if (!p.itemNumber || typeof p.itemNumber !== 'object') {
      errs.push('itemNumber 必填');
    } else {
      const it = p.itemNumber;
      if (!ALLOWED_SOURCES.has(it.source)) errs.push('itemNumber.source 非法');
      if (it.source === 'attribute' && (typeof it.attribute !== 'string' || !it.attribute)) {
        errs.push('attribute source 需要 attribute');
      }
      if (it.selector != null && typeof it.selector !== 'string') {
        errs.push('itemNumber.selector 必须为字符串或 null');
      }
      if (!safeTransformList(it.transforms)) errs.push('itemNumber.transforms 包含不支持的 transform');
    }
    if (typeof p.mountSelector !== 'string' || !p.mountSelector) errs.push('mountSelector 必填');
    if (!ALLOWED_MOUNT_POSITIONS.has(p.mountPosition)) errs.push('mountPosition 非法');
    if (errs.length) {
      errors.push(...errs.map((e) => `${path}.${e}`));
      return;
    }
    out.push(p);
  });
  return { valid: errors.length === 0, profiles: out, errors };
}

// 按 origin 匹配第一个启用 profile（列表/详情判断交给 shouldInjectList）
export function matchProfile(profiles, { origin, pathname }) {
  for (const p of profiles || []) {
    if (p.enabled === false) continue;
    if (!originMatchesAny(p.origins, origin)) continue;
    return p;
  }
  return null;
}
