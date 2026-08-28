import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { existsSync, readFileSync, readdirSync } from "node:fs";
import { builtinModules } from "node:module";
import { dirname, extname, join, normalize as normalizePath, relative, resolve } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import ts from "typescript";

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const migrationState = JSON.parse(
  readFileSync(
    join(repositoryRoot, "scripts", "pos-shared", "pos-shared-migration-state.json"),
    "utf8",
  ),
);
const ownership = JSON.parse(
  readFileSync(
    join(repositoryRoot, "scripts", "pos-shared", "pos-shared-ownership.json"),
    "utf8",
  ),
);

const packageRules = {
  "pos-domain": [],
  "pos-api-client": ["pos-domain"],
  "pos-db": ["pos-domain"],
  "pos-sync": ["pos-domain", "pos-api-client", "pos-db"],
  "pos-payments-core": ["pos-domain"],
  "pos-receipt-core": ["pos-domain"],
  "pos-testing": [
    "pos-domain",
    "pos-api-client",
    "pos-db",
    "pos-sync",
    "pos-payments-core",
    "pos-receipt-core",
  ],
};
const forbiddenRuntimeImports = [
  /^@\//,
  /(^|\/)apps\//,
  /^react(?:\/|$)/,
  /^react-native(?:\/|$)/,
  /^expo(?:-|\/|$)/,
  /^@expo\//,
];
const canonicalPrinter = ts.createPrinter({
  newLine: ts.NewLineKind.LineFeed,
  removeComments: true,
});

function walk(root, result = []) {
  if (!existsSync(root)) return result;
  for (const entry of readdirSync(root, { withFileTypes: true })) {
    const path = join(root, entry.name);
    if (entry.isDirectory()) walk(path, result);
    else result.push(path);
  }
  return result;
}

function importsOf(source) {
  const matches = source.matchAll(
    /(?:import|export)\s+(?:type\s+)?(?:[^"']*?\s+from\s+)?["']([^"']+)["']|import\(["']([^"']+)["']\)|require\(["']([^"']+)["']\)/g,
  );
  return [...matches].map((match) => match[1] ?? match[2] ?? match[3]);
}

function packageNameOf(specifier) {
  if (specifier.startsWith("@")) {
    return specifier.split("/").slice(0, 2).join("/");
  }
  return specifier.split("/")[0];
}

function sourceFileOf(source, path) {
  const scriptKind = path.endsWith(".tsx")
    ? ts.ScriptKind.TSX
    : path.endsWith(".jsx")
      ? ts.ScriptKind.JSX
      : path.endsWith(".js") || path.endsWith(".mjs") || path.endsWith(".cjs")
        ? ts.ScriptKind.JS
        : ts.ScriptKind.TS;
  const sourceFile = ts.createSourceFile(
    path,
    source,
    ts.ScriptTarget.Latest,
    true,
    scriptKind,
  );
  assert.equal(sourceFile.parseDiagnostics.length, 0, `${path} 无法解析`);
  return sourceFile;
}

function importedBindingsOf(sourceFile) {
  const bindings = [];
  for (const statement of sourceFile.statements) {
    if (!ts.isImportDeclaration(statement)) continue;
    const clause = statement.importClause;
    assert.ok(clause, `${sourceFile.fileName} 禁止用副作用 import 迁移共享源码`);

    if (clause.name) {
      bindings.push(`${clause.isTypeOnly ? "type" : "value"}:default:${clause.name.text}`);
    }

    if (!clause.namedBindings) continue;
    if (ts.isNamespaceImport(clause.namedBindings)) {
      bindings.push(
        `${clause.isTypeOnly ? "type" : "value"}:namespace:${clause.namedBindings.name.text}`,
      );
    } else if (ts.isNamedImports(clause.namedBindings)) {
      for (const element of clause.namedBindings.elements) {
        const importedName = element.propertyName?.text ?? element.name.text;
        const kind = clause.isTypeOnly || element.isTypeOnly ? "type" : "value";
        bindings.push(`${kind}:named:${importedName}:${element.name.text}`);
      }
    }
  }
  return bindings.sort();
}

function canonicalModuleSpecifier(specifier, importerPath) {
  if (!specifier.startsWith(".")) return specifier;
  return normalizePath(join(dirname(importerPath), specifier)).replaceAll("\\", "/");
}

