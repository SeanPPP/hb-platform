import type { AppUpdateCheckResult } from "./app-update-info";

export type LoginUpdateRestartPromptDependencies = {
  checkAndDownload: () => Promise<AppUpdateCheckResult>;
  warn: (error: unknown) => void;
};

export function shouldShowLoginUpdateRestartPrompt(result: AppUpdateCheckResult): boolean {
  return result.status === "downloaded";
}

export async function checkLoginUpdateRestartPrompt(
  dependencies: LoginUpdateRestartPromptDependencies
): Promise<boolean> {
  try {
    const result = await dependencies.checkAndDownload();
    // 登录页只关心“已下载可重启”，其他 OTA 状态保持静默，避免挡住登录流程。
    return shouldShowLoginUpdateRestartPrompt(result);
  } catch (error) {
    dependencies.warn(error);
    return false;
  }
}
