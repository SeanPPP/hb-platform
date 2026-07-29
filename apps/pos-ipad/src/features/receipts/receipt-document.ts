export type ReceiptPaper = "58mm" | "80mm";
export type ReceiptLocale = "en" | "zh-CN";
export type ReceiptLine = Readonly<{ kind: "text" | "separator" | "feed"; text: string; align: "left" | "center" | "right"; bold: boolean }>;
export type EscPosDocument = Readonly<{ paper: ReceiptPaper; lines: readonly ReceiptLine[] }>;

type SaleInput = Readonly<{
  locale: ReceiptLocale; paper: ReceiptPaper; store: Readonly<{ brandName: string; storeName: string; address: string; phone: string; abn: string }>;
  orderNumber: string; soldAtIso: string; cashierName: string; deviceCode: string; lines: readonly Readonly<{ name: string; quantity: string; unitPriceCents: number; discountCents: number; totalCents: number }>[];
  subtotalCents: number; discountCents: number; totalCents: number; tenders: readonly Readonly<{ method: "cash" | "card" | "voucher" | "other"; amountCents: number; reference?: string | null }>[];
  cashChangeCents: number | null; isReprint?: boolean; title?: string;
}>;
type DailyInput = Readonly<{ locale: ReceiptLocale; paper: ReceiptPaper; storeName: string; businessDate: string; deviceCode: string; cashierName: string; paymentTotals: readonly Readonly<{ method: string; salesCents: number; refundCents: number; netCents: number }>[]; orderCount: number; salesCents: number; refundCents: number; netCents: number; expectedCashCents: number; countedCashCents: number; differenceCents: number }>;
type BankInput = Readonly<{ locale: ReceiptLocale; paper: ReceiptPaper; status: string; cardType?: string; maskedCardNumber?: string; reference?: string; rawText?: string }>;

export function buildSaleReceiptDocument(input: SaleInput): EscPosDocument {
  const width = lineWidth(input.paper); const zh = input.locale === "zh-CN"; const b = new Builder(input.paper, width);
  b.text(input.store.brandName, "center", true); if (input.store.storeName) b.text(input.store.storeName, "center"); b.wrap(input.store.address, "center");
  if (input.store.phone) b.text(`Tel: ${input.store.phone}`, "center"); if (input.store.abn) b.text(`ABN: ${input.store.abn}`, "center"); b.blank();
  b.text(input.title ?? (zh ? "税务发票" : "TAX INVOICE"), "center", true); if (input.isReprint) b.text(zh ? "*** 重打 ***" : "*** REPRINT ***", "center", true); b.blank();
  b.text(`${zh ? "单号" : "Order"}: ${input.orderNumber}`); b.text(`${zh ? "时间" : "Date"}: ${input.soldAtIso.replace("T", " ").slice(0, 19)}`); b.text(`${zh ? "收银员" : "Cashier"}: ${input.cashierName}`); b.separator(); b.text(columns([zh ? "商品" : "ITEM", zh ? "数量" : "QTY", zh ? "金额" : "PRICE"], [8, 10, width - 18], width)); b.separator();
  for (const line of input.lines) { b.wrap(line.name); b.text(columns([line.quantity, money(line.unitPriceCents), money(line.totalCents)], [8, 10, width - 18], width)); if (line.discountCents !== 0) b.two(zh ? "折扣" : "Discount", money(-Math.abs(line.discountCents))); }
  b.separator(); b.two(zh ? "小计" : "Subtotal", money(input.subtotalCents)); if (input.discountCents !== 0) b.two(zh ? "折扣" : "Discount", money(-Math.abs(input.discountCents))); b.two("GST", money(Math.round(input.totalCents / 11))); b.two(zh ? "总计(含GST)" : "Total(inc GST)", money(input.totalCents), true); b.separator();
  for (const tender of input.tenders) { b.two(tenderLabel(tender.method, zh), money(tender.amountCents)); if (tender.reference) b.text(`  Ref: ${maskReference(tender.reference)}`); }
  if (input.cashChangeCents !== null) b.two(zh ? "找零" : "Change", money(input.cashChangeCents), true);
  b.separator(); b.text(`${zh ? "设备" : "Device"}: ${input.deviceCode}`); b.text(zh ? "感谢惠顾" : "Thank you for your purchase!", "center", true); b.feed();
  return b.build();
}

export function buildDailyCloseDocument(input: DailyInput): EscPosDocument {
  const width = lineWidth(input.paper); const b = new Builder(input.paper, width); b.text(input.storeName, "center", true); b.text(input.locale === "zh-CN" ? "日结" : "DAILY CLOSE", "center", true); b.text(`${input.locale === "zh-CN" ? "日期" : "Date"}: ${input.businessDate}`); b.text(`Terminal: ${input.deviceCode}`); b.text(`Cashier: ${input.cashierName}`); b.separator();
  for (const p of input.paymentTotals) b.text(columns([p.method, money(p.salesCents), money(p.refundCents), money(p.netCents)], [Math.max(8, width - 27), 9, 9, 9], width));
  b.separator(); b.two("Orders", String(input.orderCount)); b.two("Sales Amount", money(input.salesCents)); b.two("Refund Amount", money(input.refundCents)); b.two("Net Amount", money(input.netCents), true); b.separator(); b.two("Cash Expected", money(input.expectedCashCents)); b.two("Cash Counted", money(input.countedCashCents)); b.two("Cash Difference", money(input.differenceCents), true); b.feed(); return b.build();
}

