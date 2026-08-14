import type { AttendanceAuditPresenter } from "./attendance-audit-presenter";

/**
 * 组合根注入可信设备上下文、Keychain/crypto、加密缓存、可信时间和审计读 Port。
 * 路由只能调用零参数工厂，不能从导航参数伪造门店、设备或权限。
 */
export interface AttendanceAuditRuntimeFactory {
  createPresenter(): AttendanceAuditPresenter;
}

export function resolveAttendanceAuditRuntimeFactory(
  services: object,
): AttendanceAuditRuntimeFactory | null {
  if (!("attendanceAudit" in services)) return null;
  const candidate = services.attendanceAudit;
  if (
    typeof candidate !== "object" ||
    candidate === null ||
    !("createPresenter" in candidate) ||
    typeof candidate.createPresenter !== "function"
  ) {
    return null;
  }
  return candidate as AttendanceAuditRuntimeFactory;
}
