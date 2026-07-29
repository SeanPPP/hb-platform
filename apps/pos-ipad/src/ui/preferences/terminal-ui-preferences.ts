import Storage from "expo-sqlite/kv-store";

export type AppLanguage = "zh" | "en";

export const LANGUAGE_PREFERENCE_KEY = "hb.pos.language.v1";
export const SALES_TOOLBAR_ORDER_PREFERENCE_KEY = "hb.pos.sales-toolbar-order.v1";

function isAppLanguage(value: unknown): value is AppLanguage {
  return value === "zh" || value === "en";
}

function isToolbarActionOrder(value: unknown): value is string[] {
  return Array.isArray(value) && value.every((actionId) => typeof actionId === "string");
}

/** 同步读取仅含语言代码的本地偏好，存储故障时继续使用设备语言。 */
export function readStoredLanguage(): AppLanguage | null {
  try {
    const value = Storage.getItemSync(LANGUAGE_PREFERENCE_KEY);
    return isAppLanguage(value) ? value : null;
  } catch {
    return null;
  }
}

/** 异步保存语言；设备存储不可用不得影响当前会话。 */
export async function saveStoredLanguage(language: AppLanguage): Promise<void> {
  try {
    await Storage.setItem(LANGUAGE_PREFERENCE_KEY, language);
  } catch {
    // 偏好持久化失败不应阻断 POS 界面。
  }
}

/** 同步读取销售工具栏 action ID 排序，损坏数据一律忽略。 */
export function readSalesToolbarOrder(): string[] | null {
  try {
    const serialized = Storage.getItemSync(SALES_TOOLBAR_ORDER_PREFERENCE_KEY);
    if (serialized == null) {
      return null;
    }

    const actionOrder: unknown = JSON.parse(serialized);
    return isToolbarActionOrder(actionOrder) ? actionOrder : null;
  } catch {
    return null;
  }
}

/** 异步保存销售工具栏 action ID 排序，非法数据或存储故障均安全忽略。 */
export async function saveSalesToolbarOrder(
  actionIds: readonly string[],
): Promise<void> {
  if (!isToolbarActionOrder(actionIds)) {
    return;
  }

  try {
    await Storage.setItem(
      SALES_TOOLBAR_ORDER_PREFERENCE_KEY,
      JSON.stringify(actionIds),
    );
  } catch {
    // 偏好持久化失败不应阻断 POS 界面。
  }
}
