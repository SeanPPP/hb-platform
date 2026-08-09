import { expect, jest, test } from "@jest/globals";
import { render } from "@testing-library/react-native";
import { Text } from "react-native";

import { PosPanResponderView } from "./pos-pan-responder-view";

test("PosPanResponderView 显式转发 responder 回调且保持非交互子内容", async () => {
  const onMoveShouldSetResponder = jest.fn(() => false);
  const onResponderRelease = jest.fn();
  const panHandlers = {
    onMoveShouldSetResponder,
    onResponderRelease,
  } as never;
  const screen = await render(
    <PosPanResponderView
      panHandlers={panHandlers}
      testID="pan-responder-view"
    >
      <Text>拖动卡片</Text>
    </PosPanResponderView>,
  );

  const view = screen.getByTestId("pan-responder-view");
  expect(view.props.onMoveShouldSetResponder).toBe(onMoveShouldSetResponder);
  expect(view.props.onResponderRelease).toBe(onResponderRelease);
  expect(screen.getByText("拖动卡片")).toBeTruthy();
});
