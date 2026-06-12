import * as SecureStore from "expo-secure-store";

const KEYS = {
  ACCESS_TOKEN: "hbweb_access_token",
  REFRESH_TOKEN: "hbweb_refresh_token",
  USER: "hbweb_user",
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
  async clearAll() {
    await Promise.all([
      SecureStore.deleteItemAsync(KEYS.ACCESS_TOKEN),
      SecureStore.deleteItemAsync(KEYS.REFRESH_TOKEN),
      SecureStore.deleteItemAsync(KEYS.USER),
    ]);
  },
};
