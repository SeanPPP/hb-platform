import { afterEach, describe, expect, it, jest } from "@jest/globals";
import { act, fireEvent, render, waitFor } from "@testing-library/react-native";
import { StyleSheet } from "react-native";

import {
  CATALOG_MAINTENANCE_MIN_TOUCH_TARGET,
  CatalogMaintenancePresenter,
  CatalogMaintenanceScreen,
  type CatalogMaintenancePort,
} from "./index";

class ScreenCatalogMaintenancePort implements CatalogMaintenancePort {
  public calls = 0;
  public hold: Promise<void> | null = null;
  public failure = false;

  public async downloadAndActivate() {
    this.calls += 1;
    await this.hold;
    if (this.failure) throw new Error("HTTP 401 Bearer should-not-appear");
    return { snapshotId: "snapshot-ui-1", itemCount: 42 };
  }
}

const presenters: CatalogMaintenancePresenter[] = [];

function createPresenter(port: CatalogMaintenancePort) {
  const presenter = new CatalogMaintenancePresenter({
    authenticatedStoreCode: "BNE-01",
    port,
  });
  presenters.push(presenter);
  return presenter;
}

afterEach(() => {
  for (const presenter of presenters.splice(0)) presenter.destroy();
  jest.restoreAllMocks();
});

describe("CatalogMaintenanceScreen", () => {
  it("中文界面显示 idle 与下载状态，并保持所有主操作至少 44pt", async () => {
    let release!: () => void;
    const port = new ScreenCatalogMaintenancePort();
    port.hold = new Promise<void>((resolve) => {
      release = resolve;
    });
    const presenter = createPresenter(port);
    const screen = await render(
      <CatalogMaintenanceScreen locale="zh" presenter={presenter} />,
    );

    expect(screen.getByTestId("catalog-maintenance-idle")).toBeTruthy();
    expect(screen.getByText("旧目录仍可继续使用。")).toBeTruthy();
    expect(
      screen.queryByText("The existing catalog remains available."),
    ).toBeNull();
    expect(screen.queryByText("准备就绪 / Ready to refresh")).toBeNull();
    expect(
      StyleSheet.flatten(
        screen.getByTestId("catalog-maintenance-refresh").props.style,
      ).minHeight,
    ).toBeGreaterThanOrEqual(CATALOG_MAINTENANCE_MIN_TOUCH_TARGET);

    await act(async () => {
      fireEvent.press(screen.getByTestId("catalog-maintenance-refresh"));
      await Promise.resolve();
    });
    await waitFor(() =>
      expect(
        screen.getByTestId("catalog-maintenance-downloading"),
      ).toBeTruthy(),
    );
    expect(
      screen.getByTestId("catalog-maintenance-refresh").props
        .accessibilityState,
    ).toEqual({ disabled: true });
    await act(async () => {
      release();
      await Promise.resolve();
    });
    await waitFor(() =>
      expect(screen.getByTestId("catalog-maintenance-success")).toBeTruthy(),
    );
  });

  it("英文成功与失败状态只显示英文，并保留稳定安全码", async () => {
    const successPort = new ScreenCatalogMaintenancePort();
    const successful = await render(
      <CatalogMaintenanceScreen
        locale="en"
        presenter={createPresenter(successPort)}
      />,
    );
    await act(async () => {
      fireEvent.press(successful.getByTestId("catalog-maintenance-refresh"));
      await Promise.resolve();
    });
    await successful.findByTestId("catalog-maintenance-success");
    expect(successful.getByText("snapshot-ui-1")).toBeTruthy();
    expect(successful.getByText("42")).toBeTruthy();
    expect(successful.getByText("Catalog updated")).toBeTruthy();
    expect(successful.queryByText("目录已更新")).toBeNull();

    const failedPort = new ScreenCatalogMaintenancePort();
    failedPort.failure = true;
    const failed = await render(
      <CatalogMaintenanceScreen
        locale="en"
        presenter={createPresenter(failedPort)}
      />,
    );
    await act(async () => {
      fireEvent.press(failed.getByTestId("catalog-maintenance-refresh"));
      await Promise.resolve();
    });
    await failed.findByTestId("catalog-maintenance-failed");
    expect(failed.getByText(/catalog-refresh-failed/)).toBeTruthy();
    expect(failed.queryByText(/should-not-appear/)).toBeNull();
    expect(
      failed.getByText("The existing catalog remains available."),
    ).toBeTruthy();
    expect(failed.queryByText("旧目录仍可继续使用。")).toBeNull();
  });

  it("可选返回按钮保留至少 44pt 触控目标", async () => {
    const onBack = jest.fn();
    const screen = await render(
      <CatalogMaintenanceScreen
        locale="zh"
        onBack={onBack}
        presenter={createPresenter(new ScreenCatalogMaintenancePort())}
      />,
    );
    const back = screen.getByTestId("catalog-maintenance-back");
    expect(
      StyleSheet.flatten(back.props.style).minHeight,
    ).toBeGreaterThanOrEqual(CATALOG_MAINTENANCE_MIN_TOUCH_TARGET);
    fireEvent.press(back);
    expect(onBack).toHaveBeenCalledTimes(1);
  });
});
