import assert from "node:assert/strict";
import mobileEn from "./mobile-privacy-policy.en.json";
import mobileZh from "./mobile-privacy-policy.zh.json";
import webEn from "../../../../web/src/content/mobile-privacy-policy.en.json";
import webZh from "../../../../web/src/content/mobile-privacy-policy.zh.json";

type JsonRecord = Record<string, unknown>;

const POLICY_KEYS = [
  "policyVersion",
  "language",
  "title",
  "subtitle",
  "effectiveDateLabel",
  "effectiveDate",
  "summary",
  "organization",
  "sections",
  "footer",
];
const ORGANIZATION_KEYS = ["label", "name", "contactLabel", "email"];
const SECTION_KEYS = ["id", "title", "paragraphs", "items"];
const FOOTER_KEYS = [
  "backLabel",
  "publicCopy",
  "publicUrl",
  "emailLabel",
  "openFailedTitle",
  "openFailedMessage",
];

function assertRecord(value: unknown, name: string): asserts value is JsonRecord {
  assert.equal(typeof value, "object", `${name} 必须是对象`);
  assert.notEqual(value, null, `${name} 不能为 null`);
  assert.equal(Array.isArray(value), false, `${name} 不能是数组`);
}

function assertExactKeys(value: JsonRecord, keys: string[], name: string) {
  assert.deepEqual(Object.keys(value).sort(), [...keys].sort(), `${name} 字段必须完整且无多余字段`);
}

function assertNonEmptyString(value: unknown, name: string) {
  if (typeof value !== "string") {
    throw new TypeError(`${name} 必须是字符串`);
  }
  assert.notEqual(value.trim(), "", `${name} 不能为空`);
}

function assertStringArray(value: unknown, name: string) {
  if (!Array.isArray(value)) {
    throw new TypeError(`${name} 必须是数组`);
  }
  value.forEach((item, index) => assertNonEmptyString(item, `${name}[${index}]`));
}

function assertPolicyStructure(policy: unknown, expectedLanguage: "en" | "zh", name: string) {
  assertRecord(policy, name);
  assertExactKeys(policy, POLICY_KEYS, name);

  for (const key of [
    "policyVersion",
    "title",
    "subtitle",
    "effectiveDateLabel",
    "effectiveDate",
    "summary",
  ]) {
    assertNonEmptyString(policy[key], `${name}.${key}`);
  }
  assert.equal(policy.language, expectedLanguage, `${name}.language 不正确`);

  assertRecord(policy.organization, `${name}.organization`);
  assertExactKeys(policy.organization, ORGANIZATION_KEYS, `${name}.organization`);
  for (const key of ORGANIZATION_KEYS) {
    assertNonEmptyString(policy.organization[key], `${name}.organization.${key}`);
  }

  assert.ok(Array.isArray(policy.sections), `${name}.sections 必须是数组`);
  assert.ok(policy.sections.length > 0, `${name}.sections 不能为空`);
  policy.sections.forEach((section, index) => {
    const sectionName = `${name}.sections[${index}]`;
    assertRecord(section, sectionName);
    assertExactKeys(section, SECTION_KEYS, sectionName);
    assertNonEmptyString(section.id, `${sectionName}.id`);
    assertNonEmptyString(section.title, `${sectionName}.title`);
    assertStringArray(section.paragraphs, `${sectionName}.paragraphs`);
    assertStringArray(section.items, `${sectionName}.items`);
  });

  assertRecord(policy.footer, `${name}.footer`);
  assertExactKeys(policy.footer, FOOTER_KEYS, `${name}.footer`);
  for (const key of FOOTER_KEYS) {
    assertNonEmptyString(policy.footer[key], `${name}.footer.${key}`);
  }
}

function assertSameJsonShape(expected: unknown, actual: unknown, name: string) {
  if (Array.isArray(expected)) {
    assert.ok(Array.isArray(actual), `${name} 必须同为数组`);
    assert.equal(actual.length, expected.length, `${name} 数组长度必须一致`);
    expected.forEach((value, index) => assertSameJsonShape(value, actual[index], `${name}[${index}]`));
    return;
  }

  if (typeof expected === "object" && expected !== null) {
    assertRecord(expected, `${name}.expected`);
    assertRecord(actual, `${name}.actual`);
    assert.deepEqual(Object.keys(actual).sort(), Object.keys(expected).sort(), `${name} 字段必须一致`);
    Object.keys(expected).forEach((key) => assertSameJsonShape(expected[key], actual[key], `${name}.${key}`));
    return;
  }

  assert.equal(typeof actual, typeof expected, `${name} 值类型必须一致`);
}

// 四份 JSON 均需独立验明完整结构，再校验跨端内容与跨语言骨架。
assertPolicyStructure(mobileEn, "en", "mobileEn");
assertPolicyStructure(mobileZh, "zh", "mobileZh");
assertPolicyStructure(webEn, "en", "webEn");
assertPolicyStructure(webZh, "zh", "webZh");
assert.deepEqual(webEn, mobileEn, "英文 App 与 Web 隐私政策必须完全一致");
assert.deepEqual(webZh, mobileZh, "中文 App 与 Web 隐私政策必须完全一致");
assertSameJsonShape(mobileEn, mobileZh, "中英文隐私政策结构");
assert.equal(mobileEn.footer.publicUrl, "https://hotbargain.vip/privacy/mobile");
assert.equal(mobileEn.organization.email, "inquiries@hotbargain.com.au");
assert.match(JSON.stringify(mobileEn), /Singapore/);
assert.match(JSON.stringify(mobileZh), /新加坡/);
assert.doesNotMatch(JSON.stringify(mobileEn), /Shanghai/i);
assert.doesNotMatch(JSON.stringify(mobileZh), /上海/);

console.log("mobile privacy policy parity tests passed");
