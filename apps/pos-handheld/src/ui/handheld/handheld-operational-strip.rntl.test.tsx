import { expect, test } from "@jest/globals";
import { render } from "@testing-library/react-native";

import { HandheldOperationalStrip } from "./handheld-operational-strip";

test("operational strip carries the six mobile POS statuses and never adds a display item", async () => {
  const screen = await render(
    <HandheldOperationalStrip
      items={[
        { key: "store", label: "门店", value: "Sunnybank" },
        { key: "cashier", label: "员工", value: "018" },
        { key: "network", label: "网络", value: "在线", tone: "success" },
        { key: "sync", label: "同步", value: "0", tone: "success" },
        { key: "scanner", label: "扫码", value: "就绪", tone: "success" },
        { key: "printer", label: "打印机", value: "已连接", tone: "success" },
      ]}
    />,
  );

  expect(screen.getAllByTestId("handheld-operational-item")).toHaveLength(6);
  expect(screen.queryByText(/客显|display/iu)).toBeNull();
});
