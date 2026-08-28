export const catalogMaintenanceEnglishCopy = {
  "header.eyebrow": "CATALOG MAINTENANCE",
  "header.title": "Refresh catalog",
  "header.subtitle":
    "Download and verify a replacement snapshot; it activates only after completion.",
  "action.back": "Back",
  "status.panelLabel": "LOCAL CATALOG",
  "action.title": "Safe activation",
  "action.copy":
    "Your current catalog remains available during download, validation, or failure.",
  "action.background":
    "You can leave this page; the catalog continues refreshing inside the app, and the same progress is shown when you return.",
  "action.refresh": "Refresh now",
  "action.refreshing": "Refreshing catalog…",
  "action.footnote":
    "No catalog reset or API address settings are available here.",
  "status.catalogLoading": "Reading local catalog",
  "status.catalogUnavailable": "No local catalog",
  "status.catalogMetadataError": "Local catalog information could not be read",
  "status.downloading": "Refreshing catalog",
  "status.success": "Catalog updated",
  "status.warning": "Catalog activated with a follow-up warning",
  "status.failed": "Refresh did not complete",
  "status.safeError": "Safe error: {{errorCode}}",
  "status.idle": "Ready to refresh",
  "status.idleHint": "You can download a new verified catalog snapshot.",
  continuity: "The existing catalog remains available.",
  "metric.version": "Catalog version",
  "metric.snapshot": "Snapshot ID",
  "metric.items": "Items",
  "metric.activated": "Activated",
  "progress.total": "Overall progress: {{percent}}%",
  "progress.elapsed": "Elapsed: {{elapsed}}",
  "progress.preparing": "Waiting for catalog preparation",
  "progress.pages": "{{completed}} / {{total}} pages",
  "progress.currentStep": "Current step: {{step}}",
  "progress.accessibility": "Catalog refresh progress, {{percent}} percent",
  "progress.stepAccessibility": "{{step}}, {{percent}} percent complete",
  "step.prepare": "Prepare catalog",
  "step.products": "Download and verify items",
  "step.promotions": "Sync promotions",
  "step.activate": "Safe activation",
  "warning.runtimeReload":
    "The new catalog product data is active and checkout can continue. The last verified promotion rules remain in use; retry refresh later or contact support.",
  "warning.activationVerification":
    "Catalog activation was committed, but local confirmation did not complete. Do not continue checkout; contact support.",
} as const;

export type CatalogMaintenanceCopyKey =
  keyof typeof catalogMaintenanceEnglishCopy;

export const catalogMaintenanceChineseCopy = {
  "header.eyebrow": "目录维护",
  "header.title": "手动刷新目录",
  "header.subtitle": "下载并验证替换快照；仅在完整成功后才激活。",
  "action.back": "返回",
  "status.panelLabel": "当前本地目录",
  "action.title": "安全切换",
  "action.copy": "当前旧目录在下载、校验或失败时都可继续使用。",
  "action.background":
    "可离开本页，目录会在应用内继续刷新；返回后仍显示相同进度。",
  "action.refresh": "立即刷新目录",
  "action.refreshing": "正在刷新目录…",
  "action.footnote": "此页面不提供目录重置或 API 地址设置。",
  "status.catalogLoading": "正在读取本地目录",
  "status.catalogUnavailable": "暂无本地目录",
  "status.catalogMetadataError": "无法读取本地目录信息",
  "status.downloading": "正在刷新目录",
  "status.success": "目录已更新",
  "status.warning": "目录已激活，但后续载入或确认未完成",
  "status.failed": "刷新未完成",
  "status.safeError": "安全错误码：{{errorCode}}",
  "status.idle": "准备就绪",
  "status.idleHint": "可开始下载新的已验证目录快照。",
  continuity: "旧目录仍可继续使用。",
  "metric.version": "目录版本",
  "metric.snapshot": "快照 ID",
  "metric.items": "商品数",
  "metric.activated": "启用时间",
  "progress.total": "总进度：{{percent}}%",
  "progress.elapsed": "已用时：{{elapsed}}",
  "progress.preparing": "正在等待目录准备",
  "progress.pages": "{{completed}} / {{total}} 页",
  "progress.currentStep": "当前步骤：{{step}}",
  "progress.accessibility": "目录刷新进度，{{percent}}%",
  "progress.stepAccessibility": "{{step}}，已完成 {{percent}}%",
  "step.prepare": "准备目录",
  "step.products": "下载并校验商品",
  "step.promotions": "同步促销",
  "step.activate": "安全激活",
  "warning.runtimeReload":
    "新目录商品数据已启用，可以继续收银。系统会保留上一份已验证促销规则；请稍后重试刷新或联系支持。",
  "warning.activationVerification":
    "目录激活已提交，但本地确认未完成。请勿继续收银；请联系支持。",
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
