import AsyncStorage from "@react-native-async-storage/async-storage";

export const AppAsyncStorage = {
  async getString(key: string) {
    return AsyncStorage.getItem(key);
  },
  async setString(key: string, value: string) {
    await AsyncStorage.setItem(key, value);
  },
  async removeItem(key: string) {
    await AsyncStorage.removeItem(key);
  },
  async getObject<T = unknown>(key: string) {
    const d = await AsyncStorage.getItem(key);
    return d ? (JSON.parse(d) as T) : null;
  },
  async setObject(key: string, value: unknown) {
    await AsyncStorage.setItem(key, JSON.stringify(value));
  },
};
