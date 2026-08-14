export type SettingsSquareEnvironment = "Sandbox" | "Production";

export type SettingsSquareTokenStatus = Readonly<{
  environment: SettingsSquareEnvironment;
  configured: boolean;
  enabled: boolean;
  updatedAt: string | null;
}>;

export type SettingsSquareLocation = Readonly<{
  id: string;
  name: string;
  status: string | null;
  currency: string | null;
  country: string | null;
}>;

export type SettingsSquareDevice = Readonly<{
  id: string;
  code: string | null;
  name: string;
  status: string | null;
  locationId: string | null;
  sandboxTest: boolean;
}>;

export type SettingsSquareDeviceCode = Readonly<{
  id: string;
  code: string | null;
  status: string | null;
  deviceId: string | null;
  locationId: string | null;
  name: string;
}>;

export type SettingsSquareCreateDeviceCodeInput = Readonly<{
  environment: SettingsSquareEnvironment;
  idempotencyKey: string;
  locationId: string;
  name?: string;
  productType?: string;
}>;

export interface SettingsSquareSetupPort {
  getSquareTokenStatus(
    environment: SettingsSquareEnvironment,
    signal: AbortSignal,
  ): Promise<SettingsSquareTokenStatus>;
  listSquareLocations(
    environment: SettingsSquareEnvironment,
    signal: AbortSignal,
  ): Promise<readonly SettingsSquareLocation[]>;
  listSquareDevices(
    environment: SettingsSquareEnvironment,
    locationId: string,
    signal: AbortSignal,
  ): Promise<readonly SettingsSquareDevice[]>;
  listSquareDeviceCodes(
    environment: SettingsSquareEnvironment,
    locationId: string,
    signal: AbortSignal,
  ): Promise<readonly SettingsSquareDeviceCode[]>;
  createSquareDeviceCode(
    input: SettingsSquareCreateDeviceCodeInput,
    signal: AbortSignal,
  ): Promise<SettingsSquareDeviceCode>;
  getSquareDeviceCode(
    environment: SettingsSquareEnvironment,
    deviceCodeId: string,
    signal: AbortSignal,
  ): Promise<SettingsSquareDeviceCode>;
}

export const SETTINGS_SQUARE_SANDBOX_TEST_DEVICE_STATUS =
  "SANDBOX_TEST" as const;

export const SETTINGS_SQUARE_SANDBOX_CHECKOUT_DEVICES = Object.freeze([
  Object.freeze({
    id: "9fa747a2-25ff-48ee-b078-04381f7c828f",
    name: "Sandbox: success credit card",
  }),
  Object.freeze({
    id: "22cd266c-6246-4c06-9983-67f0c26346b0",
    name: "Sandbox: success credit card with 20% tip",
  }),
  Object.freeze({
    id: "4mp4e78c-88ed-4d55-a269-8008dfe14e9",
    name: "Sandbox: success gift card",
  }),
  Object.freeze({
    id: "388b5a08-a77c-48ef-ad2a-4a790e6f2789",
    name: "Sandbox: success Interac credit card (CAD)",
  }),
  Object.freeze({
    id: "2b0b734b-b187-47f0-9d6f-288745210bdb",
    name: "Sandbox: success Interac with 20% tip (CAD)",
  }),
  Object.freeze({
    id: "19a01fbd-3dcd-4d9f-a499-a641684af745",
    name: "Sandbox: success eMoney/FeLiCa",
  }),
  Object.freeze({
    id: "819f8d79-961e-4097-8f70-ef70b3e7db28",
    name: "Sandbox: success Afterpay",
  }),
  Object.freeze({
    id: "cae0ee02-f83b-11ec-b939-0242ac120002",
    name: "Sandbox: success PayPay (Japan)",
  }),
  Object.freeze({
    id: "841100b9-ee60-4537-9bcf-e30b2ba5e215",
    name: "Sandbox: cancel by buyer",
  }),
  Object.freeze({
    id: "0a956d49-619a-4530-8e5e-8eac603ffc5e",
    name: "Sandbox: timeout by Square",
  }),
  Object.freeze({
    id: "da40d603-c2ea-4a65-8cfd-f42e36dab0c7",
    name: "Sandbox: offline terminal",
  }),
] as const);

export function normalizeSettingsSquareDeviceId(
  value: unknown,
): string | null {
  if (typeof value !== "string") return null;
  const trimmed = value.trim();
  if (!trimmed) return null;
  const normalized = trimmed.toLowerCase().startsWith("device:")
    ? trimmed.slice("device:".length).trim()
    : trimmed;
  return normalized || null;
}

export function mergeSettingsSquareDevices(
  environment: SettingsSquareEnvironment,
  locationId: string,
  devices: readonly SettingsSquareDevice[],
): readonly SettingsSquareDevice[] {
  const normalizedLocationId = normalizedOptionalText(locationId);
  const sandboxDevicesById = new Map(
    SETTINGS_SQUARE_SANDBOX_CHECKOUT_DEVICES.map((device) => [
      device.id.toLowerCase(),
      device,
    ]),
  );
  const merged: SettingsSquareDevice[] = [];
  const seenIds = new Set<string>();

  for (const device of devices) {
    const normalizedId = normalizeSettingsSquareDeviceId(device.id);
    if (!normalizedId) continue;
    const sandboxDevice = sandboxDevicesById.get(normalizedId.toLowerCase());
    const canonicalId = sandboxDevice?.id ?? normalizedId;
    const deduplicationKey = canonicalId.toLowerCase();
    if (seenIds.has(deduplicationKey)) continue;
    seenIds.add(deduplicationKey);

    merged.push(
      Object.freeze({
        id: canonicalId,
        code: normalizedOptionalText(device.code),
        name: normalizedOptionalText(device.name) ?? canonicalId,
        status: normalizedOptionalText(device.status),
        locationId:
          normalizedOptionalText(device.locationId) ?? normalizedLocationId,
        sandboxTest: environment === "Sandbox" && sandboxDevice !== undefined,
      }),
    );
  }

  if (environment === "Sandbox") {
    for (const sandboxDevice of SETTINGS_SQUARE_SANDBOX_CHECKOUT_DEVICES) {
      const deduplicationKey = sandboxDevice.id.toLowerCase();
      if (seenIds.has(deduplicationKey)) continue;
      seenIds.add(deduplicationKey);
      // Square Sandbox 只能通过官方 checkout device id 控制成功、取消和超时结果。
      merged.push(
        Object.freeze({
          id: sandboxDevice.id,
          code: null,
          name: sandboxDevice.name,
          status: SETTINGS_SQUARE_SANDBOX_TEST_DEVICE_STATUS,
          locationId: normalizedLocationId,
          sandboxTest: true,
        }),
      );
    }
  }

  return Object.freeze(merged);
}

function normalizedOptionalText(value: unknown): string | null {
  return typeof value === "string" && value.trim() ? value.trim() : null;
}
