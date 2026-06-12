function normalizeValue(value?: string | null) {
  const trimmed = value?.trim();
  return trimmed ? trimmed : "";
}

export function resolveQrDisplayValue(primary?: string | null, fallback?: string | null) {
  return normalizeValue(primary) || normalizeValue(fallback);
}