function moduleReferencesOf(sourceFile) {
  const references = [];
  for (const statement of sourceFile.statements) {
    if (ts.isImportDeclaration(statement)) {
      const module = canonicalModuleSpecifier(
        statement.moduleSpecifier.text,
        sourceFile.fileName,
      );
      const clause = statement.importClause;
      if (!clause) {
        references.push(`import:side-effect:${module}`);
        continue;
      }
      if (clause.name) {
        references.push(
          `import:${clause.isTypeOnly ? "type" : "value"}:default:${clause.name.text}:${module}`,
        );
      }
      if (!clause.namedBindings) continue;
      if (ts.isNamespaceImport(clause.namedBindings)) {
        references.push(
          `import:${clause.isTypeOnly ? "type" : "value"}:namespace:${clause.namedBindings.name.text}:${module}`,
        );
      } else if (ts.isNamedImports(clause.namedBindings)) {
        for (const element of clause.namedBindings.elements) {
          const importedName = element.propertyName?.text ?? element.name.text;
          const kind = clause.isTypeOnly || element.isTypeOnly ? "type" : "value";
          references.push(
            `import:${kind}:named:${importedName}:${element.name.text}:${module}`,
          );
        }
      }
      continue;
    }

    if (
      ts.isExportDeclaration(statement) &&
      statement.moduleSpecifier &&
      ts.isStringLiteral(statement.moduleSpecifier)
    ) {
      const module = canonicalModuleSpecifier(
        statement.moduleSpecifier.text,
        sourceFile.fileName,
      );
      if (!statement.exportClause) {
        references.push(`export:${statement.isTypeOnly ? "type" : "value"}:*:${module}`);
      } else if (ts.isNamespaceExport(statement.exportClause)) {
        references.push(
          `export:${statement.isTypeOnly ? "type" : "value"}:namespace:${statement.exportClause.name.text}:${module}`,
        );
      } else {
        for (const element of statement.exportClause.elements) {
          const localName = element.propertyName?.text ?? element.name.text;
          const kind = statement.isTypeOnly || element.isTypeOnly ? "type" : "value";
          references.push(`export:${kind}:named:${localName}:${element.name.text}:${module}`);
        }
      }
    }
  }

  const visit = (node) => {
    if (
      ts.isCallExpression(node) &&
      node.arguments.length === 1 &&
      ts.isStringLiteral(node.arguments[0]) &&
      (
        node.expression.kind === ts.SyntaxKind.ImportKeyword ||
        (ts.isIdentifier(node.expression) && node.expression.text === "require")
      )
    ) {
      const kind = node.expression.kind === ts.SyntaxKind.ImportKeyword
        ? "dynamic-import"
        : "require";
      references.push(
        `${kind}:${canonicalModuleSpecifier(node.arguments[0].text, sourceFile.fileName)}`,
      );
    }
    ts.forEachChild(node, visit);
  };
  ts.forEachChild(sourceFile, visit);
  return references.sort();
}

function canonicalModuleBody(sourceFile) {
  const withoutImports = ts.factory.updateSourceFile(
    sourceFile,
    sourceFile.statements.filter((statement) => !ts.isImportDeclaration(statement)),
  );
  const normalizeModuleSpecifiers = (context) => {
    const visit = (node) => {
      if (
        ts.isExportDeclaration(node) &&
        node.moduleSpecifier &&
        ts.isStringLiteral(node.moduleSpecifier)
      ) {
        return ts.factory.updateExportDeclaration(
          node,
          node.modifiers,
          node.isTypeOnly,
          node.exportClause,
          ts.factory.createStringLiteral("__MODULE__"),
          node.attributes,
        );
      }
      if (
        ts.isCallExpression(node) &&
        node.arguments.length === 1 &&
        ts.isStringLiteral(node.arguments[0]) &&
        (
          node.expression.kind === ts.SyntaxKind.ImportKeyword ||
          (ts.isIdentifier(node.expression) && node.expression.text === "require")
        )
      ) {
        return ts.factory.updateCallExpression(
          node,
          node.expression,
          node.typeArguments,
          [ts.factory.createStringLiteral("__MODULE__")],
        );
      }
      return ts.visitEachChild(node, visit, context);
    };
    return (root) => ts.visitNode(root, visit);
  };
  const transformed = ts.transform(withoutImports, [normalizeModuleSpecifiers]);
  try {
    return canonicalPrinter.printFile(transformed.transformed[0]);
  } finally {
    transformed.dispose();
  }
}

