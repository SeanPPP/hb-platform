import { useState } from "react";
import { i18n } from "@/shared/i18n/i18n";
import { useAuthStore } from "@/store/auth-store";

export function useLogin() {
  const login = useAuthStore((s) => s.login);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function execute(username: string, password: string) {
    setError(null);
    setIsLoading(true);
    try {
      await login({ username, password });
    } catch (e: any) {
      const msg =
        e?.response?.data?.message || e?.message || i18n.t("common:errors.loginFailed");
      setError(msg);
      throw e;
    } finally {
      setIsLoading(false);
    }
  }

  return { login: execute, isLoading, error };
}
