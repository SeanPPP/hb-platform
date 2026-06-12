import { extractApiErrorMessage } from "../../shared/api/error-message";
import { validatePasswordValue, validateStoreUserForm } from "./validation";

const messages: Record<string, string> = {
  "messages.emailInvalid": "Invalid email",
  "messages.passwordRequired": "Enter a password",
  "messages.passwordTooShort": "Password must be at least 6 characters",
  "messages.passwordTooLong": "Password must be 100 characters or fewer",
  "messages.usernameLength": "Username must be 3 to 50 characters",
  "messages.usernameRequired": "Enter a username",
};

function t(key: string) {
  return messages[key] ?? key;
}

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

const validForm = {
  username: "staff001",
  fullName: "Staff One",
  email: "staff@example.com",
  phone: "",
  password: "secret1",
  status: true,
};

assertEqual(
  validateStoreUserForm({ ...validForm, password: "12345" }, "create", t),
  "Password must be at least 6 characters",
  "create form rejects short passwords"
);

assertEqual(
  validateStoreUserForm({ ...validForm, email: "not-email" }, "create", t),
  "Invalid email",
  "create form rejects invalid email"
);

assertEqual(
  validateStoreUserForm({ ...validForm, username: "ab" }, "create", t),
  "Username must be 3 to 50 characters",
  "create form rejects short usernames"
);

assertEqual(
  validatePasswordValue("12345", t),
  "Password must be at least 6 characters",
  "reset password rejects short passwords"
);

assertEqual(
  extractApiErrorMessage(
    {
      response: {
        data: {
          success: false,
          message: "请求参数验证失败",
          details: {
            Password: {
              errors: ["密码长度必须在6-100个字符之间"],
            },
          },
        },
      },
    },
    "Fallback"
  ),
  "密码长度必须在6-100个字符之间",
  "api error extracts validation details"
);

assertEqual(
  extractApiErrorMessage(
    {
      response: {
        data: {
          success: false,
          message: "用户名已存在",
        },
      },
    },
    "Fallback"
  ),
  "用户名已存在",
  "api error extracts envelope message"
);