function canonicalSource(source, path, { includeModuleSources = true } = {}) {
  const sourceFile = sourceFileOf(source, path);
  const canonical = {
    imports: importedBindingsOf(sourceFile),
    module: canonicalModuleBody(sourceFile),
  };
  if (includeModuleSources) {
    canonical.moduleReferences = moduleReferencesOf(sourceFile);
  }
  return JSON.stringify(canonical);
}

function canonicalRuntimeSource(source, path, options) {
  const transpiled = ts.transpileModule(source, {
    compilerOptions: {
      jsx: ts.JsxEmit.ReactJSX,
      module: ts.ModuleKind.ESNext,
      removeComments: true,
      target: ts.ScriptTarget.ES2022,
      verbatimModuleSyntax: true,
    },
    fileName: path,
  });
  return canonicalSource(transpiled.outputText, `${path}.js`, options);
}

function baselineSource(path) {
  return execFileSync(
    "git",
    ["show", `${migrationState.baseline}:apps/pos-ipad/src/${path}`],
    { cwd: repositoryRoot, encoding: "utf8" },
  );
}

function sha256(value) {
  return createHash("sha256").update(value).digest("hex");
}

function matchesDeclaredGlob(path, pattern) {
  const globstarDirectory = "\u0000";
  const globstar = "\u0001";
  const star = "\u0002";
  const expression = pattern
    .replaceAll("**/", globstarDirectory)
    .replaceAll("**", globstar)
    .replaceAll("*", star)
    .replace(/[.*+?^${}()|[\]\\]/g, "\\$&")
    .replaceAll(globstarDirectory, "(?:.*/)?")
    .replaceAll(globstar, ".*")
    .replaceAll(star, "[^/]*");
  return new RegExp(`^${expression}$`, "u").test(path);
}

test("行为测试登记的 glob 同时支持单层与跨目录匹配", () => {
  assert.equal(
    matchesDeclaredGlob("src/core/sync/sync-coordinator.test.ts", "src/**/*.test.ts"),
    true,
  );
  assert.equal(
    matchesDeclaredGlob("src/sync-coordinator.test.ts", "src/**/*.test.ts"),
    true,
  );
  assert.equal(
    matchesDeclaredGlob("src/core/sync/sync-coordinator.ts", "src/**/*.test.ts"),
    false,
  );
});

test("共享包依赖方向与 package.json 一致", () => {
  for (const [packageName, allowed] of Object.entries(packageRules)) {
    const packageRoot = join(repositoryRoot, "packages", packageName);
    const packageJson = JSON.parse(readFileSync(join(packageRoot, "package.json"), "utf8"));
    const declared = Object.keys(packageJson.dependencies ?? {})
      .filter((name) => name.startsWith("@hb/pos-"))
      .map((name) => name.slice("@hb/".length))
      .sort();
    assert.deepEqual(declared, [...allowed].sort(), `${packageName} 的共享包依赖方向漂移`);
  }
});

test("共享包源码不反向导入 App、React Native、Expo 或未允许共享包", () => {
  const violations = [];
  for (const [packageName, allowed] of Object.entries(packageRules)) {
    const sourceRoot = join(repositoryRoot, "packages", packageName, "src");
    for (const path of walk(sourceRoot).filter((file) =>
      [".ts", ".tsx", ".js", ".mjs", ".cjs"].includes(extname(file))
    )) {
      for (const specifier of importsOf(readFileSync(path, "utf8"))) {
        if (forbiddenRuntimeImports.some((pattern) => pattern.test(specifier))) {
          violations.push(`${relative(repositoryRoot, path)} -> ${specifier}`);
        }
        if (specifier.startsWith("@hb/pos-")) {
          const dependency = specifier.split("/").slice(0, 2).join("/").slice("@hb/".length);
          if (!allowed.includes(dependency)) {
            violations.push(`${relative(repositoryRoot, path)} -> ${specifier}`);
          }
        }
      }
    }
  }
  assert.deepEqual(violations, []);
});

