import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const squareController = readFileSync(
  new URL("../../pos-wpf/src/Hbpos.Api/Controllers/SquareController.cs", import.meta.url),
  "utf8",
);
const squareContracts = readFileSync(
  new URL("../../pos-wpf/src/Hbpos.Contracts/Square/SquareTerminalContracts.cs", import.meta.url),
  "utf8",
);
const linklyController = readFileSync(
  new URL("../../pos-wpf/src/Hbpos.Api/Controllers/LinklyController.cs", import.meta.url),
  "utf8",
);
const linklyContracts = readFileSync(
  new URL(
    "../../pos-wpf/src/Hbpos.Contracts/Linkly/LinklyCloudBackendAsyncContracts.cs",
    import.meta.url,
  ),
  "utf8",
);
const recoveryContract = JSON.parse(
  readFileSync(
    new URL("../test-fixtures/wpf-parity/payment-recovery-contract.json", import.meta.url),
    "utf8",
  ),
);

for (const route of recoveryContract.square.requiredRoutes) {
  assert.ok(squareController.includes(`[${route.attribute}("${route.template}")]`), route.template);
}

for (const field of recoveryContract.square.durableFields) {
  assert.match(squareContracts, new RegExp(`\\b${field.evidence}\\b`));
}

for (const route of recoveryContract.linkly.requiredRoutes) {
  assert.ok(linklyController.includes(`[${route.attribute}("${route.template}")]`), route.template);
}

for (const field of recoveryContract.linkly.durableFields) {
  assert.match(linklyContracts, new RegExp(`\\b${field.evidence}\\b`, "i"));
}

assert.equal(recoveryContract.square.unknownAllowsFreshCharge, false);
assert.equal(recoveryContract.linkly.unknownAllowsFreshCharge, false);
assert.equal(recoveryContract.linkly.createHasClientIdempotencyKey, false);

console.log("payment recovery contract: Square and Linkly routes verified");
