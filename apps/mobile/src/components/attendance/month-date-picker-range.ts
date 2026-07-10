function monthKey(date: Date) {
  return date.getFullYear() * 12 + date.getMonth();
}

function parseMonthKey(value?: string) {
  const match = value?.match(/^(\d{4})-(\d{2})-\d{2}$/);
  return match ? Number(match[1]) * 12 + Number(match[2]) - 1 : undefined;
}

export function isMonthDateInRange(value: string, minDate?: string, maxDate?: string) {
  return (!minDate || value >= minDate) && (!maxDate || value <= maxDate);
}

export function canNavigateToMonth(month: Date, minDate?: string, maxDate?: string) {
  const candidate = monthKey(month);
  const minimum = parseMonthKey(minDate);
  const maximum = parseMonthKey(maxDate);
  return (minimum === undefined || candidate >= minimum) && (maximum === undefined || candidate <= maximum);
}
