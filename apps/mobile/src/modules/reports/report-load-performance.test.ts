import assert from "node:assert/strict";
import test from "node:test";

import {
  REPORT_FIRST_DATA_BUDGET_MS,
  REPORT_NAVIGATION_MARKER_TTL_MS,
  ReportLoadPerformanceTimer,
  ReportLoadVisibilityGate,
  buildReportLoadApplicationLog,
  consumeReportNavigationStart,
  discardReportNavigationStart,
  hasPendingReportNavigationStart,
  hasUsableSuccessfulReportCache,
  markReportHubNavigationStart,
  markReportNavigationStart,
  recordReportLoadPerformance,
  type ReportLoadPerformanceEvent,
} from "./report-load-performance";

test("底栏进入报告中心只允许真实活动页签消费同一次点击", () => {
  let now = 100;
  const productTimer = new ReportLoadPerformanceTimer(() => now);
  const revenueTimer = new ReportLoadPerformanceTimer(() => now);

  markReportHubNavigationStart(now);
  assert.equal(hasPendingReportNavigationStart("revenue", now), true);
  assert.equal(hasPendingReportNavigationStart("product", now), true);

  now = 150;
  productTimer.start("warm", "product");
  productTimer.markDataNormalized();
  now = 180;
  assert.equal(productTimer.markFirstRowVisible()?.navigationMs, 50);
  assert.equal(hasPendingReportNavigationStart("revenue", now), false, "未激活页签不得残留同组点击");

  revenueTimer.start("warm", "revenue");
  revenueTimer.markDataNormalized();
  now = 200;
  assert.equal(revenueTimer.markFirstRowVisible()?.navigationMs, 0, "后续查询不得误消费旧底栏点击");
});

test("已聚焦但没有可执行查询时可整组丢弃底栏点击", () => {
  markReportHubNavigationStart(500);
  discardReportNavigationStart("revenue");

  assert.equal(hasPendingReportNavigationStart("revenue", 600), false);
  assert.equal(hasPendingReportNavigationStart("product", 600), false);
});

test("导航计时标记按报表隔离、一次性消费且过期后丢弃", () => {
  markReportNavigationStart("revenue", 100);
  markReportNavigationStart("product", 120);

  assert.equal(consumeReportNavigationStart("revenue", 180), 100);
  assert.equal(consumeReportNavigationStart("revenue", 181), null, "同一标记只能消费一次");
  assert.equal(consumeReportNavigationStart("product", 200), 120, "营业额不得消费商品标记");

  markReportNavigationStart("revenue", 300);
  assert.equal(
    consumeReportNavigationStart("revenue", 300 + REPORT_NAVIGATION_MARKER_TTL_MS + 1),
    null,
    "陈旧点击不得污染后续进入报告页的计时",
  );
});

test("首个报表查询继承点击起点，同时保留请求与渲染分段语义", () => {
  let now = 100;
  const timer = new ReportLoadPerformanceTimer(() => now);

  markReportNavigationStart("revenue", now);
  now = 160;
  timer.start("cold", "revenue");
  now = 1_010;
  timer.markDataNormalized();
  now = 1_210;

  assert.deepEqual(timer.markFirstRowVisible(), {
    cacheState: "cold",
    navigationMs: 60,
    requestMs: 850,
    normalizeRenderMs: 200,
    totalMs: 1_110,
    budgetMs: REPORT_FIRST_DATA_BUDGET_MS.cold,
    meetsFirstDataBudget: true,
  });
});

test("在途请求晚于启动收到返回 Reports 标记时从本次焦点会话重新计时", () => {
  let now = 100;
  const timer = new ReportLoadPerformanceTimer(() => now);

  timer.start("cold", "revenue");
  now = 200;
  markReportHubNavigationStart(now);
  now = 300;
  timer.markDataNormalized();
  now = 350;

  assert.deepEqual(timer.markFirstRowVisible(), {
    cacheState: "cold",
    navigationMs: 0,
    requestMs: 100,
    normalizeRenderMs: 50,
    totalMs: 150,
    budgetMs: REPORT_FIRST_DATA_BUDGET_MS.cold,
    meetsFirstDataBudget: true,
  });
  assert.equal(hasPendingReportNavigationStart("revenue", now), false);
  assert.equal(hasPendingReportNavigationStart("product", now), false, "活动页签必须整组认领点击");
});

