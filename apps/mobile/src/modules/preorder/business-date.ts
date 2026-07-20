function isLeapYear(year: number) {
  return year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
}

function getDaysInMonth(year: number, month: number) {
  if (month === 2) return isLeapYear(year) ? 29 : 28;
  return [4, 6, 9, 11].includes(month) ? 30 : 31;
}

export function formatBrisbaneBusinessDate(value: string | null | undefined) {
  const normalized = value?.trim();
  if (!normalized) return "";

  // 预计到货日是 Brisbane 业务日，直接保留 YYYY-MM-DD，禁止经 Date 转换产生时区偏移。
  const match = /^(\d{4})-(\d{2})-(\d{2})(?:$|T)/.exec(normalized);
  if (!match) return "";
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  if (month < 1 || month > 12 || day < 1 || day > getDaysInMonth(year, month)) return "";
  return `${match[1]}-${match[2]}-${match[3]}`;
}
