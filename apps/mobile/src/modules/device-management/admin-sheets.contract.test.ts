import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import assert from "node:assert/strict";
import ts from "typescript";
import { createInstance } from "i18next";

function assertIncludes(source: string, expected: string, message: string) {
  if (!source.includes(expected)) throw new Error(message);
}

const source = readFileSync(resolve(import.meta.dirname, "admin-sheets.tsx"), "utf8");

assertIncludes(source, "setGrant(result.grant); setToken(result.token);", "emergency success must retain the returned grant and token before readback");
assertIncludes(source, "setOneTimeResult({ secret: result.token", "emergency one-time result must not depend on a later list read");
assertIncludes(source, "void read(true, requestGeneration);", "emergency readback must be independent and generation guarded");
assertIncludes(source, "createInFlight.current", "emergency create must prevent duplicate submits while a request is in flight");
assertIncludes(source, "setCreateOutcomeUnknown(isResultUnknownCreateError(error))", "unknown activation-create outcomes must disable another POST");
assertIncludes(source, "onPress={() => void recoverCreate()}", "unknown outcomes must offer explicit verification instead of automatic POST retry");
assertIncludes(source, "await FileSystem.deleteAsync(file, { idempotent: true })", "exported QR cache files must be deleted after sharing");
assertIncludes(source, "{sheetMessage ? <HelperText type=\"error\" visible>", "activation sheets must show errors inside the active sheet");
assertIncludes(source, "{feedback ? <HelperText type=\"error\" visible>", "edit and emergency sheets must show errors inside the active sheet");

