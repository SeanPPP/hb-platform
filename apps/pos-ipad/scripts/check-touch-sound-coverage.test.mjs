import assert from "node:assert/strict";
import { readdir, readFile } from "node:fs/promises";
import { relative } from "node:path";
import test from "node:test";

import ts from "typescript";

const projectRoot = new URL("../", import.meta.url);
const sourceRoots = ["app", "src"];
const excludedPathSegments = new Set(["node_modules", ".expo", "ios", "android"]);
const touchPrimitiveNames = new Set([
  "Pressable",
  "TouchableOpacity",
  "TouchableHighlight",
  "TouchableWithoutFeedback",
  "Button",
  "Switch",
]);
const reactNativeTouchableHostNames = new Set(["Text", "View", "Image"]);
const directTextInputAllowlist = new Set([
  // HID 扫描器需要维持原生焦点与无软键盘行为，不能改成可触控输入包装器。
  "src/core/peripherals/scanner/hid-scanner-capture.tsx",
]);
const controlImplementationFiles = new Set([
  "src/ui/controls/pos-pan-responder-view.tsx",
  "src/ui/controls/pos-pressable.tsx",
  "src/ui/controls/pos-switch.tsx",
  "src/ui/controls/pos-text-input.tsx",
]);

test("业务 TSX 只能经由 POS 触控控件承接人工触摸", async () => {
  const failures = [];

  for (const file of await productionTsxFiles()) {
    const path = relativePath(file);
    failures.push(
      ...touchCoverageFailuresForSource(await readFile(file, "utf8"), path),
    );
  }

  assert.deepEqual(failures, [], `触控声音覆盖遗漏：\n${failures.join("\n")}`);
});

test("AST 拒绝 RN 宿主、Animated 宿主与第三方组件直接承接触摸", () => {
  const failures = touchCoverageFailuresForSource(
    `
      import { Animated, Image, Text, View } from "react-native";
      import { Button } from "react-native-paper";
      import { Link } from "expo-router";

      export function Fixture() {
        return <>
          <Text onPress={() => undefined}>Text</Text>
          <View onResponderRelease={() => undefined} />
          <Image onLongPress={() => undefined} />
          <Animated.View onTouchStart={() => undefined} />
          <Animated.Text onTouchEnd={() => undefined}>Animated text</Animated.Text>
          <Animated.Image onResponderGrant={() => undefined} />
          <Button onPress={() => undefined}>Button</Button>
          <Link onPress={() => undefined} href="/" />
        </>;
      }
    `,
    "src/fixtures/forbidden-touch.tsx",
  );
  const message = failures.join("\n");

  for (const expected of [
    "<Text>",
    "<View>",
    "<Image>",
    "<Animated.View>",
    "<Animated.Text>",
    "<Animated.Image>",
    "<Button>",
    "<Link>",
  ]) {
    assert.match(message, new RegExp(expected.replace(".", "\\."), "u"));
  }
});

test("AST 拒绝可解析 spread 与第三方隐式激活入口", () => {
  const failures = touchCoverageFailuresForSource(
    `
      import { Text } from "react-native";
      import { Link } from "expo-router";
      import { Switch as PaperSwitch } from "react-native-paper";

      const onPress = () => undefined;
      export function Fixture() {
        return <>
          <Text {...{ onPress }}>Spread</Text>
          <Link href="/settings" />
          <PaperSwitch onValueChange={() => undefined} value />
        </>;
      }
    `,
    "src/fixtures/forbidden-indirect-touch.tsx",
  );
  const message = failures.join("\n");

  assert.match(message, /<Text>.*onPress/u);
  assert.match(message, /<Link>.*href/u);
  assert.match(message, /<PaperSwitch>.*onValueChange/u);
});

test("AST 对 RN 宿主、原生输入与第三方组件的动态 spread fail-closed", () => {
  const failures = touchCoverageFailuresForSource(
    `
      import { Text, TextInput } from "react-native";
      import { Link } from "expo-router";

      export function Fixture({ inputProps, linkProps, textProps }) {
        return <>
          <Text {...textProps}>Dynamic text</Text>
          <TextInput {...inputProps} />
          <Link {...linkProps} />
        </>;
      }
    `,
    "src/fixtures/forbidden-dynamic-spread.tsx",
  );
  const message = failures.join("\n");

  assert.match(message, /<Text>.*动态 spread/u);
  assert.match(message, /<TextInput>.*动态 spread/u);
  assert.match(message, /<Link>.*动态 spread/u);
});

