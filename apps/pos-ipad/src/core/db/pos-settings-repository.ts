import type { SqliteConnectionPort } from "@hb/pos-db/core/db/types";

const RECEIPT_PRINTER_KEY = "receipt_printer_v1";
const SENSITIVE_KEY = /token|authorization|voucher|card/i;
const PERIPHERAL_ID = /^[A-Za-z0-9._:-]{1,128}$/;
const PAPER_VALUES = new Set(["58mm", "80mm"]);
const LOCALE_VALUES = new Set(["en", "zh-CN"]);

export type ReceiptPrinterSettings = Readonly<{
  printEnabled: boolean;
  drawerEnabled: boolean;
  peripheralId: string | null;
  paper: "58mm" | "80mm";
  locale: "en" | "zh-CN";
  brandName: string;
  storeName: string;
  address: string;
  phone: string;
  abn: string;
  returnPolicy: string;
  profileStoreCode: string;
}>;

export const DEFAULT_RECEIPT_PRINTER_SETTINGS: ReceiptPrinterSettings = {
  printEnabled: false,
  drawerEnabled: false,
  peripheralId: null,
  paper: "80mm",
  locale: "en",
  brandName: "",
  storeName: "",
  address: "",
  phone: "",
  abn: "",
  returnPolicy: "",
  profileStoreCode: "",
};

type SettingsRow = Readonly<{ setting_value: unknown }>;

/**
 * 仅管理 receipt_printer_v1；任何损坏或未知字段都回退到禁用打印/钱箱的安全默认值。
 * 设备票据、支付引用等敏感数据不得借 app_settings 成为普通 JSON 配置。
 */
export class PosSettingsRepository {
  public constructor(
    private readonly db: SqliteConnectionPort,
    private readonly nowIso: () => string,
  ) {}

  public async getReceiptPrinterSettings(): Promise<ReceiptPrinterSettings> {
    const row = await this.db.getFirst<SettingsRow>(
      "SELECT setting_value FROM app_settings WHERE setting_key = ?",
      [RECEIPT_PRINTER_KEY],
    );
    if (!row) return DEFAULT_RECEIPT_PRINTER_SETTINGS;
    try {
      return parseReceiptPrinterSettings(row.setting_value);
    } catch {
      // 配置损坏时绝不以旧值猜测开钱箱或打印，必须由设置页重新明确保存。
      return DEFAULT_RECEIPT_PRINTER_SETTINGS;
    }
  }

  public async saveReceiptPrinterSettings(
    input: ReceiptPrinterSettings,
  ): Promise<ReceiptPrinterSettings> {
    const settings = validateReceiptPrinterSettings(input);
    const payload = JSON.stringify(settings);
    await this.db.withExclusiveTransaction(async (transaction) => {
      await transaction.run(
        `INSERT INTO app_settings (setting_key, setting_value, updated_at_iso)
         VALUES (?, ?, ?)
         ON CONFLICT(setting_key) DO UPDATE SET
           setting_value = excluded.setting_value,
           updated_at_iso = excluded.updated_at_iso`,
        [RECEIPT_PRINTER_KEY, payload, this.nowIso()],
      );
    });
    return settings;
  }
}

function parseReceiptPrinterSettings(value: unknown): ReceiptPrinterSettings {
  if (typeof value !== "string") throw new Error("Invalid receipt printer settings JSON.");
  let parsed: unknown;
  try {
    parsed = JSON.parse(value);
  } catch {
    throw new Error("Invalid receipt printer settings JSON.");
  }
  return validateReceiptPrinterSettings(parsed);
}

function validateReceiptPrinterSettings(value: unknown): ReceiptPrinterSettings {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    throw new Error("Receipt printer settings must be an object.");
  }
  const record = value as Record<string, unknown>;
  const allowed = new Set([
    "printEnabled", "drawerEnabled", "peripheralId", "paper", "locale",
    "brandName", "storeName", "address", "phone", "abn",
    "returnPolicy", "profileStoreCode",
  ]);
  for (const key of Object.keys(record)) {
    if (SENSITIVE_KEY.test(key) || !allowed.has(key)) {
      throw new Error("Receipt printer settings contain an unsupported or sensitive field.");
    }
  }

  const peripheralId = nullablePeripheral(record.peripheralId);
  const printEnabled = boolean(record.printEnabled, "printEnabled");
  const drawerEnabled = boolean(record.drawerEnabled, "drawerEnabled");
  if (drawerEnabled && peripheralId === null) {
    throw new Error("Drawer can only be enabled for a valid receipt printer peripheral.");
  }
  return {
    printEnabled,
    drawerEnabled,
    peripheralId,
    paper: enumeration(record.paper, PAPER_VALUES, "paper") as "58mm" | "80mm",
    locale: enumeration(record.locale, LOCALE_VALUES, "locale") as "en" | "zh-CN",
    brandName: boundedText(record.brandName, 120, "brandName"),
    storeName: boundedText(record.storeName, 120, "storeName"),
    address: multilineText(record.address, 240, "address"),
    phone: boundedText(record.phone, 60, "phone"),
    abn: boundedText(record.abn, 32, "abn"),
    returnPolicy: optionalMultilineText(record.returnPolicy, 500, "returnPolicy"),
    profileStoreCode: optionalText(record.profileStoreCode, 128, "profileStoreCode"),
  };
}

function boolean(value: unknown, field: string): boolean {
  if (typeof value !== "boolean") throw new Error(`Receipt printer ${field} must be boolean.`);
  return value;
}

function nullablePeripheral(value: unknown): string | null {
  if (value === null) return null;
  if (typeof value !== "string" || !PERIPHERAL_ID.test(value)) {
    throw new Error("Receipt printer peripheralId is invalid.");
  }
  return value;
}

function enumeration(value: unknown, allowed: ReadonlySet<string>, field: string): string {
  if (typeof value !== "string" || !allowed.has(value)) {
    throw new Error(`Receipt printer ${field} is invalid.`);
  }
  return value;
}

function boundedText(value: unknown, maxLength: number, field: string): string {
  if (typeof value !== "string" || value.length > maxLength || /[\u0000-\u001F\u007F-\u009F]/.test(value)) {
    throw new Error(`Receipt printer ${field} is invalid.`);
  }
  return value;
}

/** 地址与退货政策需要换行排版，仅放行 CR/LF/TAB，其余控制字符（含 C1）仍拒绝。 */
function multilineText(value: unknown, maxLength: number, field: string): string {
  if (
    typeof value !== "string" ||
    value.length > maxLength ||
    /[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F-\u009F]/.test(value)
  ) {
    throw new Error(`Receipt printer ${field} is invalid.`);
  }
  return value;
}

/** 旧 receipt_printer_v1 没有新资料字段，首次读取补默认空值且不清空旧硬件。 */
function optionalText(value: unknown, maxLength: number, field: string): string {
  if (value === undefined || value === null) return "";
  return boundedText(value, maxLength, field);
}

/** 退货政策/地址需要换行排版，仅放行 CR/LF/TAB，其余控制字符仍拒绝。 */
function optionalMultilineText(value: unknown, maxLength: number, field: string): string {
  if (value === undefined || value === null) return "";
  return multilineText(value, maxLength, field);
}
