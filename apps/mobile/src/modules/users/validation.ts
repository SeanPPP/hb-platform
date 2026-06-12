import type { StoreUserFormValues } from "@/modules/users/types";

export type UserDialogMode = "create" | "edit";
type Translate = (key: string) => string;

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const MIN_USERNAME_LENGTH = 3;
const MAX_USERNAME_LENGTH = 50;
const MIN_PASSWORD_LENGTH = 6;
const MAX_PASSWORD_LENGTH = 100;

export function validatePasswordValue(password: string, t: Translate) {
  const trimmed = password.trim();
  if (!trimmed) {
    return t("messages.passwordRequired");
  }

  if (trimmed.length < MIN_PASSWORD_LENGTH) {
    return t("messages.passwordTooShort");
  }

  if (trimmed.length > MAX_PASSWORD_LENGTH) {
    return t("messages.passwordTooLong");
  }

  return null;
}

export function validateStoreUserForm(
  values: StoreUserFormValues,
  mode: UserDialogMode,
  t: Translate
) {
  const username = values.username.trim();
  if (!username) {
    return t("messages.usernameRequired");
  }

  if (username.length < MIN_USERNAME_LENGTH || username.length > MAX_USERNAME_LENGTH) {
    return t("messages.usernameLength");
  }

  const email = values.email.trim();
  if (email && !EMAIL_PATTERN.test(email)) {
    return t("messages.emailInvalid");
  }

  if (mode === "create") {
    return validatePasswordValue(values.password, t);
  }

  return null;
}