test("OTA 状态机保持 App-local，共享包只持有纯操作租约合同", () => {
  for (const packageName of Object.keys(packageRules)) {
    assert.equal(
      existsSync(join(repositoryRoot, "packages", packageName, "src", "features", "app-updates")),
      false,
      `${packageName} 不得提前吸收第 11 项 OTA 状态机`,
    );
  }

  const extracted = migrationState.extractedContracts ?? [];
  const updateLeaseContract = extracted.find(
    ({ path, package: packageName }) =>
      packageName === "pos-domain" && path === "core/contracts/update-operation-lease.ts",
  );
  assert.deepEqual(
    [updateLeaseContract?.package, updateLeaseContract?.path],
    ["pos-domain", "core/contracts/update-operation-lease.ts"],
  );
  const contract = readFileSync(
    join(repositoryRoot, "packages", "pos-domain", "src", updateLeaseContract.path),
    "utf8",
  );
  assert.match(contract, /runOperation<T>\(operation: \(\) => T \| Promise<T>\): Promise<T>/u);
  assert.doesNotMatch(contract, /runTransition|transitionActive|\bexpo\b|react-native/iu);

  for (const app of ["pos-ipad", "pos-handheld"]) {
    const appCoordinator = join(
      repositoryRoot,
      "apps",
      app,
      "src",
      "features",
      "app-updates",
      "update-transition-lease-coordinator.ts",
    );
    assert.equal(existsSync(appCoordinator), true, `${app} 必须保留 OTA 状态机真源`);
    assert.match(readFileSync(appCoordinator, "utf8"), /class UpdateTransitionLeaseCoordinator/u);
  }
});

test("双端同步材料错误共享同一个构造器真源", () => {
  const extracted = migrationState.extractedContracts ?? [];
  assert.equal(
    extracted.some(
      ({ path, package: packageName, sourcePath }) =>
        packageName === "pos-db" &&
        path === "core/db/order-sync-material-contract.ts" &&
        sourcePath === "core/db/sqlite-order-sync-material.ts",
    ),
    true,
  );

  for (const app of ["pos-ipad", "pos-handheld"]) {
    const source = readFileSync(
      join(repositoryRoot, "apps", app, "src", "core", "db", "sqlite-order-sync-material.ts"),
      "utf8",
    );
    assert.match(source, /from "@hb\/pos-db\/core\/db\/order-sync-material-contract"/u);
    assert.doesNotMatch(source, /export class OrderSyncMaterialError extends Error/u);
  }
});

test("共享包生产源码显式声明所有第三方运行时依赖", () => {
  const builtins = new Set([
    ...builtinModules,
    ...builtinModules.map((name) => `node:${name}`),
  ]);
  const violations = [];

  for (const packageName of Object.keys(packageRules)) {
    const packageRoot = join(repositoryRoot, "packages", packageName);
    const packageJson = JSON.parse(
      readFileSync(join(packageRoot, "package.json"), "utf8"),
    );
    const declared = new Set(Object.keys(packageJson.dependencies ?? {}));
    const sourceRoot = join(packageRoot, "src");

    for (const path of walk(sourceRoot).filter((file) =>
      [".ts", ".tsx", ".js", ".mjs", ".cjs"].includes(extname(file)) &&
      !/(?:\.test|\.spec|\.rntl)\.[cm]?[jt]sx?$/u.test(file)
    )) {
      for (const specifier of importsOf(readFileSync(path, "utf8"))) {
        if (
          specifier.startsWith(".") ||
          specifier.startsWith("/") ||
          specifier.startsWith("@hb/pos-") ||
          builtins.has(specifier)
        ) {
          continue;
        }

        const dependency = packageNameOf(specifier);
        if (!declared.has(dependency)) {
          violations.push(
            `${relative(repositoryRoot, path)} -> ${specifier}（缺少 ${dependency} dependencies）`,
          );
        }
      }
    }
  }

  assert.deepEqual(violations, []);
});

test("迁移语义规范化保留模块来源身份", () => {
  const original = `
    import { resolvePayment } from "./original-payment";
    export const run = () => resolvePayment();
  `;
  const redirected = original.replace("./original-payment", "./different-payment");

  assert.notEqual(
    canonicalSource(original, "features/payments/example.ts"),
    canonicalSource(redirected, "features/payments/example.ts"),
  );
  assert.notEqual(
    canonicalRuntimeSource(original, "features/payments/example.ts"),
    canonicalRuntimeSource(redirected, "features/payments/example.ts"),
  );
});

