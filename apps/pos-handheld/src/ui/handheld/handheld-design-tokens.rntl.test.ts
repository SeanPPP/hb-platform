import { expect, test } from "@jest/globals";

import {
  handheldControl,
  handheldLayout,
  handheldTone,
} from "./handheld-design-tokens";

test("handheld design tokens freeze the prompt-set spacing and touch contract", () => {
  expect(handheldLayout.grid).toBe(8);
  expect(handheldLayout.screenPadding).toBe(16);
  expect(handheldLayout.sectionGap).toBe(16);
  expect(handheldControl.minimumHeight).toBe(48);
  expect(handheldControl.radius).toBe(6);
});

test("handheld semantic tones use the shared POS palette", () => {
  expect(handheldTone).toEqual({
    danger: { foreground: "#B73932", background: "#F9E7E5" },
    info: { foreground: "#235C8C", background: "#E7F0F8" },
    success: { foreground: "#277C63", background: "#E5F3ED" },
    warning: { foreground: "#B98516", background: "#FFF5CF" },
  });
});