test("AST 允许 POS 控件与本地业务封装透传触摸回调", () => {
  const failures = touchCoverageFailuresForSource(
    `
      import { PosPressable } from "@/ui/controls/pos-pressable";
      import { PosSwitch } from "@/ui/controls/pos-switch";
      import { PosTextInput } from "@/ui/controls/pos-text-input";
      import { LocalButton } from "./local-button";
      import { LocalLink } from "./local-link";
      import { LocalSwitch } from "./local-switch";

      function InlineButton({ onPress }) {
        return <PosPressable onPress={onPress}>Inline</PosPressable>;
      }

      export function Fixture() {
        const dynamicProps = {};
        return <>
          <PosPressable onPress={() => undefined}>POS</PosPressable>
          <PosTextInput onTouchEnd={() => undefined} />
          <PosSwitch onValueChange={() => undefined} />
          <PosPressable {...{ onPress: () => undefined }}>Spread POS</PosPressable>
          <PosPressable {...dynamicProps}>Dynamic POS</PosPressable>
          <PosTextInput {...dynamicProps} />
          <PosSwitch {...dynamicProps} />
          <LocalButton onPress={() => undefined} />
          <LocalLink href="/settings" />
          <LocalSwitch onValueChange={() => undefined} />
          <LocalButton {...dynamicProps} />
          <InlineButton onPress={() => undefined} />
        </>;
      }
    `,
    "src/fixtures/allowed-touch.tsx",
  );

  assert.deepEqual(failures, []);
});

test("HID 隐藏 TextInput 保持唯一原生输入例外", () => {
  const failures = touchCoverageFailuresForSource(
    `
      import { TextInput } from "react-native";
      export function Fixture({ captureProps }) {
        return <TextInput {...captureProps} />;
      }
    `,
    "src/core/peripherals/scanner/hid-scanner-capture.tsx",
  );

  assert.deepEqual(failures, []);
});

function touchCoverageFailuresForSource(sourceText, path) {
  if (controlImplementationFiles.has(path)) return [];
  const failures = [];
  const source = ts.createSourceFile(
    path,
    sourceText,
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TSX,
  );
  const importBindings = collectImportBindings(source);

  visit(source, (node) => {
    if (ts.isImportDeclaration(node) && isReactNativeImport(node)) {
      for (const imported of reactNativeValueImports(node)) {
        if (touchPrimitiveNames.has(imported)) {
          failures.push(`${path}:${lineOf(source, node)} 禁止直接导入 ${imported}`);
        }
      }
    }

    if (!ts.isJsxOpeningElement(node) && !ts.isJsxSelfClosingElement(node)) return;
    const tagParts = jsxTagParts(node.tagName);
    const tag = tagParts.join(".");
    const reactNativeTag = resolveReactNativeTag(tagParts, importBindings);
    const touchHandlers = jsxTouchHandlerNames(node);
    const hasDynamicSpread = jsxHasDynamicSpread(node);
    const thirdPartyActivations = jsxThirdPartyActivationNames(
      node,
      tagParts,
      importBindings,
    );
    if (reactNativeTag && touchPrimitiveNames.has(reactNativeTag)) {
      failures.push(`${path}:${lineOf(source, node)} 禁止直接使用 <${tag}>`);
    }
    if (
      reactNativeTag === "TextInput" &&
      !directTextInputAllowlist.has(path)
    ) {
      failures.push(`${path}:${lineOf(source, node)} 可见输入必须使用 <PosTextInput>`);
    }
    if (
      reactNativeTag &&
      isReactNativeTouchableHost(reactNativeTag) &&
      touchHandlers.length > 0
    ) {
      failures.push(
        `${path}:${lineOf(source, node)} <${tag}> 禁止直接绑定 ${touchHandlers.join(
          "/",
        )}`,
      );
    }
    if (
      hasDynamicSpread &&
      reactNativeTag &&
      (isReactNativeTouchableHost(reactNativeTag) ||
        reactNativeTag === "TextInput") &&
      !(
        reactNativeTag === "TextInput" &&
        directTextInputAllowlist.has(path)
      )
    ) {
      failures.push(
        `${path}:${lineOf(source, node)} <${tag}> 动态 spread 无法证明触控安全`,
      );
    }
    if (
      hasDynamicSpread &&
      isThirdPartyTag(tagParts, importBindings)
    ) {
      failures.push(
        `${path}:${lineOf(source, node)} 第三方 <${tag}> 动态 spread 无法证明触控安全`,
      );
    }
    const thirdPartyEntrypoints = [
      ...new Set([...touchHandlers, ...thirdPartyActivations]),
    ];
    if (
      thirdPartyEntrypoints.length > 0 &&
      isThirdPartyTag(tagParts, importBindings)
    ) {
      failures.push(
        `${path}:${lineOf(source, node)} 第三方 <${tag}> 禁止直接承接 ${thirdPartyEntrypoints.join(
          "/",
        )}`,
      );
    }
  });

  return failures;
}

