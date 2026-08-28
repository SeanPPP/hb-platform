import assert from "node:assert/strict";
import { existsSync, mkdirSync } from "node:fs";
import test from "node:test";

import {
  buildExpoMetroBundleCommand,
  verifyExpoMetroBundle,
} from "./verify-expo-metro-bundle.mjs";

test("POS Expo/Metro 验证固定平台、真实 export 参数并清理临时产物", async () => {
  const command = buildExpoMetroBundleCommand({
    platform: "ios",
    outputDirectory: "/tmp/metro-output",
  });
  assert.deepEqual(command, {
    command: "npx",
    args: ["expo", "export", "--platform", "ios", "--output-dir", "/tmp/metro-output"],
  });

  let outputDirectory = "";
  await verifyExpoMetroBundle({
    platform: "android",
    executeFn: async (spec) => {
      outputDirectory = spec.args.at(-1);
      mkdirSync(outputDirectory, { recursive: true });
      mkdirSync(`${outputDirectory}/assets`);
    },
  });
  assert.equal(existsSync(outputDirectory), false);
  assert.throws(
    () => buildExpoMetroBundleCommand({ platform: "web", outputDirectory: "/tmp/x" }),
    /ios|android/,
  );
});
