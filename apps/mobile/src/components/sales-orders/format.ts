/** 金额固定澳元两位小数；null 用占位符，不能把缺失显示成 A$0.00。 */
export function formatSalesOrderMoney(value: number | null | undefined) {
  if (value == null || !Number.isFinite(value)) return "—";
  const abs = Math.abs(value).toLocaleString("en-AU", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
  return value < 0 ? `-A$${abs}` : `A$${abs}`;
}

/**
 * OrderTime 是门店本地墙钟时间（库内已核实），按字面拆分显示为 DD/MM HH:mm，不做任何时区转换。
 * 一旦经过 Date 解析再格式化，设备时区会把它平移 10 小时。
 */
export function formatSalesOrderTime(value: string | null | undefined, withYear = false) {
  if (!value) return "—";
  const match = /^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2})/.exec(value);
  if (!match) return value;
  const [, year, month, day, hour, minute] = match;
  return withYear
    ? `${day}/${month}/${year} ${hour}:${minute}`
    : `${day}/${month} ${hour}:${minute}`;
}

/** YYYY-MM-DD → DD/MM，用于筛选 chip 的紧凑区间。 */
export function formatShortDate(value: string) {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
  return match ? `${match[3]}/${match[2]}` : value;
}
