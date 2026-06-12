export function calculateAge(
  birthday?: string | null,
  referenceDate = new Date(),
): number | null {
  if (!birthday) {
    return null;
  }

  const parsed = new Date(birthday);
  if (Number.isNaN(parsed.getTime())) {
    return null;
  }

  let age = referenceDate.getFullYear() - parsed.getFullYear();
  const birthdayPassed =
    referenceDate.getMonth() > parsed.getMonth() ||
    (referenceDate.getMonth() === parsed.getMonth() &&
      referenceDate.getDate() >= parsed.getDate());

  if (!birthdayPassed) {
    age -= 1;
  }

  return age >= 0 ? age : null;
}

export function maskTrailingFour(
  value?: string | null,
  emptyValue = "--",
): string {
  const normalized = value?.trim();
  if (!normalized) {
    return emptyValue;
  }

  if (normalized.length <= 4) {
    return "*".repeat(normalized.length);
  }

  return `${normalized.slice(0, -4)}****`;
}
