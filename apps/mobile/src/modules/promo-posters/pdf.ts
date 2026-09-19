import { Platform } from "react-native";
import { generatePromoPosterPdf } from "./api";
import { buildPromoPosterFileName } from "./logic";
import type { PromoPosterPdfRequest } from "./types";

const PDF_MIME = "application/pdf";
const PDF_UTI = "com.adobe.pdf";

export type PromoPosterPdfAction = "preview" | "share";

export interface PromoPosterPdfFile {
  fileUri: string;
  fileName: string;
  /** 后端返回的实际页数；缺失时由调用方按本地规则估算。 */
  pageCount: number | null;
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

/** 调后端生成海报 PDF 并写到应用文档目录，返回本地 file URI。带登录态走 apiClient，不能用裸 fetch。 */
export async function downloadPromoPosterPdf(
  request: PromoPosterPdfRequest,
  signal?: AbortSignal,
): Promise<PromoPosterPdfFile> {
  const { data, pageCount } = await generatePromoPosterPdf(request, signal);
  const base64 = await toBase64(data);
  if (!base64) {
    throw Object.assign(new Error("Poster PDF is empty"), { code: "PROMO_POSTER_PDF_EMPTY" });
  }
  const FileSystem = await import("expo-file-system/legacy");
  const fileName = buildPromoPosterFileName(new Date());
  const fileUri = `${FileSystem.documentDirectory ?? ""}${fileName}`;
  await FileSystem.writeAsStringAsync(fileUri, base64, {
    encoding: FileSystem.EncodingType.Base64,
  });
  return { fileUri, fileName, pageCount };
}

/**
 * 预览：Android 交给系统 PDF 查看器（可直接打印）；iOS 没有独立查看器，走系统分享面板（含打印与快速查看）。
 * 分享 / 保存：两端都走系统分享面板，用户可存到「文件」或转发。
 */
export async function openPromoPosterPdf(fileUri: string, action: PromoPosterPdfAction) {
  if (action === "preview" && Platform.OS === "android") {
    const [FileSystem, IntentLauncher] = await Promise.all([
      import("expo-file-system/legacy"),
      import("expo-intent-launcher"),
    ]);
    try {
      const contentUri = await FileSystem.getContentUriAsync(fileUri);
      await IntentLauncher.startActivityAsync("android.intent.action.VIEW", {
        data: contentUri,
        type: PDF_MIME,
        flags: 1,
      });
      return;
    } catch (error) {
      // 设备没装 PDF 查看器时 VIEW 会抛 ActivityNotFound，退回系统分享面板让店员选其它应用打开。
      console.warn("[promo-posters] 打开 PDF 查看器失败，改用分享面板", error);
    }
  }
  const Sharing = await import("expo-sharing");
  if (!(await Sharing.isAvailableAsync())) {
    throw Object.assign(new Error("Sharing unavailable"), { code: "PROMO_POSTER_SHARE_UNAVAILABLE" });
  }
  await Sharing.shareAsync(fileUri, { mimeType: PDF_MIME, UTI: PDF_UTI });
}
