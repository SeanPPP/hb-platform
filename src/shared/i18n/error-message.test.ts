type Translator = (key: string) => string;

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

async function main() {
  const { resolveLocalizedErrorMessage } = await import("./error-message");

  const t: Translator = (key) => key;

  assertEqual(
    resolveLocalizedErrorMessage(new Error("网络超时"), {
      language: "zh",
      t,
      fallbackKey: "warehouse:messages.lookupFailed",
    }),
    "common:errors.timeout",
    "超时错误应映射到通用超时提示"
  );

  assertEqual(
    resolveLocalizedErrorMessage({ message: "Network Error" }, {
      language: "en",
      t,
      fallbackKey: "warehouse:messages.lookupFailed",
    }),
    "common:errors.network",
    "网络错误应映射到通用网络提示"
  );

  assertEqual(
    resolveLocalizedErrorMessage({ response: { status: 401 }, message: "未登录" }, {
      language: "en",
      t,
      fallbackKey: "common:errors.requestFailed",
    }),
    "common:errors.unauthorized",
    "未认证错误应映射到统一提示"
  );

  assertEqual(
    resolveLocalizedErrorMessage(new Error("商品条码不存在"), {
      language: "zh",
      t,
      fallbackKey: "productQuery:messages.lookupFailed",
    }),
    "商品条码不存在",
    "中文界面未知错误默认保留原始消息"
  );

  assertEqual(
    resolveLocalizedErrorMessage(new Error("商品条码不存在"), {
      language: "en",
      t,
      fallbackKey: "productQuery:messages.lookupFailed",
    }),
    "productQuery:messages.lookupFailed",
    "英文界面未知中文错误应回退到页面文案"
  );
}

void main();
