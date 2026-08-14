import { describe, expect, it, jest } from "@jest/globals";
import {
  fireEvent,
  render,
  waitFor,
} from "@testing-library/react-native";
import { StyleSheet } from "react-native";

import { ServerConnectionPanel } from "./server-connection-panel";

describe("ServerConnectionPanel", () => {
  it("小屏服务器编辑操作保持至少 48pt 触控目标", async () => {
    const screen = await render(
      <ServerConnectionPanel
        canSave
        currentAddress="https://hotbargain.vip/pos-api"
        saveAddress={async () => undefined}
        testAddress={async () => true}
      />,
    );

    const edit = screen.getByTestId("server-connection-edit");
    expect(
      StyleSheet.flatten(edit.props.style).minHeight,
    ).toBeGreaterThanOrEqual(48);

    await fireEvent.press(edit);
    for (const testID of [
      "server-connection-test",
      "server-connection-save",
    ]) {
      expect(
        StyleSheet.flatten(screen.getByTestId(testID).props.style)
          .minHeight,
      ).toBeGreaterThanOrEqual(48);
    }
  });

  it("候选地址测试成功后才允许确认保存", async () => {
    const testAddress = jest.fn<(address: string) => Promise<boolean>>(
      async () => true,
    );
    const saveAddress = jest.fn<(address: string) => Promise<void>>(
      async () => undefined,
    );
    const screen = await render(
      <ServerConnectionPanel
        canSave
        currentAddress="https://hotbargain.vip/pos-api"
        saveAddress={saveAddress}
        testAddress={testAddress}
      />,
    );

    expect(
      screen.getByTestId("server-connection-current").props.children,
    ).toBe("https://hotbargain.vip/pos-api");

    await fireEvent.press(screen.getByTestId("server-connection-edit"));
    await fireEvent.changeText(
      screen.getByTestId("server-connection-input"),
      "  http://192.168.31.246:5159  ",
    );

    expect(
      screen.getByTestId("server-connection-save").props.accessibilityState,
    ).toEqual({ disabled: true });

    await fireEvent.press(screen.getByTestId("server-connection-test"));

    await waitFor(() =>
      expect(testAddress).toHaveBeenCalledWith(
        "http://192.168.31.246:5159",
      ),
    );
    expect(screen.getByTestId("server-connection-status").props.children).toBe(
      "连接成功，可以保存此地址。",
    );
    expect(
      screen.getByTestId("server-connection-save").props.accessibilityState,
    ).toEqual({ disabled: false });

    await fireEvent.press(screen.getByTestId("server-connection-save"));
    expect(screen.getByTestId("server-connection-confirmation")).toBeTruthy();
    expect(saveAddress).not.toHaveBeenCalled();

    await fireEvent.press(screen.getByTestId("server-connection-confirm"));
    await waitFor(() =>
      expect(saveAddress).toHaveBeenCalledWith(
        "http://192.168.31.246:5159",
      ),
    );
  });

  it("测试通过后再次编辑会撤销保存资格", async () => {
    const screen = await render(
      <ServerConnectionPanel
        canSave
        currentAddress="https://hotbargain.vip/pos-api"
        saveAddress={async () => undefined}
        testAddress={async () => true}
      />,
    );

    await fireEvent.press(screen.getByTestId("server-connection-edit"));
    await fireEvent.press(screen.getByTestId("server-connection-test"));
    await waitFor(() =>
      expect(
        screen.getByTestId("server-connection-save").props.accessibilityState,
      ).toEqual({ disabled: false }),
    );

    await fireEvent.changeText(
      screen.getByTestId("server-connection-input"),
      "https://backup.example.test/pos-api",
    );

    expect(
      screen.getByTestId("server-connection-save").props.accessibilityState,
    ).toEqual({ disabled: true });
    expect(screen.queryByTestId("server-connection-status")).toBeNull();
  });

  it("账本不可检查时仍允许测试，但明确阻止保存", async () => {
    const testAddress = jest.fn<(address: string) => Promise<boolean>>(
      async () => true,
    );
    const saveAddress = jest.fn<(address: string) => Promise<void>>(
      async () => undefined,
    );
    const screen = await render(
      <ServerConnectionPanel
        canSave={false}
        currentAddress="https://hotbargain.vip/pos-api"
        saveAddress={saveAddress}
        testAddress={testAddress}
      />,
    );

    await fireEvent.press(screen.getByTestId("server-connection-edit"));
    await fireEvent.press(screen.getByTestId("server-connection-test"));

    await waitFor(() => expect(testAddress).toHaveBeenCalledTimes(1));
    expect(
      screen.getByTestId("server-connection-save-disabled-reason").props
        .children,
    ).toBe("本机账本无法检查，暂不能切换服务器。");
    expect(
      screen.getByTestId("server-connection-save").props.accessibilityState,
    ).toEqual({ disabled: true });
    expect(saveAddress).not.toHaveBeenCalled();
  });

  it("连接失败保留候选地址且不开放保存", async () => {
    const saveAddress = jest.fn<(address: string) => Promise<void>>(
      async () => undefined,
    );
    const screen = await render(
      <ServerConnectionPanel
        canSave
        currentAddress="https://hotbargain.vip/pos-api"
        saveAddress={saveAddress}
        testAddress={async () => false}
      />,
    );

    await fireEvent.press(screen.getByTestId("server-connection-edit"));
    await fireEvent.changeText(
      screen.getByTestId("server-connection-input"),
      "https://offline.example.test/pos-api",
    );
    await fireEvent.press(screen.getByTestId("server-connection-test"));

    await waitFor(() =>
      expect(screen.getByTestId("server-connection-status").props.children).toBe(
        "连接失败，请检查地址和网络后重试。",
      ),
    );
    expect(screen.getByTestId("server-connection-input").props.value).toBe(
      "https://offline.example.test/pos-api",
    );
    expect(
      screen.getByTestId("server-connection-save").props.accessibilityState,
    ).toEqual({ disabled: true });
    expect(saveAddress).not.toHaveBeenCalled();
  });
});
