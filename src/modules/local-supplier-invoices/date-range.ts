import type { InvoiceGridFilters } from "./types";

export interface InvoiceDateRangeValue {
  from?: string;
  to?: string;
}

export interface InvoiceDateRangeDisplay {
  kind: "all" | "from-only" | "to-only" | "range";
  text: string;
}

export interface FormatInvoiceDateRangeDisplayOptions {
  allDatesLabel?: string;
  formatFrom?: (date: string) => string;
  formatTo?: (date: string) => string;
}

function trimDate(value?: string) {
  const trimmed = value?.trim();
  return trimmed || undefined;
}

function normalizeRange(range: InvoiceDateRangeValue): InvoiceDateRangeValue {
  const from = trimDate(range.from);
  const to = trimDate(range.to);
  return { from, to };
}

export function clearInvoiceDateRange(): InvoiceDateRangeValue {
  return {};
}

export function formatInvoiceDateRangeDisplay(
  range: InvoiceDateRangeValue,
  options: FormatInvoiceDateRangeDisplayOptions = {}
): InvoiceDateRangeDisplay {
  const { from, to } = normalizeRange(range);

  if (from && to) {
    return { kind: "range", text: `${from} ~ ${to}` };
  }

  if (from) {
    return { kind: "from-only", text: options.formatFrom?.(from) ?? `${from} 起` };
  }

  if (to) {
    return { kind: "to-only", text: options.formatTo?.(to) ?? `截至 ${to}` };
  }

  return { kind: "all", text: options.allDatesLabel ?? "" };
}

export function selectInvoiceDateRange(
  current: InvoiceDateRangeValue,
  selectedDate: string
): InvoiceDateRangeValue {
  const next = trimDate(selectedDate);
  if (!next) {
    return normalizeRange(current);
  }

  const { from, to } = normalizeRange(current);

  if (from && to) {
    return { from: next };
  }

  if (from) {
    return next < from ? { from: next, to: from } : { from, to: next };
  }

  if (to) {
    return next <= to ? { from: next, to } : { from: to, to: next };
  }

  return { from: next };
}

export function toInvoiceOrderDateFilters(
  range: InvoiceDateRangeValue
): Pick<InvoiceGridFilters, "orderDateFrom" | "orderDateTo"> {
  const { from, to } = normalizeRange(range);
  return {
    ...(from ? { orderDateFrom: from } : {}),
    ...(to ? { orderDateTo: to } : {}),
  };
}
