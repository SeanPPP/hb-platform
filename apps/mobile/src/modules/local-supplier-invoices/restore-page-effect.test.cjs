/* global __dirname */
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { createRequire } = require("node:module");
const ts = require("typescript");
const requireFromMobile = createRequire(path.resolve(__dirname, "../../../package.json"));
const rendererRequire = createRequire(requireFromMobile.resolve("test-renderer"));
const React = rendererRequire("react");
const { createRoot } = rendererRequire("test-renderer");

// 从真实页面源码提取两个 effect，在 test-renderer 中运行，避免只用静态正则测试实现细节。
const screenPath = path.resolve(__dirname, "../../../app/(shell)/local-supplier-invoices.tsx");
const source = fs.readFileSync(screenPath, "utf8");
const sourceFile = ts.createSourceFile(screenPath, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const effectTexts = [];
function collectEffects(node) {
  if (ts.isCallExpression(node) && node.expression.getText(sourceFile) === "useEffect") {
    effectTexts.push(node.getText(sourceFile));
  }
  ts.forEachChild(node, collectEffects);
}
collectEffects(sourceFile);
const debounceEffect = effectTexts.find((text) =>
  text.includes("setTimeout") && text.includes("setDetailSearchQuery")
);
const restoreEffect = effectTexts.find((text) =>
  text.includes("restoreState") && text.includes("returnDetailsPage")
);
assert.ok(debounceEffect && restoreEffect, "真实页面 debounce/restore effects must be present");

const harnessSource = `
const { useState, useRef, useEffect } = React;
function Harness({ restoreState, userSearch, forcePage, capture }) {
  const [detailsPage, setDetailsPage] = useState(1);
  const [detailSearch, setDetailSearch] = useState('');
  const [detailSearchQuery, setDetailSearchQuery] = useState('');
  const handledRestoreKeyRef = useRef(null);
  const pendingRestoreRef = useRef(null);
  const listRequestIdRef = useRef(0);
  const detailRequestIdRef = useRef(0);
  const pendingDetailAnchorRef = useRef(null);
  const initialStoreScopeAppliedRef = useRef(false);
  const setDraftFilters = () => {}, setFilters = () => {}, setSort = () => {}, setPageSize = () => {},
    setPage = () => {}, setSelectedInvoice = () => {}, setDetails = () => {}, setDetailsTotal = () => {},
    setDetailsPageSize = () => {}, setDetailPriceChangeFilter = () => {}, setInitialStoreScopeReady = () => {};
  const bindDeviceStore = (value) => value;
  const buildInvoiceListRequestKey = (value) => JSON.stringify(value);
  const buildLocalSupplierInvoicesReturnParams = (value) => value;
  ${debounceEffect}
  ${restoreEffect}
  useEffect(() => { if (userSearch !== undefined) setDetailSearch(userSearch); }, [userSearch]);
  useEffect(() => { if (forcePage !== undefined) setDetailsPage(forcePage); }, [forcePage]);
  capture({ detailsPage, detailSearch, detailSearchQuery });
  return null;
}
return Harness;`;

const Harness = new Function(
  "React", "capture", "source", "buildLocalSupplierInvoicesReturnParams",
  ts.transpile(harnessSource, { target: ts.ScriptTarget.ES2022 })
)(React, () => {}, () => {}, (value) => value);

function makeRestore(search) {
  return {
    returnInvoiceGuid: "invoice-1", returnDetailGuid: "detail-4",
    returnDetailsPage: 4, returnDetailsPageSize: 50,
    returnDetailPriceChangeFilter: "all", returnDetailSearch: search,
    returnListPage: 2, returnListPageSize: 20, filters: { storeCode: "S01" },
    sort: { colId: "OrderDate", direction: "desc" },
  };
}

async function renderWith(root, props, snapshots) {
  await React.act(async () => root.render(React.createElement(Harness, { ...props, capture: (v) => snapshots.push(v) })));
}

async function waitFor(ms) {
  await React.act(async () => new Promise((resolve) => setTimeout(resolve, ms)));
}

(async () => {
  globalThis.IS_REACT_ACT_ENVIRONMENT = true;
  for (const search of ["", "ITEM-4"]) {
    const snapshots = [];
    const root = createRoot();
    await renderWith(root, { restoreState: makeRestore(search) }, snapshots);
    await waitFor(420);
    assert.equal(snapshots.at(-1).detailsPage, 4, `restore keeps page 4 for ${JSON.stringify(search)}`);
    await React.act(async () => root.unmount());
  }

  const snapshots = [];
  const root = createRoot();
  await renderWith(root, { restoreState: makeRestore("ITEM-4") }, snapshots);
  await waitFor(420);
  await renderWith(root, { restoreState: null, userSearch: "OTHER" }, snapshots);
  await waitFor(420);
  assert.equal(snapshots.at(-1).detailsPage, 1, "changing search resets to page 1");
  await renderWith(root, { restoreState: null, userSearch: "OTHER", forcePage: 3 }, snapshots);
  await renderWith(root, { restoreState: null, userSearch: "OTHER" }, snapshots);
  await waitFor(420);
  assert.equal(snapshots.at(-1).detailsPage, 3, "same search term does not reset a manually selected page");

  await renderWith(root, { restoreState: null, userSearch: "" }, snapshots);
  await waitFor(420);
  assert.equal(snapshots.at(-1).detailsPage, 1, "clearing search resets to page 1");

  // 用户刚输入新词时，旧 query 尚未更新；立即恢复同一个词必须取消旧 timer。
  await renderWith(root, { restoreState: null, userSearch: "LATEST" }, snapshots);
  await renderWith(root, { restoreState: makeRestore("LATEST") }, snapshots);
  await waitFor(420);
  assert.equal(snapshots.at(-1).detailsPage, 4, "restore cancels the pending search debounce");
  assert.equal(snapshots.at(-1).detailSearchQuery, "LATEST", "restore keeps the restored query");
  await React.act(async () => root.unmount());
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
