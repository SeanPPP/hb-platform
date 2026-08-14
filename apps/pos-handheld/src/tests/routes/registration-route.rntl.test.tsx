import { expect, jest, test } from "@jest/globals";
import { render } from "@testing-library/react-native";

import RegistrationRoute from "../../../app/registration";

jest.mock("@/features/device-registration", () => {
  const React = jest.requireActual<typeof import("react")>("react");
  const { Text } =
    jest.requireActual<typeof import("react-native")>("react-native");
  return {
    DeviceRegistrationScreen: () =>
      React.createElement(
        Text,
        { testID: "device-registration-route-screen" },
        "registration",
      ),
  };
});

test("注册路由直接呈现真实设备注册屏", async () => {
  const screen = await render(<RegistrationRoute />);

  expect(
    screen.getByTestId("device-registration-route-screen"),
  ).toBeTruthy();
  await screen.unmount();
});
