type Translator = (key: string) => string;

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

async function main() {
  const { resolveLocalizedErrorMessage } = await import("./error-message");

  const t: Translator = (key) => key;

  for (const code of [
    "PRINT_ERROR",
    "PRINT_PRODUCT_LABEL_ERROR",
    "PRINT_DISCOUNT_LABEL_ERROR",
    "PRINT_CLEARANCE_LABEL_ERROR",
    "PRINT_BIG_DISCOUNT_LABEL_ERROR",
    "PRINT_WAREHOUSE_PRODUCT_LABEL_ERROR",
    "PRINT_WAREHOUSE_LOCATION_LABEL_ERROR",
  ]) {
    for (const language of ["zh", "en"]) {
      const error = Object.assign(new Error("java.io.IOException: Broken pipe"), { code });
      assertEqual(
        resolveLocalizedErrorMessage(error, { language, t, fallbackKey: "messages.printFailed" }),
        "common:errors.printerDisconnected",
        `${language} ${code} 应显示可操作的打印机断线提示`
      );
      assertEqual(error.message, "java.io.IOException: Broken pipe", "本地化不能改写原始错误供日志排查");
    }
  }

  for (const message of [
    "broken pipe",
    "write failed: EPIPE",
    "Socket closed",
    "socket is closed",
    "Connection reset by peer",
    "No Bluetooth printer is connected.",
  ]) {
    assertEqual(
      resolveLocalizedErrorMessage({ code: "PRINT_PRODUCT_LABEL_ERROR", message }, {
        language: "zh",
        t,
      }),
      "common:errors.printerDisconnected",
      `原生错误对象应识别连接中断: ${message}`
    );
  }

  assertEqual(
    resolveLocalizedErrorMessage({ code: "EPIPE", message: "Broken pipe" }, { language: "zh", t }),
    "Broken pipe",
    "非打印错误不能误报为打印机断线"
  );

  assertEqual(
    resolveLocalizedErrorMessage({ code: "PRINT_PRODUCT_LABEL_ERROR", message: "Invalid barcode" }, {
      language: "zh",
      t,
    }),
    "Invalid barcode",
    "标签数据错误不能误报为打印机断线"
  );

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