async function productionTsxFiles() {
  const files = [];
  for (const sourceRoot of sourceRoots) {
    await collectTsxFiles(new URL(`${sourceRoot}/`, projectRoot), files);
  }
  return files.sort();
}

async function collectTsxFiles(directory, files) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    if (excludedPathSegments.has(entry.name)) continue;
    const path = new URL(entry.name, directory);
    if (entry.isDirectory()) {
      await collectTsxFiles(new URL(`${entry.name}/`, directory), files);
    } else if (
      entry.isFile() &&
      entry.name.endsWith(".tsx") &&
      !/\.(?:test|spec|rntl)\.tsx$/u.test(entry.name)
    ) {
      files.push(path);
    }
  }
}

function isReactNativeImport(node) {
  return ts.isStringLiteral(node.moduleSpecifier) && node.moduleSpecifier.text === "react-native";
}

function reactNativeValueImports(node) {
  if (!node.importClause || node.importClause.isTypeOnly || !node.importClause.namedBindings) {
    return [];
  }
  if (!ts.isNamedImports(node.importClause.namedBindings)) return [];
  return node.importClause.namedBindings.elements
    .filter((element) => !element.isTypeOnly)
    .map((element) => element.propertyName?.text ?? element.name.text);
}

function collectImportBindings(source) {
  const reactNative = new Map();
  const reactNativeNamespaces = new Set();
  const thirdParty = new Map();
  const thirdPartyNamespaces = new Set();

  for (const statement of source.statements) {
    if (!ts.isImportDeclaration(statement) || !statement.importClause) continue;
    if (
      statement.importClause.isTypeOnly ||
      !ts.isStringLiteral(statement.moduleSpecifier)
    ) {
      continue;
    }
    const moduleName = statement.moduleSpecifier.text;
    const bindings =
      moduleName === "react-native"
        ? reactNative
        : isThirdPartyModule(moduleName)
          ? thirdParty
          : null;
    const namespaces =
      moduleName === "react-native"
        ? reactNativeNamespaces
        : isThirdPartyModule(moduleName)
          ? thirdPartyNamespaces
          : null;
    if (!bindings || !namespaces) continue;

    if (statement.importClause.name) {
      bindings.set(statement.importClause.name.text, "default");
    }
    const namedBindings = statement.importClause.namedBindings;
    if (namedBindings && ts.isNamespaceImport(namedBindings)) {
      namespaces.add(namedBindings.name.text);
    } else if (namedBindings && ts.isNamedImports(namedBindings)) {
      for (const element of namedBindings.elements) {
        if (element.isTypeOnly) continue;
        bindings.set(
          element.name.text,
          element.propertyName?.text ?? element.name.text,
        );
      }
    }
  }

  return {
    reactNative,
    reactNativeNamespaces,
    thirdParty,
    thirdPartyNamespaces,
  };
}

function isThirdPartyModule(moduleName) {
  return (
    moduleName !== "react" &&
    moduleName !== "react-native" &&
    !moduleName.startsWith(".") &&
    !moduleName.startsWith("@/")
  );
}

function jsxTagParts(tagName) {
  if (ts.isIdentifier(tagName)) return [tagName.text];
  if (ts.isPropertyAccessExpression(tagName)) {
    return [...jsxTagParts(tagName.expression), tagName.name.text];
  }
  if (ts.isJsxNamespacedName(tagName)) {
    return [tagName.namespace.text, tagName.name.text];
  }
  return [tagName.getText()];
}

function resolveReactNativeTag(tagParts, importBindings) {
  const [root, second, third] = tagParts;
  if (!root) return null;
  if (tagParts.length === 1) {
    return importBindings.reactNative.get(root) ?? null;
  }
  if (importBindings.reactNativeNamespaces.has(root)) {
    return second === "Animated" && third ? `Animated.${third}` : second ?? null;
  }
  if (
    importBindings.reactNative.get(root) === "Animated" &&
    tagParts.length === 2
  ) {
    return `Animated.${second}`;
  }
  return null;
}

function isReactNativeTouchableHost(tag) {
  const host = tag.startsWith("Animated.") ? tag.slice("Animated.".length) : tag;
  return reactNativeTouchableHostNames.has(host);
}

function jsxTouchHandlerNames(node) {
  return jsxAttributeNames(node).filter(isTouchHandlerName);
}

function isTouchHandlerName(name) {
  return (
    name.startsWith("onPress") ||
    name === "onLongPress" ||
    name.startsWith("onTouch") ||
    name.startsWith("onResponder") ||
    /^on(?:Start|Move)ShouldSetResponder(?:Capture)?$/u.test(name) ||
    name === "onShouldBlockNativeResponder"
  );
}

