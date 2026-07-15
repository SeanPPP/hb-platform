import * as SecureStore from "expo-secure-store";
import { clearSecureAccountSession } from "./secure-session-cleanup";
import { clearCashierPrintSecureSession } from "./cashier-print-secure-session";

const KEYS = {
  ACCESS_TOKEN: "hbweb_access_token",
  REFRESH_TOKEN: "hbweb_refresh_token",
  USER: "hbweb_user",
  CASHIER_BARCODE_PRINT_PENDING: "hbweb_cashier_barcode_print_pending",
} as const;

export const SecureStorage = {
  async setToken(token: string) {
    await SecureStore.setItemAsync(KEYS.ACCESS_TOKEN, token);
  },
  async getToken() {
    return SecureStore.getItemAsync(KEYS.ACCESS_TOKEN);
  },
  async removeToken() {
    await SecureStore.deleteItemAsync(KEYS.ACCESS_TOKEN);
  },
  async setRefreshToken(token: string) {
    await SecureStore.setItemAsync(KEYS.REFRESH_TOKEN, token);
  },
  async getRefreshToken() {
    return SecureStore.getItemAsync(KEYS.REFRESH_TOKEN);
  },
  async removeRefreshToken() {
    await SecureStore.deleteItemAsync(KEYS.REFRESH_TOKEN);
  },
  async setUser(user: object) {
    await SecureStore.setItemAsync(KEYS.USER, JSON.stringify(user));
  },
  async getUser<T = unknown>() {
    const d = await SecureStore.getItemAsync(KEYS.USER);
    return d ? (JSON.parse(d) as T) : null;
  },
  async setCashierBarcodePrintPending(value: string) {
    await SecureStore.setItemAsync(KEYS.CASHIER_BARCODE_PRINT_PENDING, value);
  },
  async getCashierBarcodePrintPending() {
    return SecureStore.getItemAsync(KEYS.CASHIER_BARCODE_PRINT_PENDING);
  },
  async removeCashierBarcodePrintPending() {
    await SecureStore.deleteItemAsync(KEYS.CASHIER_BARCODE_PRINT_PENDING);
  },
  async clearCashierBarcodePrintSession() {
    await clearCashierPrintSecureSession(this);
  },
  async removeUser() {
    await SecureStore.deleteItemAsync(KEYS.USER);
  },
  async clearAll() {
    await clearSecureAccountSession(this);
  },
};