test("数据已归一化后才收到返回 Reports 标记时不会计入操作前耗时", () => {
  let now = 100;
  const timer = new ReportLoadPerformanceTimer(() => now);

  timer.start("warm", "product");
  now = 180;
  timer.markDataNormalized();
  now = 200;
  markReportHubNavigationStart(now);
  now = 240;

  assert.deepEqual(timer.markFirstRowVisible(), {
    cacheState: "warm",
    navigationMs: 0,
    requestMs: 0,
    normalizeRenderMs: 40,
    totalMs: 40,
    budgetMs: REPORT_FIRST_DATA_BUDGET_MS.warm,
    meetsFirstDataBudget: true,
  });
});

test("点击 A 失败后新焦点点击 B 必须覆盖旧起点并整组消费 B", () => {
  let now = 100;
  const timer = new ReportLoadPerformanceTimer(() => now);

  markReportHubNavigationStart(now);
  now = 120;
  timer.start("cold", "revenue");
  now = 200;
  timer.fail();

  now = 300;
  markReportHubNavigationStart(now);
  now = 340;
  timer.start("cold", "revenue");
  now = 500;
  timer.markDataNormalized();
  now = 550;

  assert.deepEqual(timer.markFirstRowVisible(), {
    cacheState: "cold",
    navigationMs: 40,
    requestMs: 160,
    normalizeRenderMs: 50,
    totalMs: 250,
    budgetMs: REPORT_FIRST_DATA_BUDGET_MS.cold,
    meetsFirstDataBudget: true,
  });
  assert.equal(hasPendingReportNavigationStart("revenue", now), false);
  assert.equal(hasPendingReportNavigationStart("product", now), false, "点击 B 的候选 marker 必须整组消费");
});

test("已认领 A 的在途请求被新焦点 B 复用时也必须改由 B 认领", () => {
  let now = 100;
  const timer = new ReportLoadPerformanceTimer(() => now);

  markReportHubNavigationStart(now);
  now = 120;
  timer.start("cold", "revenue");
  now = 300;
  markReportHubNavigationStart(now);
  now = 450;
  timer.markDataNormalized();
  now = 500;

  assert.deepEqual(timer.markFirstRowVisible(), {
    cacheState: "cold",
    navigationMs: 0,
    requestMs: 150,
    normalizeRenderMs: 50,
    totalMs: 200,
    budgetMs: REPORT_FIRST_DATA_BUDGET_MS.cold,
    meetsFirstDataBudget: true,
  });
  assert.equal(hasPendingReportNavigationStart("revenue", now), false);
  assert.equal(hasPendingReportNavigationStart("product", now), false);
});

test("已认领 A 的旧请求在 B 焦点失败时认领 B，Retry 继续从 B 计时", () => {
  let now = 100;
  const timer = new ReportLoadPerformanceTimer(() => now);

  markReportHubNavigationStart(now);
  now = 120;
  timer.start("cold", "revenue");
  now = 200;
  markReportHubNavigationStart(now);
  now = 250;
  timer.fail();

  assert.equal(hasPendingReportNavigationStart("revenue", now), false);
  assert.equal(hasPendingReportNavigationStart("product", now), false, "失败必须整组认领 B marker");

  now = 300;
  timer.start("cold", "revenue");
  now = 400;
  timer.markDataNormalized();
  now = 450;
  assert.deepEqual(timer.markFirstRowVisible(), {
    cacheState: "cold",
    navigationMs: 100,
    requestMs: 100,
    normalizeRenderMs: 50,
    totalMs: 250,
    budgetMs: REPORT_FIRST_DATA_BUDGET_MS.cold,
    meetsFirstDataBudget: true,
  });
});

test("在途复用请求失败或取消时清除尚未认领的焦点 marker", () => {
  let now = 100;
  const timer = new ReportLoadPerformanceTimer(() => now);

  timer.start("cold", "revenue");
  now = 200;
  markReportHubNavigationStart(now);
  now = 250;
  timer.fail();
  assert.equal(hasPendingReportNavigationStart("revenue", now), false);
  assert.equal(hasPendingReportNavigationStart("product", now), false);

  now = 300;
  timer.start("cold", "product");
  now = 400;
  markReportHubNavigationStart(now);
  now = 450;
  timer.cancel();
  assert.equal(hasPendingReportNavigationStart("revenue", now), false);
  assert.equal(hasPendingReportNavigationStart("product", now), false);
});

