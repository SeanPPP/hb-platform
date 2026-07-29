export const catalogMaintenanceEnglishCopy = {
  "header.eyebrow": "CATALOG MAINTENANCE",
  "header.title": "Refresh catalog",
  "header.subtitle":
    "Download and verify a replacement snapshot; it activates only after completion.",
  "action.back": "Back",
  "status.panelLabel": "CURRENT STATUS",
  "action.title": "Safe activation",
  "action.copy":
    "Your current catalog remains available during download, validation, or failure.",
  "action.refresh": "Refresh now",
  "action.refreshing": "Downloading and validating…",
  "action.footnote":
    "No catalog reset or API address settings are available here.",
  "status.downloading": "Downloading and validating",
  "status.success": "Catalog updated",
  "status.failed": "Refresh did not complete",
  "status.safeError": "Safe error: {{errorCode}}",
  "status.idle": "Ready to refresh",
  "status.idleHint": "You can download a new verified catalog snapshot.",
  continuity: "The existing catalog remains available.",
  "metric.snapshot": "Snapshot",
  "metric.items": "Items",
} as const;

export type CatalogMaintenanceCopyKey =
  keyof typeof catalogMaintenanceEnglishCopy;

export const catalogMaintenanceChineseCopy = {
  "header.eyebrow": "目录维护",
  "header.title": "手动刷新目录",
  "header.subtitle": "下载并验证替换快照；仅在完整成功后才激活。",
  "action.back": "返回",
  "status.panelLabel": "当前状态",
  "action.title": "安全切换",
  "action.copy": "当前旧目录在下载、校验或失败时都可继续使用。",
  "action.refresh": "立即刷新目录",
  "action.refreshing": "正在下载与校验…",
  "action.footnote": "此页面不提供目录重置或 API 地址设置。",
  "status.downloading": "正在下载与校验",
  "status.success": "目录已更新",
  "status.failed": "刷新未完成",
  "status.safeError": "安全错误码：{{errorCode}}",
  "status.idle": "准备就绪",
  "status.idleHint": "可开始下载新的已验证目录快照。",
  continuity: "旧目录仍可继续使用。",
  "metric.snapshot": "快照",
  "metric.items": "商品数",
} as const satisfies Record<CatalogMaintenanceCopyKey, string>;

const catalogMaintenanceCopy = {
  en: catalogMaintenanceEnglishCopy,
  zh: catalogMaintenanceChineseCopy,
} as const;

export type CatalogMaintenanceLocale = keyof typeof catalogMaintenanceCopy;

export function resolveCatalogMaintenanceLocale(
  language?: string,
): CatalogMaintenanceLocale {
  return language?.toLowerCase().startsWith("zh") ? "zh" : "en";
}

export function catalogMaintenanceText(
  locale: CatalogMaintenanceLocale,
  key: CatalogMaintenanceCopyKey,
  values?: Readonly<Record<string, string | number>>,
): string {
  const template = catalogMaintenanceCopy[locale][key];
  if (!values) return template;
  return template.replace(/\{\{(\w+)\}\}/g, (placeholder, name: string) => {
    const value = values[name];
    return value === undefined ? placeholder : String(value);
  });
}
