import { Platform } from "react-native";
import { apiClient } from "@/shared/api/client";

const API_BASE = "/react/v1/posm-sales-orders";
const PDF_MIME = "application/pdf";
const PDF_UTI = "com.adobe.pdf";

export type InvoiceAction = "preview" | "share";

/** 文件名只保留 GUID 里的安全字符，避免路径注入或系统不接受的文件名。 */
export function buildTaxInvoiceFileName(orderGuid: string) {
  const safe = orderGuid.replace(/[^0-9a-zA-Z-]/g, "").slice(0, 64) || "order";
  return `TaxInvoice_${safe}.pdf`;
}

async function toBase64(data: unknown) {
  const { fromByteArray } = await import("base64-js");
  if (data instanceof ArrayBuffer) return fromByteArray(new Uint8Array(data));
  if (ArrayBuffer.isView(data)) {
    return fromByteArray(new Uint8Array(data.buffer, data.byteOffset, data.byteLength));
  }
  if (typeof Blob !== "undefined" && data instanceof Blob) {
    return fromByteArray(new Uint8Array(await data.arrayBuffer()));
  }
  return typeof data === "string" ? data : "";
}

/** 下载发票 PDF 到应用文档目录，返回本地 file URI。带登录态走 apiClient，不能用裸 fetch。 */
export async function downloadTaxInvoice(orderGuid: string, signal?: AbortSignal) {
  const response = await apiClient.get(
    `${API_BASE}/tax-invoice/${encodeURIComponent(orderGuid)}`,
    { responseType: "arraybuffer", signal },
  );
  const base64 = await toBase64(response.data);
  if (!base64) {
    throw Object.assign(new Error("Tax invoice is empty"), { code: "SALES_ORDER_INVOICE_EMPTY" });
  }
  const FileSystem = await import("expo-file-system/legacy");
  const fileName = buildTaxInvoiceFileName(orderGuid);
  const fileUri = `${FileSystem.documentDirectory ?? ""}${fileName}`;
  await FileSystem.writeAsStringAsync(fileUri, base64, {
    encoding: FileSystem.EncodingType.Base64,
  });
  return { fileUri, fileName };
}

/**
 * 预览：Android 交给系统 PDF 查看器；iOS 没有独立查看器，走系统分享面板（含快速查看）。
 * 分享 / 保存：两端都走系统分享面板，用户可存到"文件"或转发。
 */
export async function openTaxInvoice(fileUri: string, action: InvoiceAction) {
  if (action === "preview" && Platform.OS === "android") {
    const [FileSystem, IntentLauncher] = await Promise.all([
      import("expo-file-system/legacy"),
      import("expo-intent-launcher"),
    ]);
    const contentUri = await FileSystem.getContentUriAsync(fileUri);
    await IntentLauncher.startActivityAsync("android.intent.action.VIEW", {
      data: contentUri,
      type: PDF_MIME,
      flags: 1,
    });
    return;
  }
  const Sharing = await import("expo-sharing");
  if (!(await Sharing.isAvailableAsync())) {
    throw Object.assign(new Error("Sharing unavailable"), {
      code: "SALES_ORDER_SHARE_UNAVAILABLE",
    });
  }
  await Sharing.shareAsync(fileUri, { mimeType: PDF_MIME, UTI: PDF_UTI });
}
