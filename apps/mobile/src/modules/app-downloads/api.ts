import { apiClient } from "@/shared/api/client";
import { createAppDownloadsApi } from "./api-contract";

// apiClient 的基址已经包含 /api，管理模块仅传相对接口路径。
export const appDownloadsApi = createAppDownloadsApi(apiClient);
export * from "./api-contract";
