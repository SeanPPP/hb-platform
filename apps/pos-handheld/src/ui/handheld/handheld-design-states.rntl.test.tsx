import { expect, test } from "@jest/globals";
import { render } from "@testing-library/react-native";
import { Text } from "react-native";

import stateMatrix from "../../../test-fixtures/handheld-design/state-matrix.json";

import {
  handheldDesignStates,
  HandheldStateSurface,
} from "./handheld-design-states";

test("runtime state slug contract stays aligned with all 46 design boards", () => {
  expect(handheldDesignStates).toHaveLength(46);
  expect(handheldDesignStates).toEqual(
    stateMatrix.states.map(({ id, slug }) => ({ id, slug })),
  );
});

test("state surface exposes a deterministic visual-verification target", async () => {
  const screen = await render(
    <HandheldStateSurface slug="sales-active">
      <Text>购物车内容</Text>
    </HandheldStateSurface>,
  );

  expect(screen.getByTestId("handheld-state-sales-active")).toHaveTextContent(
    "购物车内容",
  );
});
