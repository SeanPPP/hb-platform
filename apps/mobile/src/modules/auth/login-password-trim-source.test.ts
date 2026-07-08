import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const currentDir = dirname(fileURLToPath(import.meta.url));
const authStoreSource = readFileSync(resolve(currentDir, "../../store/auth-store.ts"), "utf8");

if (!authStoreSource.includes("password: payload.password.trim()")) {
  throw new Error("auth-store.login should trim password before submitting loginApi payload");
}

console.log("login-password-trim-source.test.ts: ok");