test("同次点击后的并发启动和失败重试不能覆盖更早导航起点", () => {
  let now = 1_000;
  const timer = new ReportLoadPerformanceTimer(() => now);

  markReportNavigationStart("product", now);
  now = 1_080;
  timer.start("cold", "product");
  now = 1_240;
  timer.start("cold", "product");
  now = 1_400;
  timer.fail();
  now = 1_520;
  timer.start("cold", "product");
  now = 1_800;
  timer.markDataNormalized();
  now = 1_900;

  assert.deepEqual(timer.markFirstRowVisible(), {
    cacheState: "cold",
    navigationMs: 520,
    requestMs: 280,
    normalizeRenderMs: 100,
    totalMs: 900,
    budgetMs: REPORT_FIRST_DATA_BUDGET_MS.cold,
    meetsFirstDataBudget: true,
  });
});

test("冷启动首数据 1,999ms 通过 2 秒预算，并输出三段耗时", () => {
  let now = 100;
  const timer = new ReportLoadPerformanceTimer(() => now);

  timer.start("cold");
  now = 1_700;
  timer.markDataNormalized();
  now = 2_099;
  const measurement = timer.markFirstRowVisible();

  assert.deepEqual(measurement, {
    cacheState: "cold",
    navigationMs: 0,
    requestMs: 1_600,
    normalizeRenderMs: 399,
    totalMs: 1_999,
    budgetMs: REPORT_FIRST_DATA_BUDGET_MS.cold,
    meetsFirstDataBudget: true,
  });
});

test("冷启动首数据 2,001ms 超出 2 秒预算", () => {
  let now = 0;
  const timer = new ReportLoadPerformanceTimer(() => now);

  timer.start("cold");
  now = 1_700;
  timer.markDataNormalized();
  now = 2_001;
  const measurement = timer.markFirstRowVisible();

  assert.equal(measurement?.totalMs, 2_001);
  assert.equal(measurement?.budgetMs, 2_000);
  assert.equal(measurement?.meetsFirstDataBudget, false);
});

test("热缓存使用 500ms 预算，499ms 通过而 501ms 不通过", () => {
  let now = 10;
  const timer = new ReportLoadPerformanceTimer(() => now);

  timer.start("warm");
  now = 300;
  timer.markDataNormalized();
  now = 509;
  assert.deepEqual(timer.markFirstRowVisible(), {
    cacheState: "warm",
    navigationMs: 0,
    requestMs: 290,
    normalizeRenderMs: 209,
    totalMs: 499,
    budgetMs: REPORT_FIRST_DATA_BUDGET_MS.warm,
    meetsFirstDataBudget: true,
  });

  timer.start("warm");
  now = 350;
  timer.markDataNormalized();
  now = 1_010;
  const overBudget = timer.markFirstRowVisible();

  assert.equal(overBudget?.totalMs, 501);
  assert.equal(overBudget?.meetsFirstDataBudget, false);
});

test("数据归一化完成和首行可见都只接受一次", () => {
  let now = 0;
  const timer = new ReportLoadPerformanceTimer(() => now);

  timer.start("cold");
  now = 120;
  timer.markDataNormalized();
  now = 300;
  timer.markDataNormalized();
  now = 450;
  const firstCompletion = timer.markFirstRowVisible();
  now = 600;

  assert.equal(firstCompletion?.requestMs, 120);
  assert.equal(firstCompletion?.normalizeRenderMs, 330);
  assert.equal(timer.markFirstRowVisible(), null);
});

test("错误、取消和缺少数据归一化标记都不能误报成功", () => {
  let now = 0;
  const timer = new ReportLoadPerformanceTimer(() => now);

  timer.start("cold");
  now = 100;
  assert.equal(timer.markFirstRowVisible(), null);

  timer.start("cold");
  now = 200;
  timer.markDataNormalized();
  timer.fail();
  now = 300;
  assert.equal(timer.markFirstRowVisible(), null);

  timer.start("warm");
  now = 400;
  timer.markDataNormalized();
  timer.cancel();
  now = 500;
  assert.equal(timer.markFirstRowVisible(), null);
});

