import { sha256 } from "js-sha256";

export function hashPassword(password: string): string {
  return sha256(password);
}
