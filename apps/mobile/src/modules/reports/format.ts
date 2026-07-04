export type ReportIntent = "positive" | "negative" | "neutral";

export function formatMoney(value: number | null | undefined) {
  const amount = typeof value === "number" && Number.isFinite(value) ? value : 0;
  return `$${amount.toLocaleString("en-AU", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })}`;
}

export function formatSignedMoney(value: number | null | undefined) {
  const amount = typeof value === "number" && Number.isFinite(value) ? value : 0;
  const sign = amount > 0 ? "+" : amount < 0 ? "-" : "";
  return `${sign}${formatMoney(Math.abs(amount))}`;
}

export function formatRatio(value: number | null | undefined) {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return "--";
  }
  const normalized = value * 100;
  const sign = normalized > 0 ? "+" : "";
  return `${sign}${normalized.toFixed(1)}%`;
}

export function getDeltaIntent(value: number | null | undefined, ratio?: number | null): ReportIntent {
  if (typeof value !== "number" || !Number.isFinite(value) || value === 0) {
    return "neutral";
  }
  return value > 0 ? "positive" : "negative";
}

export function getIntentColor(intent: ReportIntent) {
  if (intent === "positive") {
    return "#0F8A5F";
  }
  if (intent === "negative") {
    return "#D92D20";
  }
  return "#6B7280";
}
