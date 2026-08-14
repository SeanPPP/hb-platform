import { beforeEach, expect, jest, test } from "@jest/globals";
import { render } from "@testing-library/react-native";

import IndexScreen from "../../../app/index";

const mockResolvePosEntryRoute = jest.fn();
let mockRuntime: unknown;
let mockActiveCashier: unknown;

jest.mock("expo-router", () => {
  const React = jest.requireActual<typeof import("react")>("react");
  const { Text } =
    jest.requireActual<typeof import("react-native")>("react-native");
  return {
    Redirect: ({ href }: { href: string }) =>
      React.createElement(Text, { testID: "redirect" }, href),
  };
});

jest.mock("@/core/runtime/pos-runtime-context", () => ({
  usePosRuntime: () => mockRuntime,
}));

jest.mock("@/features/cashier-login", () => ({
  resolvePosEntryRoute: (...args: unknown[]) => mockResolvePosEntryRoute(...args),
  useCashierLoginStore: (selector: (state: { activeCashier: unknown }) => unknown) =>
    selector({ activeCashier: mockActiveCashier }),
}));

jest.mock("@/ui/screens/bootstrap-screen", () => {
  const React = jest.requireActual<typeof import("react")>("react");
  const { Text } =
    jest.requireActual<typeof import("react-native")>("react-native");
  return {
    BootstrapScreen: () =>
      React.createElement(Text, { testID: "bootstrap" }, "bootstrap"),
  };
});

beforeEach(() => {
  mockRuntime = { state: { phase: "bootstrapping" } };
  mockActiveCashier = null;
  mockResolvePosEntryRoute.mockReset();
});

test("入口尚未解析路由时展示 BootstrapScreen", async () => {
  mockResolvePosEntryRoute.mockReturnValue(null);

  const screen = await render(<IndexScreen />);

  expect(screen.getByTestId("bootstrap")).toBeTruthy();
  expect(mockResolvePosEntryRoute).toHaveBeenCalledWith(
    (mockRuntime as { state: unknown }).state,
    null,
  );
  await screen.unmount();
});
