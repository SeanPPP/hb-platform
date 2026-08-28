import { existsSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { dirname, extname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import ts from "typescript";

const mode = process.argv[2] ?? "--dry-run";
if (!["--dry-run", "--write", "--check"].includes(mode)) {
  throw new Error("用法: node rewrite-pos-app-migrated-imports.mjs [--dry-run|--write|--check]");
}

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const state = JSON.parse(
  readFileSync(
    join(repositoryRoot, "scripts", "pos-shared", "pos-shared-migration-state.json"),
    "utf8",
  ),
);
const targetByPath = new Map();
for (const [packageName, paths] of Object.entries(state.migratedPaths)) {
  for (const path of paths) targetByPath.set(path, { packageName });
}
for (const { package: packageName, path } of state.reconciledPaths ?? []) {
  targetByPath.set(path, { packageName });
}
for (const { package: packageName, path, specifier } of state.centralizedPaths ?? []) {
  targetByPath.set(path, { packageName, specifier });
}

function walk(root, result = []) {
  if (!existsSync(root)) return result;
  for (const entry of readdirSync(root, { withFileTypes: true })) {
    if (
      entry.isDirectory() &&
      ["node_modules", ".expo", ".git", "Pods", "build", "dist"].includes(entry.name)
    ) {
      continue;
    }
    const path = join(root, entry.name);
    if (entry.isDirectory()) walk(path, result);
    else result.push(path);
  }
  return result;
}

function normalizePath(path) {
  return path.split(sep).join("/");
}

function resolveMigratedPath(filePath, sourceRoot, specifier) {
  let base;
  if (specifier.startsWith("@/")) {
    base = specifier.slice(2);
  } else if (specifier.startsWith(".")) {
    base = normalizePath(relative(sourceRoot, resolve(dirname(filePath), specifier)));
  } else {
    return undefined;
  }
  base = base.replace(/\.(?:ts|tsx|js|jsx)$/u, "");
  for (const candidate of [
    `${base}.ts`,
    `${base}.tsx`,
    `${base}.d.ts`,
    `${base}/index.ts`,
    `${base}/index.tsx`,
  ]) {
    if (targetByPath.has(candidate)) return candidate;
  }
  return undefined;
}

const changedFiles = [];
for (const app of ["pos-ipad", "pos-handheld"]) {
  const appRoot = join(repositoryRoot, "apps", app);
  const sourceRoot = join(appRoot, "src");
  for (const filePath of walk(appRoot).filter((path) =>
    [".ts", ".tsx", ".js", ".jsx"].includes(extname(path))
  )) {
    const source = readFileSync(filePath, "utf8");
    const sourceFile = ts.createSourceFile(filePath, source, ts.ScriptTarget.Latest, true);
    const replacements = [];
    function visit(node) {
      let literal;
      if (
        (ts.isImportDeclaration(node) || ts.isExportDeclaration(node)) &&
        node.moduleSpecifier &&
        ts.isStringLiteral(node.moduleSpecifier)
      ) {
        literal = node.moduleSpecifier;
      } else if (
        ts.isImportTypeNode(node) &&
        ts.isLiteralTypeNode(node.argument) &&
        ts.isStringLiteral(node.argument.literal)
      ) {
        literal = node.argument.literal;
      }
      if (literal) {
        const targetPath = resolveMigratedPath(filePath, sourceRoot, literal.text);
        const target = targetPath ? targetByPath.get(targetPath) : undefined;
        if (targetPath && target) {
          replacements.push({
            start: literal.getStart(sourceFile) + 1,
            end: literal.getEnd() - 1,
            text:
              target.specifier ??
              `@hb/${target.packageName}/${targetPath.replace(/\.(?:ts|tsx)$/u, "")}`,
          });
        }
      }
      ts.forEachChild(node, visit);
    }
    visit(sourceFile);
    let rewritten = source;
    for (const replacement of replacements.sort((left, right) => right.start - left.start)) {
      rewritten =
        rewritten.slice(0, replacement.start) +
        replacement.text +
        rewritten.slice(replacement.end);
    }
    if (rewritten === source) continue;
    changedFiles.push(normalizePath(relative(repositoryRoot, filePath)));
    if (mode === "--write") writeFileSync(filePath, rewritten);
  }
}

changedFiles.sort();
if (changedFiles.length > 0) {
  process.stdout.write(`${changedFiles.join("\n")}\n`);
}
process.stdout.write(
  `${mode === "--write" ? "已重写" : "待重写"} ${changedFiles.length} 个文件\n`,
);
if (mode === "--check" && changedFiles.length > 0) process.exitCode = 1;
