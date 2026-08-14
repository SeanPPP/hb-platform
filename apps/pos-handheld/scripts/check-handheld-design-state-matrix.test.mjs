import assert from "node:assert/strict";
import { existsSync, readFileSync, readdirSync } from "node:fs";

const appRoot = new URL("../", import.meta.url);
const matrixUrl = new URL(
  "test-fixtures/handheld-design/state-matrix.json",
  appRoot,
);
const matrix = JSON.parse(readFileSync(matrixUrl, "utf8"));

function listProductionScreens(directoryUrl) {
  if (!existsSync(directoryUrl)) {
    return [];
  }

  return readdirSync(directoryUrl, { withFileTypes: true }).flatMap((entry) => {
    const entryUrl = new URL(`${entry.name}${entry.isDirectory() ? "/" : ""}`, directoryUrl);
    if (entry.isDirectory()) {
      return listProductionScreens(entryUrl);
    }
    if (!entry.name.endsWith(".tsx") || /\.(?:rntl\.)?test\.tsx$/u.test(entry.name)) {
      return [];
    }
    return [{ path: entryUrl.pathname, source: readFileSync(entryUrl, "utf8") }];
  });
}

const productionScreens = [
  ...listProductionScreens(new URL("app/", appRoot)),
  ...listProductionScreens(new URL("src/features/", appRoot)),
  ...listProductionScreens(new URL("src/ui/screens/", appRoot)),
];

assert.equal(matrix.source, "docs/design/prompt-set.md");
assert.equal(existsSync(new URL(matrix.source, appRoot)), true);
assert.deepEqual(matrix.palette, {
  background: "#F4F1EA",
  surface: "#FFFFFF",
  text: "#10253A",
  primary: "#E65A2F",
});
assert.deepEqual(matrix.rules, {
  layout: "single-column-portrait",
  spacing: 8,
  minimumControlHeight: 48,
  bottomTabs: false,
  customerDisplay: false,
  logo: false,
  watermark: false,
  gradients: false,
  glass: false,
});
assert.equal(matrix.states.length, 46);
assert.deepEqual(
  matrix.states.map((state) => state.id),
  Array.from({ length: 46 }, (_, index) => String(index + 1).padStart(2, "0")),
);
assert.equal(new Set(matrix.states.map((state) => state.slug)).size, 46);
assert.equal(matrix.states.filter((state) => state.pda === true).length, 4);
assert.deepEqual(
  matrix.states.filter((state) => state.pda === true).map((state) => state.id),
  ["43", "44", "45", "46"],
);

for (const state of matrix.states) {
  assert.match(state.title, /\S/u, `${state.id} 缺少标题`);
  assert.match(state.group, /\S/u, `${state.id} 缺少业务分组`);
  assert.match(state.route, /^\/[a-z-]*$/u, `${state.id} route 非法`);
  assert.ok(
    ["route", "modal", "state"].includes(state.surface),
    `${state.id} surface 非法`,
  );
  assert.equal(
    /customer[- ]display|external[- ]display|客显/iu.test(
      `${state.slug} ${state.title} ${state.group}`,
    ),
    false,
    `${state.id} 不得包含客显`,
  );

  const routePath =
    state.route === "/"
      ? "app/index.tsx"
      : `app/${state.route.slice(1)}.tsx`;
  assert.equal(
    existsSync(new URL(routePath, appRoot)),
    true,
    `${state.id} 缺少真实路由 ${state.route}`,
  );

  const stateOwners = productionScreens.filter(
    ({ source }) =>
      source.includes("HandheldStateSurface") &&
      (source.includes(`\"${state.slug}\"`) || source.includes(`'${state.slug}'`)),
  );
  assert.ok(
    stateOwners.length > 0,
    `${state.id} ${state.slug} 尚未绑定真实 HandheldStateSurface`,
  );
}

console.log("pos-handheld 46-state design contract: ok");
