import type { ReactNode } from "react";
import { Redirect } from "expo-router";
import { useAuthStore } from "@/store/auth-store";
import { canAccessVersionManagement } from "./version-management-access";

export function VersionManagementGuard({ children }: { children: ReactNode }) {
  const isAdmin = useAuthStore((state) => state.access.isAdmin);
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const sessionKind = useAuthStore((state) => state.sessionKind);
  if (!canAccessVersionManagement({ isAdmin, isAuthenticated, sessionKind })) {
    return <Redirect href="/(shell)/workbench" />;
  }
  // 未授权时不挂载子页面，避免深链在跳转前触发管理接口。
  return children;
}
