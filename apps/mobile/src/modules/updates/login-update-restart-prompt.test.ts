import assert from "node:assert/strict";
import {
  checkLoginUpdateRestartPrompt,
  shouldShowLoginUpdateRestartPrompt,
} from "./login-update-restart-prompt";

async function run() {
  assert.equal(
    shouldShowLoginUpdateRestartPrompt({ status: "downloaded" }),
    true,
    "下载完成后登录页应提示用户重启"
  );

  assert.equal(
    shouldShowLoginUpdateRestartPrompt({ status: "not-available" }),
    false,
    "没有可用更新时登录页不应显示提示"
  );
  assert.equal(
    shouldShowLoginUpdateRestartPrompt({ status: "development-disabled" }),
    false,
    "开发模式禁用 OTA 时登录页不应显示提示"
  );
  assert.equal(
    shouldShowLoginUpdateRestartPrompt({ status: "configuration-disabled" }),
    false,
    "OTA 配置禁用时登录页不应显示提示"
  );

  {
    const shouldPrompt = await checkLoginUpdateRestartPrompt({
      checkAndDownload: async () => ({ status: "downloaded" }),
      warn: () => undefined,
    });

    assert.equal(shouldPrompt, true, "检查并下载到更新后应返回可重启状态");
  }

  {
    const warnings: unknown[] = [];
    const shouldPrompt = await checkLoginUpdateRestartPrompt({
      checkAndDownload: async () => {
        throw new Error("network failed");
      },
      warn: (error) => warnings.push(error),
    });

    assert.equal(shouldPrompt, false, "检查失败时应静默返回不可重启状态");
    assert.equal(warnings.length, 1, "检查失败应记录一次警告供调试使用");
  }

  console.log("login-update-restart-prompt.test.ts: ok");
}

void run();
