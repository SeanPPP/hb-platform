import type { AxiosRequestConfig } from "axios";
import { extractBinaryErrorMessage, normalizePromoPosterDefaults, parseBinaryJsonBody } from "./logic";
import type { PromoPosterDefaults, PromoPosterPdfRequest } from "./types";

const BASE_PATH = "/react/v1/promo-posters";
/** 200 张拼版可能要数十秒，单独放宽超时（默认 30 秒）。 */
const PDF_TIMEOUT_MS = 120_000;

// 动态加载 apiClient / 设备 store，纯逻辑单测不会初始化 React Native 运行时。
async function getApiClient() {
  const { apiClient } = await import("@/shared/api/client");
  return apiClient;
}

/** 与扫码查询页一致的设备/账号双轨：设备会话带设备头，账号会话由拦截器补 Bearer。 */
async function buildRequestConfig(): Promise<AxiosRequestConfig> {
  const { useDeviceStore } = await import("@/store/device-store");
  const session = useDeviceStore.getState().session;
  if (!session?.hardwareId || !session.authCode) {
    return {};
  }
  return {
    headers: {
      "X-Device-Id": session.hardwareId,
      "X-Auth-Code": session.authCode,
    },
  };
}

export async function getPromoPosterDefaults(
  storeCode: string,
  productCode: string,
  signal?: AbortSignal,
): Promise<PromoPosterDefaults> {
  const [apiClient, config] = await Promise.all([getApiClient(), buildRequestConfig()]);
  const response = await apiClient.get(`${BASE_PATH}/defaults`, {
    ...config,
    params: { storeCode, productCode },
    signal,
  });
  const defaults = normalizePromoPosterDefaults(response.data);
  if (!defaults) {
    throw Object.assign(new Error("Promo poster defaults are empty"), { code: "PROMO_POSTER_DEFAULTS_EMPTY" });
  }
  return defaults;
}

/**
 * 生成 PDF：成功返回 PDF 二进制（ArrayBuffer）与后端实际页数（X-Poster-Page-Count，缺失时为 null）。
 * 失败时响应体同样是 ArrayBuffer，需要解码成 JSON 再把 message 放回错误对象，
 * 通用错误提示（resolveLocalizedErrorMessage）才能展示后端给出的原因。
 */
export async function generatePromoPosterPdf(
  request: PromoPosterPdfRequest,
  signal?: AbortSignal,
): Promise<{ data: unknown; pageCount: number | null }> {
  const [apiClient, config] = await Promise.all([getApiClient(), buildRequestConfig()]);
  try {
    const response = await apiClient.post(`${BASE_PATH}/pdf`, request, {
      ...config,
      headers: { ...(config.headers ?? {}), Accept: "application/pdf, application/json" },
      responseType: "arraybuffer",
      timeout: PDF_TIMEOUT_MS,
      signal,
    });
    const rawPageCount = Number(response.headers?.["x-poster-page-count"]);
    return {
      data: response.data,
      pageCount: Number.isInteger(rawPageCount) && rawPageCount > 0 ? rawPageCount : null,
    };
  } catch (error) {
    const response = (error as { response?: { data?: unknown } } | null)?.response;
    if (response && response.data !== undefined) {
      const body = parseBinaryJsonBody(response.data);
      const message = extractBinaryErrorMessage(response.data);
      if (body) response.data = body;
      if (message && error instanceof Error) error.message = message;
    }
    throw error;
  }
}