test("已迁移共享源码保持固定基线语义，显式类型适配受哈希与行为测试锁定", () => {
  const runtimeAdaptations = new Map(
    (migrationState.runtimeAdaptations ?? []).map((entry) => [entry.path, entry]),
  );
  assert.equal(
    runtimeAdaptations.size,
    (migrationState.runtimeAdaptations ?? []).length,
    "运行时适配路径不得重复",
  );
  const adaptations = new Map(
    (migrationState.semanticAdaptations ?? []).map((entry) => [entry.path, entry]),
  );
  assert.equal(
    adaptations.size,
    (migrationState.semanticAdaptations ?? []).length,
    "语义适配路径不得重复",
  );

  const entries = [
    ...Object.entries(migrationState.migratedPaths).flatMap(([packageName, paths]) =>
      paths.map((path) => ({ packageName, path })),
    ),
    ...(migrationState.reconciledPaths ?? []).map((entry) => ({
      packageName: entry.package,
      path: entry.path,
    })),
  ];
  const visitedRuntimeAdaptations = new Set();
  const visitedAdaptations = new Set();
  const testAdaptations = new Map(
    (migrationState.testAdaptations ?? []).map((entry) => [entry.path, entry]),
  );
  const visitedTestAdaptations = new Set();
  const reviewedModuleTopology = [];

  for (const { packageName, path } of entries) {
    const baseline = baselineSource(path);
    const sharedPath = join(repositoryRoot, "packages", packageName, "src", path);
    const current = readFileSync(sharedPath, "utf8");
    reviewedModuleTopology.push({
      package: packageName,
      path,
      references: moduleReferencesOf(sourceFileOf(current, path)),
    });
    const currentRuntimeShape = canonicalRuntimeSource(current, path, {
      includeModuleSources: false,
    });
    const baselineRuntimeShape = canonicalRuntimeSource(baseline, path, {
      includeModuleSources: false,
    });
    const runtimeMatches = currentRuntimeShape === baselineRuntimeShape;
    const runtimeAdaptation = runtimeAdaptations.get(path);
    const testAdaptation = testAdaptations.get(path);
    if (!runtimeMatches) {
      if (/\.test\.[cm]?[jt]sx?$/u.test(path)) {
        assert.ok(testAdaptation, `${path} 的测试运行时语义相对固定基线发生变化`);
        assert.equal(testAdaptation.package, packageName, `${path} 的测试适配包归属错误`);
        assert.ok(testAdaptation.reason.length >= 12, `${path} 缺少测试适配原因`);
        assert.ok(
          current.includes(testAdaptation.requiredAssertion),
          `${path} 缺少登记的行为断言`,
        );
        visitedTestAdaptations.add(path);
        continue;
      }

      assert.ok(runtimeAdaptation, `${path} 的运行时语义相对固定基线发生变化`);
      assert.equal(runtimeAdaptation.package, packageName, `${path} 的运行时适配包归属错误`);
      assert.ok(runtimeAdaptation.reason.length >= 12, `${path} 缺少运行时适配原因`);
      assert.equal(
        runtimeAdaptation.baselineCanonicalRuntimeSha256,
        sha256(baselineRuntimeShape),
        `${path} 的固定运行时基线发生漂移，必须重新审查`,
      );
      assert.equal(
        runtimeAdaptation.canonicalRuntimeSha256,
        sha256(currentRuntimeShape),
        `${path} 的显式运行时适配发生漂移，必须重新审查`,
      );
      assert.equal(
        runtimeAdaptation.canonicalTypeSha256,
        sha256(canonicalSource(current, path, { includeModuleSources: false })),
        `${path} 的显式运行时类型合同发生漂移，必须重新审查`,
      );
      assert.ok(runtimeAdaptation.behaviorTests.length > 0, `${path} 缺少行为测试登记`);
      for (const verification of runtimeAdaptation.behaviorTests) {
        const workspaceRoot = join(repositoryRoot, verification.workspace);
        const packageJson = JSON.parse(
          readFileSync(join(workspaceRoot, "package.json"), "utf8"),
        );
        assert.equal(
          existsSync(join(workspaceRoot, verification.path)),
          true,
          `${path} 的行为测试不存在：${verification.workspace}/${verification.path}`,
        );
        assert.ok(
          matchesDeclaredGlob(verification.path, verification.pattern),
          `${verification.path} 不在登记的测试模式 ${verification.pattern} 中`,
        );
        assert.ok(
          packageJson.scripts?.[verification.script]?.includes(verification.pattern),
          `${verification.workspace} 的 ${verification.script} 未运行 ${verification.pattern}`,
        );
        if (verification.script !== "test") {
          assert.ok(
            packageJson.scripts?.test?.includes(`npm run ${verification.script}`),
            `${verification.workspace} 默认测试未纳入 ${verification.script}`,
          );
        }
      }
      visitedRuntimeAdaptations.add(path);
      continue;
    }
    assert.equal(runtimeAdaptation, undefined, `${path} 已无需运行时适配登记`);
    assert.equal(testAdaptation, undefined, `${path} 已无需测试适配登记`);

    const baselineTypeShape = canonicalSource(baseline, path, {
      includeModuleSources: false,
    });
    const currentTypeShape = canonicalSource(current, path, {
      includeModuleSources: false,
    });
    const adaptation = adaptations.get(path);
    if (baselineTypeShape === currentTypeShape) {
      assert.equal(adaptation, undefined, `${path} 已无需语义适配登记`);
      continue;
    }

    assert.ok(adaptation, `${path} 含导入路径之外的类型变化，必须显式登记`);
    assert.equal(adaptation.package, packageName, `${path} 的语义适配包归属错误`);
    assert.ok(adaptation.reason.length >= 12, `${path} 缺少具体语义适配原因`);
    assert.equal(
      adaptation.canonicalTypeSha256,
      sha256(currentTypeShape),
      `${path} 的显式类型适配发生漂移，必须重新审查`,
    );
    assert.ok(adaptation.behaviorTests.length > 0, `${path} 缺少行为测试登记`);
    for (const verification of adaptation.behaviorTests) {
      const workspaceRoot = join(repositoryRoot, verification.workspace);
      const packageJson = JSON.parse(
        readFileSync(join(workspaceRoot, "package.json"), "utf8"),
      );
      assert.equal(
        existsSync(join(workspaceRoot, verification.path)),
        true,
        `${path} 的行为测试不存在：${verification.workspace}/${verification.path}`,
      );
      assert.ok(
        matchesDeclaredGlob(verification.path, verification.pattern),
        `${verification.path} 不在登记的测试模式 ${verification.pattern} 中`,
      );
      assert.ok(
        packageJson.scripts?.[verification.script]?.includes(verification.pattern),
        `${verification.workspace} 的 ${verification.script} 未运行 ${verification.pattern}`,
      );
      assert.ok(
        packageJson.scripts?.test?.includes(`npm run ${verification.script}`),
        `${verification.workspace} 默认测试未纳入 ${verification.script}`,
      );
    }
    visitedAdaptations.add(path);
  }

  reviewedModuleTopology.sort((left, right) =>
    `${left.package}:${left.path}`.localeCompare(`${right.package}:${right.path}`)
  );
  assert.equal(
    migrationState.reviewedModuleTopologySha256,
    sha256(JSON.stringify(reviewedModuleTopology)),
    "已迁移源码的模块来源发生漂移，必须重新审查并更新拓扑哈希",
  );

  assert.deepEqual(
    [...runtimeAdaptations.keys()].filter((path) => !visitedRuntimeAdaptations.has(path)),
    [],
    "存在未对应已迁移源码的运行时适配登记",
  );
  assert.deepEqual(
    [...adaptations.keys()].filter((path) => !visitedAdaptations.has(path)),
    [],
    "存在未对应已迁移源码的语义适配登记",
  );
  assert.deepEqual(
    [...testAdaptations.keys()].filter((path) => !visitedTestAdaptations.has(path)),
    [],
    "存在未对应已迁移测试的测试适配登记",
  );
});

