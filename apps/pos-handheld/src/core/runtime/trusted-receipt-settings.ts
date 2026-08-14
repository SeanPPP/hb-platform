import type { ReceiptPrinterSettings } from "../db/pos-settings-repository";

export type TrustedReceiptStorePresentation = Readonly<{
  deviceCode: string;
  storeCode: string;
  storeName: string;
  terminalName: string;
}>;

export type TrustedReceiptStorePresentationSource =
  () => Promise<TrustedReceiptStorePresentation>;

const CONTROL_CHARACTERS = /[\u0000-\u001f\u007f-\u009f]/u;
const MAX_STORE_NAME_CHARACTERS = 120;

/**
 * 设备注册展示缓存只在门店 scope 完全一致时覆盖票据设置；读取失败不能阻断打印。
 */
export async function resolveTrustedReceiptPrinterSettings(
  current: ReceiptPrinterSettings,
  expectedStoreCode: string,
  readPresentation?: TrustedReceiptStorePresentationSource,
): Promise<ReceiptPrinterSettings> {
  const storeCode = expectedStoreCode.trim();
  const savedStoreName = safeStoreName(current.storeName);
  let trustedStoreName: string | null = null;

  if (readPresentation) {
    try {
      const presentation = await readPresentation();
      if (presentation.storeCode.trim() === storeCode) {
        trustedStoreName = safeStoreName(presentation.storeName);
      }
    } catch {
      // 中文注释：展示缓存是可选增强，失败时继续使用持久设置或可信门店编码。
    }
  }

  return Object.freeze({
    ...current,
    storeName: trustedStoreName ?? savedStoreName ?? storeCode,
  });
}

function safeStoreName(value: string): string | null {
  const trimmed = value.trim();
  if (!trimmed || CONTROL_CHARACTERS.test(trimmed)) return null;
  return [...trimmed].slice(0, MAX_STORE_NAME_CHARACTERS).join("");
}