test("时钟回拨不会产生负数耗时", () => {
  let now = 1_000;
  const timer = new ReportLoadPerformanceTimer(() => now);

  timer.start("warm");
  now = 900;
  timer.markDataNormalized();
  now = 800;
  const measurement = timer.markFirstRowVisible();

  assert.equal(measurement?.requestMs, 0);
  assert.equal(measurement?.normalizeRenderMs, 0);
  assert.equal(measurement?.totalMs, 0);
});

test("性能事件只包含报告标识与耗时，不携带业务数据", () => {
  const captured: unknown[] = [];
  const event = recordReportLoadPerformance(
    "revenue",
    {
      cacheState: "cold",
      navigationMs: 0,
      requestMs: 1_200,
      normalizeRenderMs: 80,
      totalMs: 1_280,
      budgetMs: 2_000,
      meetsFirstDataBudget: true,
    },
    (payload) => captured.push(payload),
  );

  assert.deepEqual(captured, [event]);
  assert.deepEqual(event, {
    event: "report_first_data",
    report: "revenue",
    cacheState: "cold",
    navigationMs: 0,
    requestMs: 1_200,
    normalizeRenderMs: 80,
    totalMs: 1_280,
    budgetMs: 2_000,
    meetsFirstDataBudget: true,
  });
});

test("性能事件写入应用日志时只保留固定性能字段并主动移除账号上下文", () => {
  const event: ReportLoadPerformanceEvent = {
    event: "report_first_data" as const,
    report: "supplier-branches",
    cacheState: "cold" as const,
    navigationMs: 0,
    requestMs: 1_300,
    normalizeRenderMs: 120,
    totalMs: 1_420,
    budgetMs: 2_000,
    meetsFirstDataBudget: true,
  };

  assert.deepEqual(buildReportLoadApplicationLog(event), {
    level: "Information",
    message: "报表首条数据可见耗时",
    sourceType: "mobile.report.performance",
    category: "ReportPerformance",
    userId: "",
    userName: "",
    properties: event,
  });
});

test("超出预算时写 Warning，并允许测试注入应用日志上报器", () => {
  const applicationLogs: unknown[] = [];
  const measurement = {
    cacheState: "warm" as const,
    navigationMs: 0,
    requestMs: 450,
    normalizeRenderMs: 80,
    totalMs: 530,
    budgetMs: 500,
    meetsFirstDataBudget: false,
  };

  const event = recordReportLoadPerformance(
    "product-branches",
    measurement,
    () => undefined,
    (input) => applicationLogs.push(input),
  );

  assert.deepEqual(applicationLogs, [{
    level: "Warning",
    message: "报表首条数据可见耗时",
    sourceType: "mobile.report.performance",
    category: "ReportPerformance",
    userId: "",
    userName: "",
    properties: event,
  }]);
});

test("营业额同 key 刷新保留已可见业务行且每次请求只完成一次", () => {
  let now = 0;
  const timer = new ReportLoadPerformanceTimer(() => now);
  let branchRowVisible = true;

  timer.start("cold");
  now = 800;
  timer.markDataNormalized();
  now = 850;
  const initialCompletion = branchRowVisible ? timer.markFirstRowVisible() : null;
  assert.equal(initialCompletion?.cacheState, "cold");
  assert.equal(timer.markFirstRowVisible(), null, "初次查询不得重复完成");

  // 同 query key 的真实 refetch 不会卸载现有行；新数据即使结构相同也要靠 dataUpdatedAt 完成 warm 会话。
  timer.start("warm");
  now = 1_050;
  timer.markDataNormalized();
  now = 1_080;
  const refetchCompletion = branchRowVisible ? timer.markFirstRowVisible() : null;
  assert.equal(refetchCompletion?.cacheState, "warm");
  assert.equal(refetchCompletion?.totalMs, 230);
  assert.equal(timer.markFirstRowVisible(), null, "同 key 刷新不得产生第二个完成事件");

  branchRowVisible = false;
  timer.start("warm");
  now = 1_200;
  timer.markDataNormalized();
  assert.equal(branchRowVisible ? timer.markFirstRowVisible() : null, null);
  timer.cancel();
});