test("已迁移路径只有共享包一个真源", () => {
  const manifestPaths = new Set(ownership.files.map(({ path }) => path));
  const seen = new Set();
  for (const [packageName, paths] of Object.entries(migrationState.migratedPaths)) {
    assert.ok(packageRules[packageName], `未知共享包 ${packageName}`);
    for (const path of paths) {
      assert.equal(seen.has(path), false, `${path} 被多个共享包声明`);
      seen.add(path);
      assert.equal(manifestPaths.has(path), true, `${path} 不在固定基线归属清单`);
      assert.equal(
        existsSync(join(repositoryRoot, "packages", packageName, "src", path)),
        true,
        `${path} 未出现在 ${packageName}`,
      );
      for (const app of ["pos-ipad", "pos-handheld"]) {
        assert.equal(
          existsSync(join(repositoryRoot, "apps", app, "src", path)),
          false,
          `${path} 迁移后仍残留在 ${app}`,
        );
      }
    }
  }
});

test("显式调和的分叉文件只允许注释或格式差异，并收敛为单一共享源", () => {
  const printer = ts.createPrinter({ removeComments: true });
  for (const entry of migrationState.reconciledPaths ?? []) {
    assert.ok(packageRules[entry.package], `${entry.path} 指向未知共享包`);
    assert.ok(entry.reason.length >= 12, `${entry.path} 缺少调和原因`);
    const variants = ["pos-ipad", "pos-handheld"].map((app) =>
      execFileSync(
        "git",
        [
          "show",
          `${migrationState.baseline}:apps/${app}/src/${entry.path}`,
        ],
        { cwd: repositoryRoot, encoding: "utf8" },
      )
    );
    assert.notEqual(variants[0], variants[1], `${entry.path} 并非基线分叉文件`);
    const normalized = variants.map((source, index) =>
      printer.printFile(
        ts.createSourceFile(
          `${entry.path}.${index}.ts`,
          source,
          ts.ScriptTarget.Latest,
          true,
        ),
      )
    );
    assert.equal(
      normalized[0],
      normalized[1],
      `${entry.path} 含注释/格式之外的行为或类型差异，禁止自动调和`,
    );
    assert.equal(
      existsSync(join(repositoryRoot, "packages", entry.package, "src", entry.path)),
      true,
      `${entry.path} 未进入 ${entry.package}`,
    );
    for (const app of ["pos-ipad", "pos-handheld"]) {
      assert.equal(
        existsSync(join(repositoryRoot, "apps", app, "src", entry.path)),
        false,
        `${entry.path} 调和后仍残留在 ${app}`,
      );
    }
  }
});