// 纯 gate 的测试必须与真实组件接线一起成立：关窗/重开不能清除未知结果。
const ast = ts.createSourceFile("admin-sheets.tsx", source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const emergency = ast.statements.find((node) => ts.isFunctionDeclaration(node) && node.name?.text === "EmergencyLoginSheet");
assert.ok(emergency && ts.isFunctionDeclaration(emergency) && emergency.body);
const effects: ts.CallExpression[] = [];
function visit(node: ts.Node) {
  if (ts.isCallExpression(node) && node.expression.getText(ast) === "useEffect") effects.push(node);
  ts.forEachChild(node, visit);
}
visit(emergency);
const clearingEffects = effects.filter((effect) => effect.arguments[0]?.getText(ast).includes("clearForNewContext"));
assert.equal(clearingEffects.length, 0, "context and visibility effects must never clear unresolved outcomes");
const visibilityEffects = effects.filter((effect) => effect.arguments[1]?.getText(ast).includes("visible"));
assert.ok(visibilityEffects.length > 0);
for (const effect of visibilityEffects) assert.doesNotMatch(effect.arguments[0].getText(ast), /setCreateOutcomeUnknown\(false\)|clearForNewContext/);
const emergencySource = emergency.getText(ast);
assertIncludes(emergencySource, "JSON.stringify([userGuid, storeCode])", "emergency outcomes must use an account/store context");
assertIncludes(emergencySource, "!emergencyCreateGate.begin(createContextKey)", "submit must synchronously register the pending context before POST");
assertIncludes(emergencySource, "if (unknown) emergencyCreateGate.markUnknown(createContextKey)", "uncertain response must lock its original context even after navigation");
assert.match(source, /catch \(error\) \{\s*if \(!loadGate\.current\.isCurrent\(requestGeneration\)\) return false;\s*onMessage\(getSafeErrorMessage\(error, t, language, "deviceManagement:messages\.activationLoadFailed"\)\)/,
  "旧账号或类型请求的失败反馈也必须经过代次门禁");

const activation = ast.statements.find((node) => ts.isFunctionDeclaration(node) && node.name?.text === "ActivationCodePanel");
assert.ok(activation && ts.isFunctionDeclaration(activation));
const activationSource = activation.getText(ast);
assertIncludes(activationSource, "JSON.stringify([userGuid, kind])", "activation outcomes must use an account/type context");
assertIncludes(activationSource, "!activationCreateGate.begin(createContextKey)", "new components must consult the shared pending/unknown registry");
assert.match(activationSource, /if \(!loadGate\.current\.isCurrent\(requestGeneration\)\) \{ activationCreateGate\.markUnknown\(createContextKey\); return; \}\s*setCreated\([\s\S]*?activationCreateGate\.clearForNewContext\(createContextKey\)/,
  "late activation success without a safe one-time display must remain blocked");
assert.match(emergencySource, /if \(generation\.current !== requestGeneration\) \{ emergencyCreateGate\.markUnknown\(createContextKey\); return; \}\s*setGrant\(result\.grant\); setToken\(result\.token\);\s*setOneTimeResult\([\s\S]*?emergencyCreateGate\.clearForNewContext\(createContextKey\)/,
  "late emergency success without a safe one-time display must remain blocked");
visit(activation);
for (const effect of effects) assert.doesNotMatch(effect.arguments[0].getText(ast), /clearForNewContext/, "no effect may clear unresolved outcomes on a context round trip");
assert.match(activationSource, /getPosActivationGrants\(\{ \.\.\.listQuery, pageSize: 30 \}\)/);
assert.match(activationSource, /getMobileActivationGrants\(\{ \.\.\.listQuery, pageSize: 30 \}\)/);
assertIncludes(activationSource, "setTotal(nextGrants.total)", "pagination must use the server total");
assertIncludes(activationSource, "listQuery.page >= totalPages", "the final page must disable Next");
assertIncludes(activationSource, "listQuery.page <= 1", "the first page must disable Previous");
assertIncludes(activationSource, "|| createOutcomeUnknown", "the submit handler must also reject unknown-outcome retries");
assertIncludes(activationSource, "setMinutes(1440)", "creation validity must use the existing Web default");
assert.doesNotMatch(activationSource, /setTargetUserGuid\(items\[0\]/, "Mobile creation must require explicit account selection");
assertIncludes(activationSource, 'setStoreCode(store.storeCode); setAccounts([]); setTargetUserGuid("")', "changing creation stores must immediately clear the previous target account");
const activationDeclarations = activation.body!.statements.filter(ts.isVariableStatement).flatMap((node) => [...node.declarationList.declarations]);
const revokeDeclaration = activationDeclarations.find((node) => node.name.getText(ast) === "submitRevoke");
assert.ok(revokeDeclaration);
assert.doesNotMatch(revokeDeclaration.getText(ast), /setCreateOutcomeUnknown\(false\)/, "revoking an unrelated old grant must not unlock an uncertain creation");
const listDeclaration = activationDeclarations.find((node) => node.name.getText(ast) === "load");
const storesDeclaration = activationDeclarations.find((node) => node.name.getText(ast) === "loadStores");
assert.ok(listDeclaration && storesDeclaration);
assert.doesNotMatch(listDeclaration.getText(ast), /get(?:Pos|Mobile)ManageableStores/, "a store-list failure must not discard a successful grant readback");
assert.doesNotMatch(storesDeclaration.getText(ast), /listQuery/, "pagination must not reload store options");
const recoveryDeclaration = activationDeclarations.find((node) => node.name.getText(ast) === "recoverCreate");
assert.ok(recoveryDeclaration);
assertIncludes(recoveryDeclaration.getText(ast), "if (!(await load()))", "manual recovery requires a successful current readback");
assertIncludes(recoveryDeclaration.getText(ast), "activationCreateGate.isPending(createContextKey)", "pending requests cannot be manually unlocked");
assert.doesNotMatch(recoveryDeclaration.getText(ast), /createPosActivationCode|createMobileActivationCode/, "manual recovery must never submit a new grant");
const screenSource = readFileSync(resolve(import.meta.dirname, "../../../app/(shell)/device-management.tsx"), "utf8");
for (const hook of ["useDeviceManagementDevices", "useAppDeviceStatuses", "useAppDeviceStatusSummary"]) {
  assert.match(screenSource, new RegExp(`${hook}\\([^,]+, canViewLegacyDeviceRegistration && viewMode ===`), `${hook} must synchronously check legacy-list permission`);
}
assertIncludes(screenSource, 'useState<DeviceManagementViewMode>(canViewLegacyDeviceRegistration ? "registered" : "activation")', "activation-only accounts must start on an authorized view");
assertIncludes(activationSource, 'grant.status === "Available" || grant.status === "Expired"', "expired-code revocation must retain Web parity");
let hasScrollableGrants = false;
function visitActivation(node: ts.Node) {
  if (ts.isJsxElement(node) && node.openingElement.tagName.getText(ast) === "ScrollView" && node.children.some((child) => child.getText(ast).includes("grants.map"))) hasScrollableGrants = true;
  ts.forEachChild(node, visitActivation);
}
visitActivation(activation);
assert.ok(hasScrollableGrants, "activation records must be inside a scrollable list");

// 使用真实命名空间解析界面与错误回退文案，不能只比较中英文 JSON 的键集合。
async function verifyTranslations() {
  const keys = [...source.matchAll(/["`]((?:deviceManagement[.:])[A-Za-z.]+)["`]/g)].map((match) => match[1]);
  assert.ok(keys.length > 40);
  const translator = createInstance();
  for (const language of ["zh", "en"]) {
    const messages = JSON.parse(readFileSync(resolve(import.meta.dirname, `../../locales/${language}/screens/deviceManagement.json`), "utf8"));
    await translator.init({ lng: language, fallbackLng: false, defaultNS: "deviceManagement", resources: { [language]: { deviceManagement: messages } } });
    for (const key of keys) assert.ok(translator.exists(key), `${language}: missing displayed text ${key}`);
    assert.equal(translator.t("deviceManagement:edit.title"), messages.edit.title);
  }
}
void verifyTranslations()
  .then(() => console.log("admin-sheets.contract.test.ts: ok"))
  .catch((error) => { console.error(error); process.exitCode = 1; });
