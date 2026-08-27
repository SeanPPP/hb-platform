const CONTROL_CHARACTER_PATTERN = /[\u0000-\u001f\u007f]/u;

export class ValidationError extends Error {
  constructor(message) {
    super(message);
    this.name = "ValidationError";
  }
}

export function assertPlainObject(value, path) {
  if (
    value === null ||
    typeof value !== "object" ||
    Array.isArray(value) ||
    Object.getPrototypeOf(value) !== Object.prototype
  ) {
    throw new ValidationError(`${path} 必须是普通 JSON 对象`);
  }
  return value;
}

export function assertExactKeys(value, { required = [], optional = [] }, path) {
  assertPlainObject(value, path);
  const requiredSet = new Set(required);
  const allowed = new Set([...required, ...optional]);

  for (const key of requiredSet) {
    if (!Object.hasOwn(value, key)) {
      throw new ValidationError(`${path}.${key} 为必填字段`);
    }
  }
  for (const key of Object.keys(value)) {
    if (!allowed.has(key)) {
      throw new ValidationError(`${path} 包含未知字段 ${key}`);
    }
  }
}

export function assertSafeString(
  value,
  path,
  { minLength = 1, maxLength = 256, pattern } = {},
) {
  if (typeof value !== "string") {
    throw new ValidationError(`${path} 必须是字符串`);
  }
  if (value.length < minLength || value.length > maxLength) {
    throw new ValidationError(
      `${path} 长度必须介于 ${minLength} 和 ${maxLength} 个字符之间`,
    );
  }
  if (value !== value.trim() || CONTROL_CHARACTER_PATTERN.test(value)) {
    throw new ValidationError(`${path} 不得包含首尾空白或控制字符`);
  }
  if (pattern && !pattern.test(value)) {
    throw new ValidationError(`${path} 格式无效`);
  }
  return value;
}

export function assertEnum(value, allowed, path) {
  if (!allowed.includes(value)) {
    throw new ValidationError(`${path} 只允许 ${allowed.join("、")}`);
  }
  return value;
}

export function assertFiniteNumber(
  value,
  path,
  { min = -Number.MAX_VALUE, max = Number.MAX_VALUE, integer = false } = {},
) {
  if (
    typeof value !== "number" ||
    !Number.isFinite(value) ||
    (integer && !Number.isInteger(value)) ||
    value < min ||
    value > max
  ) {
    const numberType = integer ? "有限整数" : "有限数字";
    throw new ValidationError(`${path} 必须是 ${min} 至 ${max} 之间的${numberType}`);
  }
  return value;
}

export function assertBoolean(value, path) {
  if (typeof value !== "boolean") {
    throw new ValidationError(`${path} 必须是布尔值`);
  }
  return value;
}

export function assertCanonicalUtcTimestamp(value, path) {
  assertSafeString(value, path, {
    minLength: 24,
    maxLength: 24,
    pattern: /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/u,
  });
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime()) || parsed.toISOString() !== value) {
    throw new ValidationError(`${path} 必须是规范 UTC ISO-8601 时间`);
  }
  return parsed;
}

export function assertUuidV4(value, path) {
  return assertSafeString(value, path, {
    minLength: 36,
    maxLength: 36,
    pattern:
      /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu,
  });
}

export function assertCommitSha(value, path) {
  return assertSafeString(value, path, {
    minLength: 40,
    maxLength: 40,
    pattern: /^[0-9a-f]{40}$/iu,
  });
}

export function assertValidDate(value, path) {
  if (!(value instanceof Date) || Number.isNaN(value.getTime())) {
    throw new ValidationError(`${path} 必须是有效 Date`);
  }
  return value;
}