function jsxAttributeNames(node) {
  const names = [];
  for (const property of node.attributes.properties) {
    if (ts.isJsxAttribute(property)) {
      if (ts.isIdentifier(property.name)) names.push(property.name.text);
      continue;
    }
    names.push(...staticObjectPropertyNames(property.expression));
  }
  return [...new Set(names)];
}

function jsxHasDynamicSpread(node) {
  return node.attributes.properties.some(
    (property) =>
      ts.isJsxSpreadAttribute(property) &&
      !isStaticallyResolvableObjectExpression(property.expression),
  );
}

function isStaticallyResolvableObjectExpression(expression) {
  const unwrapped = unwrapStaticExpression(expression);
  if (!ts.isObjectLiteralExpression(unwrapped)) return false;
  return unwrapped.properties.every((property) => {
    if (ts.isSpreadAssignment(property)) {
      return isStaticallyResolvableObjectExpression(property.expression);
    }
    if (ts.isShorthandPropertyAssignment(property)) return true;
    if (
      ts.isPropertyAssignment(property) ||
      ts.isMethodDeclaration(property) ||
      ts.isGetAccessorDeclaration(property) ||
      ts.isSetAccessorDeclaration(property)
    ) {
      return staticPropertyName(property.name) !== null;
    }
    return false;
  });
}

function staticObjectPropertyNames(expression) {
  const unwrapped = unwrapStaticExpression(expression);
  if (!ts.isObjectLiteralExpression(unwrapped)) return [];

  const names = [];
  for (const property of unwrapped.properties) {
    if (ts.isSpreadAssignment(property)) {
      names.push(...staticObjectPropertyNames(property.expression));
      continue;
    }
    if (ts.isShorthandPropertyAssignment(property)) {
      names.push(property.name.text);
      continue;
    }
    if (
      ts.isPropertyAssignment(property) ||
      ts.isMethodDeclaration(property) ||
      ts.isGetAccessorDeclaration(property) ||
      ts.isSetAccessorDeclaration(property)
    ) {
      const name = staticPropertyName(property.name);
      if (name) names.push(name);
    }
  }
  return names;
}

function unwrapStaticExpression(expression) {
  let current = expression;
  while (
    ts.isParenthesizedExpression(current) ||
    ts.isAsExpression(current) ||
    ts.isTypeAssertionExpression(current) ||
    ts.isSatisfiesExpression(current) ||
    ts.isNonNullExpression(current)
  ) {
    current = current.expression;
  }
  return current;
}

function staticPropertyName(name) {
  if (ts.isIdentifier(name) || ts.isStringLiteral(name)) return name.text;
  if (
    ts.isComputedPropertyName(name) &&
    ts.isStringLiteral(unwrapStaticExpression(name.expression))
  ) {
    return unwrapStaticExpression(name.expression).text;
  }
  return null;
}

function jsxThirdPartyActivationNames(node, tagParts, importBindings) {
  if (!isThirdPartyTag(tagParts, importBindings)) return [];
  const component = thirdPartyComponentName(tagParts, importBindings);
  const attributeNames = jsxAttributeNames(node);
  const activations = [];

  if (
    (component === "Link" || component.endsWith("Link")) &&
    attributeNames.includes("href")
  ) {
    activations.push("href");
  }
  if (
    (component === "Link" || component.endsWith("Link")) &&
    attributeNames.includes("to")
  ) {
    activations.push("to");
  }
  if (
    (component === "Switch" || component.endsWith("Switch")) &&
    attributeNames.includes("onValueChange")
  ) {
    activations.push("onValueChange");
  }
  return activations;
}

function thirdPartyComponentName(tagParts, importBindings) {
  const [root] = tagParts;
  if (!root) return "";
  if (importBindings.thirdPartyNamespaces.has(root)) {
    return tagParts.at(-1) ?? root;
  }
  const imported = importBindings.thirdParty.get(root);
  if (!imported || imported === "default") {
    return tagParts.at(-1) ?? root;
  }
  return imported;
}

function isThirdPartyTag(tagParts, importBindings) {
  const [root] = tagParts;
  return Boolean(
    root &&
      (importBindings.thirdParty.has(root) ||
        importBindings.thirdPartyNamespaces.has(root)),
  );
}

function lineOf(source, node) {
  return source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1;
}

function relativePath(file) {
  return relative(new URL(".", projectRoot).pathname, file.pathname);
}

function visit(node, callback) {
  callback(node);
  ts.forEachChild(node, (child) => visit(child, callback));
}
