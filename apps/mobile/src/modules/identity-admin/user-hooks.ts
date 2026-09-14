import { useEffect, useState } from "react";
import { useAuthStore } from "@/store/auth-store";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import en from "@/locales/en/identityUsers.json";
import zh from "@/locales/zh/identityUsers.json";
import { isIdentitySessionAllowed } from "./user-logic";

export function useIdentityUserCopy() {
  const { language } = useAppTranslation();
  return language === "zh" ? zh : en;
}

export function useIdentitySession(permission = "Users.View") {
  const user = useAuthStore(s => s.user);
  const access = useAuthStore(s => s.access);
  const sessionKind = useAuthStore(s => s.sessionKind);
  const isAuthenticated = useAuthStore(s => s.isAuthenticated);
  const iosReviewOfflineGuardActive = useAuthStore(s => s.iosReviewOfflineGuardActive);
  const allowed = isIdentitySessionAllowed({ sessionKind, isAuthenticated, iosReviewOfflineGuardActive }, access.hasPermission(permission));
  return { user, access, allowed, sessionKind, isAuthenticated, actorKey: user?.userGUID ?? "" };
}

export function useIdentitySearch(value: string) {
  const [search, setSearch] = useState(value.trim());
  useEffect(() => { const timer = setTimeout(() => setSearch(value.trim()), 300); return () => clearTimeout(timer); }, [value]);
  return search;
}

export function identityDate(value?: string) {
  if (!value) return "—";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "—" : date.toLocaleString();
}
