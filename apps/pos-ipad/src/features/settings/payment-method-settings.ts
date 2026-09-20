import type { SqliteConnectionPort } from "@hb/pos-db/core/db/types";

export type PaymentMethodSettings = Readonly<{
  useManualCard: boolean;
  giftCardEnabled: boolean;
}>;

export const DEFAULT_PAYMENT_METHOD_SETTINGS: PaymentMethodSettings = Object.freeze({
  useManualCard: false,
  giftCardEnabled: false,
});

const PAYMENT_METHODS_KEY = "payment_methods_v1";

export function normalizePaymentMethodSettings(value: unknown): PaymentMethodSettings {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    return DEFAULT_PAYMENT_METHOD_SETTINGS;
  }
  const record = value as Record<string, unknown>;
  // 只接受两个公开布尔开关，损坏配置不能意外启用付款入口。
  if (Object.keys(record).some((key) => key !== "useManualCard" && key !== "giftCardEnabled") ||
      typeof record.useManualCard !== "boolean" || typeof record.giftCardEnabled !== "boolean") {
    return DEFAULT_PAYMENT_METHOD_SETTINGS;
  }
  return Object.freeze({ useManualCard: record.useManualCard, giftCardEnabled: record.giftCardEnabled });
}

/** 不存卡号、凭据或交易结果，也不覆盖既有 Square/Linkly 配置。 */
export class PaymentMethodSettingsRepository {
  public constructor(private readonly db: SqliteConnectionPort, private readonly nowIso: () => string) {}

  public async load(): Promise<PaymentMethodSettings> {
    const row = await this.db.getFirst<{ setting_value: unknown }>(
      "SELECT setting_value FROM app_settings WHERE setting_key = ?", [PAYMENT_METHODS_KEY],
    );
    if (!row || typeof row.setting_value !== "string") return DEFAULT_PAYMENT_METHOD_SETTINGS;
    try { return normalizePaymentMethodSettings(JSON.parse(row.setting_value)); }
    catch { return DEFAULT_PAYMENT_METHOD_SETTINGS; }
  }

  public async save(input: PaymentMethodSettings): Promise<PaymentMethodSettings> {
    const settings = normalizePaymentMethodSettings(input);
    if (settings === DEFAULT_PAYMENT_METHOD_SETTINGS) {
      throw new Error("Invalid payment method settings.");
    }
    await this.db.withExclusiveTransaction(async (transaction) => {
      await transaction.run(
        `INSERT INTO app_settings (setting_key, setting_value, updated_at_iso)
         VALUES (?, ?, ?) ON CONFLICT(setting_key) DO UPDATE SET
           setting_value = excluded.setting_value, updated_at_iso = excluded.updated_at_iso`,
        [PAYMENT_METHODS_KEY, JSON.stringify(settings), this.nowIso()],
      );
    });
    return settings;
  }
}