export function buildBankReceiptDocument(input: BankInput): EscPosDocument {
  const b = new Builder(input.paper, lineWidth(input.paper)); b.text(input.locale === "zh-CN" ? "银行卡回单" : "CARD RECEIPT", "center", true); b.text(sanitizeBankText(input.status), "center", true); if (input.cardType || input.maskedCardNumber) b.text([input.cardType, maskPan(input.maskedCardNumber ?? "")].filter(Boolean).join(" ")); if (input.reference) b.text(`Ref: ${maskReference(input.reference)}`); for (const raw of (input.rawText ?? "").split(/\r?\n/)) b.wrap(sanitizeBankText(raw)); b.feed(); return b.build();
}

export function documentToEscPosBytes(document: EscPosDocument): Uint8Array {
  const chunks: number[] = [0x1b, 0x40]; const utf8 = new TextEncoder(); for (const line of document.lines) { if (line.kind === "feed") { chunks.push(0x1b, 0x64, 0x03); continue; } if (line.kind === "separator") { chunks.push(...utf8.encode(line.text), 0x0a); continue; } chunks.push(0x1b, 0x61, line.align === "center" ? 1 : line.align === "right" ? 2 : 0, 0x1b, 0x45, line.bold ? 1 : 0, ...utf8.encode(line.text), 0x0a); } chunks.push(0x1d, 0x56, 0x00); return Uint8Array.from(chunks);
}

export function displayWidth(value: string): number { return [...value].reduce((n, c) => n + (isWide(c) ? 2 : 1), 0); }
class Builder { private readonly out: ReceiptLine[] = []; public constructor(private readonly paper: ReceiptPaper, private readonly width: number) {} public text(text: string, align: ReceiptLine["align"] = "left", bold = false): void { for (const line of wrap(text, this.width)) this.out.push({ kind: "text", text: line, align, bold }); } public wrap(text: string, align: ReceiptLine["align"] = "left"): void { this.text(text, align); } public blank(): void { this.out.push({ kind: "text", text: "", align: "left", bold: false }); } public separator(): void { this.out.push({ kind: "separator", text: "-".repeat(this.width), align: "left", bold: false }); } public two(left: string, right: string, bold = false): void { this.text(two(left, right, this.width), "left", bold); } public feed(): void { this.out.push({ kind: "feed", text: "", align: "left", bold: false }); } public build(): EscPosDocument { return { paper: this.paper, lines: this.out }; } }
function lineWidth(paper: ReceiptPaper): number { return paper === "58mm" ? 32 : 48; } function money(cents: number): string { const sign = cents < 0 ? "-" : ""; const v = Math.abs(assertCents(cents)); return `${sign}$${Math.floor(v / 100)}.${String(v % 100).padStart(2, "0")}`; } function assertCents(v: number): number { if (!Number.isSafeInteger(v)) throw new Error("Receipt amounts must be integer cents."); return v; }
function two(left: string, right: string, width: number): string { const r = trim(right, Math.min(14, width - 2)); const l = trim(left, Math.max(1, width - displayWidth(r) - 1)); return l + " ".repeat(Math.max(1, width - displayWidth(l) - displayWidth(r))) + r; } function columns(values: readonly string[], widths: readonly number[], width: number): string { const result = values.map((v, i) => i === 0 ? trim(v, widths[i]!).padEnd(widths[i]!) : trim(v, widths[i]!).padStart(widths[i]!)).join(""); return trim(result, width); }
function wrap(text: string, width: number): readonly string[] { const lines: string[] = []; let current = ""; for (const char of [...(text || "")]) { if (char === "\n") { lines.push(current); current = ""; continue; } if (displayWidth(current) + displayWidth(char) > width) { lines.push(current); current = ""; } current += char; } if (current || lines.length === 0) lines.push(current); return lines; } function trim(value: string, width: number): string { let out = ""; for (const c of [...value]) { if (displayWidth(out) + displayWidth(c) > width) break; out += c; } return out; } function isWide(c: string): boolean { const n = c.codePointAt(0) ?? 0; return n >= 0x1100 && (n <= 0x115f || n >= 0x2e80 || n >= 0x1f300); }
function tenderLabel(method: string, zh: boolean): string { return zh ? ({ cash: "现金", card: "银行卡", voucher: "代金券", other: "其他" }[method] ?? method) : method.toUpperCase(); }
function maskReference(v: string): string { const clean = v.replace(/\s/g, ""); return clean.length <= 4 ? "****" : `****${clean.slice(-4)}`; } function maskPan(v: string): string { const digits = v.replace(/\D/g, ""); return digits.length >= 12 ? `****${digits.slice(-4)}` : maskReference(v); }
function sanitizeBankText(value: string): string { if (/\b(token|voucher|authorization|auth\s*code)\b/i.test(value)) return "[REDACTED]"; const pan = value.replace(/(?<!\d)(?:\d[ .-]*){11,18}\d(?!\d)/g, (m) => maskPan(m)); return /\b(rrn|txn\s*ref|stan|trace|invoice)\b/i.test(pan) ? pan.replace(/(:\s*)(\S+)/, (_m, p) => `${p}${maskReference(String(_m).split(p)[1] ?? "")}`) : pan; }
