import Storage from "expo-sqlite/kv-store";

export type AppLanguage = "zh" | "en";

export const LANGUAGE_PREFERENCE_KEY = "hb.pos.language.v1";
export const SALES_TOOLBAR_ORDER_PREFERENCE_KEY = "hb.pos.sales-toolbar-order.v1";
export const BUTTON_SOUND_PREFERENCE_KEY = "hb.pos.button-sound.v1";
export const SPECIAL_NODE_SOUND_PREFERENCE_KEY =
  "hb.pos.special-node-sound.v1";
/** 旧版本总开关仅用于双开关首次读取时的兼容回退。 */
export const TOUCH_SOUND_PREFERENCE_KEY = "hb.pos.touch-sound.v1";

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

function parseStoredBoolean(value: string | null): boolean | null {
  if (value === "true") return true;
  if (value === "false") return false;
  return null;
}

function readSoundEnabled(preferenceKey: string): boolean {
  try {
    const currentValue = parseStoredBoolean(Storage.getItemSync(preferenceKey));
    if (currentValue !== null) return currentValue;

    const legacyValue = parseStoredBoolean(
      Storage.getItemSync(TOUCH_SOUND_PREFERENCE_KEY),
    );
    return legacyValue ?? true;
  } catch {
    return true;
  }
}

/** 新键缺失或损坏时只读回退旧总开关；新安装和读取异常默认开启。 */
export function readButtonSoundEnabled(): boolean {
  return readSoundEnabled(BUTTON_SOUND_PREFERENCE_KEY);
}

/** 新键缺失或损坏时只读回退旧总开关；新安装和读取异常默认开启。 */
export function readSpecialNodeSoundEnabled(): boolean {
  return readSoundEnabled(SPECIAL_NODE_SOUND_PREFERENCE_KEY);
}

async function saveSoundEnabled(
  preferenceKey: string,
  enabled: boolean,
): Promise<void> {
  try {
    await Storage.setItem(preferenceKey, enabled ? "true" : "false");
  } catch {
    // 偏好持久化失败不应阻断 POS 界面。
  }
}

/** 写入失败不回滚内存会话状态，也不阻断终端当前操作。 */
export async function saveButtonSoundEnabled(enabled: boolean): Promise<void> {
  await saveSoundEnabled(BUTTON_SOUND_PREFERENCE_KEY, enabled);
}

/** 写入失败不回滚内存会话状态，也不阻断终端当前操作。 */
export async function saveSpecialNodeSoundEnabled(
  enabled: boolean,
): Promise<void> {
  await saveSoundEnabled(SPECIAL_NODE_SOUND_PREFERENCE_KEY, enabled);
}