test("保留路径必须逐项记录不可共享原因", () => {
  const manifestPaths = new Set(ownership.files.map(({ path }) => path));
  const retained = migrationState.retainedPaths ?? [];
  const migrated = new Set(Object.values(migrationState.migratedPaths).flat());
  const reconciled = new Set(
    (migrationState.reconciledPaths ?? []).map(({ path }) => path),
  );
  const centralized = new Set(
    (migrationState.centralizedPaths ?? []).map(({ path }) => path),
  );
  assert.equal(new Set(retained.map(({ path }) => path)).size, retained.length);
  for (const entry of retained) {
    assert.equal(manifestPaths.has(entry.path), true, `${entry.path} 不在固定基线归属清单`);
    assert.ok(entry.reason.length >= 12, `${entry.path} 缺少具体保留原因`);
    assert.equal(migrated.has(entry.path), false, `${entry.path} 已迁移却仍在保留清单`);
    assert.equal(reconciled.has(entry.path), false, `${entry.path} 已调和却仍在保留清单`);
    assert.equal(centralized.has(entry.path), false, `${entry.path} 已集中却仍在保留清单`);
    for (const app of ["pos-ipad", "pos-handheld"]) {
      assert.equal(
        existsSync(join(repositoryRoot, "apps", app, "src", entry.path)),
        true,
        `${entry.path} 作为保留项却未出现在 ${app}`,
      );
    }
  }

  for (const entry of migrationState.centralizedPaths ?? []) {
    assert.equal(manifestPaths.has(entry.path), true, `${entry.path} 不在固定基线归属清单`);
    assert.equal(
      existsSync(join(repositoryRoot, "packages", entry.package, "src", entry.path)),
      true,
      `${entry.path} 未集中到 ${entry.package}`,
    );
    for (const app of ["pos-ipad", "pos-handheld"]) {
      assert.equal(
        existsSync(join(repositoryRoot, "apps", app, "src", entry.path)),
        false,
        `${entry.path} 集中后仍残留在 ${app}`,
      );
    }
  }

  const accounted = new Set([
    ...migrated,
    ...reconciled,
    ...centralized,
    ...retained.map(({ path }) => path),
  ]);
  const unaccounted = ownership.files
    .filter(({ owner, path }) => owner !== "app-local" && !accounted.has(path))
    .map(({ path }) => path);
  assert.deepEqual(unaccounted, [], "存在未迁移且未登记原因的共享归属路径");
});