test("弹窗必须同时满足数据归一化、首行可见和展示动画完成", () => {
  let now = 0;
  const gate = new ReportLoadVisibilityGate(new ReportLoadPerformanceTimer(() => now));

  gate.start("cold");
  now = 120;
  assert.equal(gate.markDataNormalized(), null);
  now = 180;
  assert.equal(gate.setFirstRowVisible(true), null);
  now = 360;
  assert.deepEqual(gate.setPresentationReady(true), {
    cacheState: "cold",
    navigationMs: 0,
    requestMs: 120,
    normalizeRenderMs: 240,
    totalMs: 360,
    budgetMs: 2_000,
    meetsFirstDataBudget: true,
  });
});

test("同 key warm 刷新保留展示与可见状态，新 key 会重置两项门禁", () => {
  let now = 0;
  const gate = new ReportLoadVisibilityGate(new ReportLoadPerformanceTimer(() => now));

  gate.start("cold");
  gate.setFirstRowVisible(true);
  gate.setPresentationReady(true);
  now = 100;
  assert.equal(gate.markDataNormalized()?.totalMs, 100);

  gate.start("warm", { preserveVisibility: true, preservePresentation: true });
  now = 280;
  assert.equal(gate.markDataNormalized()?.totalMs, 180, "同 key 不依赖不会重触发的 onLayout/viewability");

  gate.start("warm");
  now = 350;
  assert.equal(gate.markDataNormalized(), null);
  assert.equal(gate.setPresentationReady(true), null);
  now = 390;
  assert.equal(gate.setFirstRowVisible(true)?.totalMs, 110, "新 key 必须重新通过三重门禁");
});

test("失败和取消会清除弹窗展示与首行可见状态", () => {
  let now = 0;
  const gate = new ReportLoadVisibilityGate(new ReportLoadPerformanceTimer(() => now));

  gate.start("cold");
  gate.setFirstRowVisible(true);
  gate.setPresentationReady(true);
  gate.fail();
  gate.start("warm", { preserveVisibility: true, preservePresentation: true });
  now = 50;
  assert.equal(gate.markDataNormalized(), null);
  gate.cancel();
  assert.equal(gate.setPresentationReady(true), null);
  assert.equal(gate.setFirstRowVisible(true), null);
});

test("失败后同 key 重试可从独立物理状态恢复三重门禁", () => {
  let now = 0;
  const gate = new ReportLoadVisibilityGate(new ReportLoadPerformanceTimer(() => now));
  const physicalState = {
    firstRowVisible: true,
    presentationReady: true,
  } as const;

  gate.start("cold");
  gate.setFirstRowVisible(physicalState.firstRowVisible);
  gate.setPresentationReady(physicalState.presentationReady);
  gate.fail();

  now = 100;
  gate.start("warm", { restorePhysicalState: physicalState });
  now = 280;

  assert.deepEqual(gate.markDataNormalized(), {
    cacheState: "warm",
    navigationMs: 0,
    requestMs: 180,
    normalizeRenderMs: 0,
    totalMs: 180,
    budgetMs: 500,
    meetsFirstDataBudget: true,
  });
});

test("成功缓存经历 refetch error 后，同 key 重试必须改按 cold 预算分类", () => {
  const completeRows = { isComplete: true, rows: [{ id: "row-1" }] };
  const isUsable = (value: typeof completeRows) => value.isComplete && value.rows.length > 0;
  const classify = (status: string) => (
    hasUsableSuccessfulReportCache(status, completeRows, isUsable) ? "warm" : "cold"
  );

  assert.deepEqual(
    ["success", "error", "error"].map(classify),
    ["warm", "cold", "cold"],
    "成功缓存、refetch error、同 key retry 必须依次分类为 warm、cold、cold",
  );
  assert.equal(
    hasUsableSuccessfulReportCache("success", { isComplete: true, rows: [] }, isUsable),
    false,
  );
  assert.equal(hasUsableSuccessfulReportCache("pending", undefined, isUsable), false);
});
