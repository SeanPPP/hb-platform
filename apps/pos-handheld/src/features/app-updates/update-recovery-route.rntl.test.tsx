import { afterEach, beforeEach, expect, jest, test } from "@jest/globals";
import {
  act,
  cleanup,
  fireEvent,
  render,
  waitFor,
} from "@testing-library/react-native";

import UpdateRecoveryRoute, {
  resolveUpdateRecoverySection,
  shareUpdateRecoverySnapshot,
} from "../../../app/update-recovery";

import type { AppUpdateRecoveryRuntimeSnapshot } from "./app-update-recovery-contract";

const mockFileCreate = jest.fn();
const mockFileWrite = jest.fn();
const mockFileDelete = jest.fn();
const mockFileConstructor = jest.fn();
const mockShareAsync =
  jest.fn<(uri: string, options: unknown) => Promise<void>>();
const mockSharingAvailable = jest.fn<() => Promise<boolean>>();
const mockRouterPush = jest.fn();
const mockRouterReplace = jest.fn();
const mockReadSnapshot =
  jest.fn<() => Promise<AppUpdateRecoveryRuntimeSnapshot>>();
const mockGetSafetySnapshot = jest.fn<() => Promise<{
  hasActiveCart: boolean;
  hasCatalogRefreshInFlight: boolean;
  hasFulfilmentInFlight: boolean;
  hasPendingDurableWrite: boolean;
  hasRecoveryRequired: boolean;
  hasSyncOrAuditInFlight: boolean;
  hasUnresolvedPayment: boolean;
}>>();
let mockFileExists = false;
let mockSearchParams: { section?: string } = {};
let mockRuntime: {
  state: {
    backend: "reachable";
    device: "authorized-online";
  };
  services: {
    appUpdateRecovery: {
      readSnapshot: typeof mockReadSnapshot;
    };
    appUpdateSafety: {
      getSnapshot: typeof mockGetSafetySnapshot;
    };
  } | null;
};

jest.mock("expo-file-system", () => ({
  File: class {
    public readonly uri =
      "file:///cache/hb-pos-update-support.json";

    public constructor(directory: unknown, fileName: string) {
      mockFileConstructor(directory, fileName);
    }

    public get exists() {
      return mockFileExists;
    }

    public create(options: unknown) {
      mockFileExists = true;
      mockFileCreate(options);
    }

    public write(value: string) {
      mockFileWrite(value);
    }

    public delete() {
      mockFileExists = false;
      mockFileDelete();
    }
  },
  Paths: { cache: "file:///cache/" },
}));

jest.mock("expo-sharing", () => ({
  isAvailableAsync: () => mockSharingAvailable(),
  shareAsync: (uri: string, options: unknown) =>
    mockShareAsync(uri, options),
}));

jest.mock("react-i18next", () => ({
  useTranslation: () => ({
    i18n: { language: "zh", resolvedLanguage: "zh" },
  }),
}));

jest.mock("expo-router", () => ({
  useLocalSearchParams: () => mockSearchParams,
  useRouter: () => ({
    push: mockRouterPush,
    replace: mockRouterReplace,
  }),
}));

jest.mock("@/core/runtime/pos-runtime-context", () => ({
  usePosRuntime: () => mockRuntime,
}));

beforeEach(() => {
  jest.clearAllMocks();
  mockFileExists = false;
  mockSearchParams = { section: "support" };
  mockSharingAvailable.mockResolvedValue(true);
  mockShareAsync.mockResolvedValue(undefined);
  mockReadSnapshot.mockResolvedValue({
    appVersion: "1.2.3",
    buildNumber: "101",
    runtimeVersion: "1.2.3",
    channel: "pos-handheld-production",
    apiOrigin: "https://pos.example",
  });
  mockGetSafetySnapshot.mockResolvedValue({
    hasActiveCart: false,
    hasCatalogRefreshInFlight: false,
    hasFulfilmentInFlight: false,
    hasPendingDurableWrite: false,
    hasRecoveryRequired: true,
    hasSyncOrAuditInFlight: true,
    hasUnresolvedPayment: false,
  });
  mockRuntime = {
    state: {
      backend: "reachable",
      device: "authorized-online",
    },
    services: {
      appUpdateRecovery: {
        readSnapshot: mockReadSnapshot,
      },
      appUpdateSafety: {
        getSnapshot: mockGetSafetySnapshot,
      },
    },
  };
});

afterEach(async () => {
  await cleanup();
  mockFileExists = false;
});

test("恢复页只接受 support 查询值，其他输入回退到 settings", () => {
  expect(resolveUpdateRecoverySection("support")).toBe("support");
  expect(resolveUpdateRecoverySection(["support", "settings"])).toBe(
    "support",
  );
  expect(resolveUpdateRecoverySection("sync-history")).toBe(
    "settings",
  );
  expect(resolveUpdateRecoverySection(undefined)).toBe("settings");
});

test("支持导出使用固定缓存文件并在成功后删除", async () => {
  const serialized = '{"appVersion":"1.2.3"}';

  await shareUpdateRecoverySnapshot(serialized);

  expect(mockFileConstructor).toHaveBeenCalledWith(
    "file:///cache/",
    "hb-pos-update-support.json",
  );
  expect(mockFileCreate).toHaveBeenCalledWith({
    intermediates: true,
    overwrite: true,
  });
  expect(mockFileWrite).toHaveBeenCalledWith(serialized);
  expect(mockShareAsync).toHaveBeenCalledWith(
    "file:///cache/hb-pos-update-support.json",
    {
      UTI: "public.json",
      dialogTitle: "HB POS update diagnostics",
      mimeType: "application/json",
    },
  );
  expect(mockFileDelete).toHaveBeenCalledTimes(1);
  expect(mockFileExists).toBe(false);
});

test("系统分享失败时仍删除临时诊断文件", async () => {
  mockShareAsync.mockRejectedValueOnce(new Error("share failed"));

  await expect(
    shareUpdateRecoverySnapshot('{"appVersion":"1.2.3"}'),
  ).rejects.toThrow("share failed");

  expect(mockFileDelete).toHaveBeenCalledTimes(1);
  expect(mockFileExists).toBe(false);
});

test("恢复页导出失败显示稳定提示且不会泄漏底层异常", async () => {
  mockShareAsync.mockRejectedValueOnce(
    new Error("provider secret should stay hidden"),
  );
  const screen = await render(<UpdateRecoveryRoute />);
  const exportButton = await waitFor(() =>
    screen.getByTestId("app-update-recovery-export"),
  );

  await act(async () => {
    fireEvent.press(exportButton);
  });

  expect(
    await waitFor(() =>
      screen.getByTestId("app-update-recovery-export-error"),
    ),
  ).toBeTruthy();
  expect(screen.getByText("诊断导出失败，请重试。")).toBeTruthy();
  expect(screen.queryByText(/provider secret/u)).toBeNull();
  expect(mockFileDelete).toHaveBeenCalledTimes(1);
  expect(mockFileExists).toBe(false);
});

test("恢复页读取真实升级安全快照并显示同步与支付恢复状态", async () => {
  const screen = await render(<UpdateRecoveryRoute />);

  expect(
    await screen.findByTestId("app-update-recovery-sync-state"),
  ).toHaveTextContent("同步与审计正在处理");
  expect(
    screen.getByTestId("app-update-recovery-payment-state"),
  ).toHaveTextContent("需要完成支付恢复");
  expect(mockGetSafetySnapshot).toHaveBeenCalledTimes(1);
});
