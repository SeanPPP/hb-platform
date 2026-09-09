import assert from "node:assert/strict";
import test from "node:test";

import { cleanupFaceCameraFiles } from "./face-camera-cleanup";

test("camera crash cleanup only removes Expo UUID JPEG files in the exact Camera directory", () => {
  const uuid = "9E839C20-2076-41EC-845A-092672EDC3BC";
  const names = [`Camera/${uuid}.jpg`, `Camera/${uuid}.mov`, "face-upload.jpg", "Camera/receipt.jpg", `other/Camera/${uuid}.jpg`];
  const deleted: string[] = [];
  assert.equal(cleanupFaceCameraFiles("file:///cache/", () => names.map(name => ({ uri: `file:///cache/${name}`, delete: () => { deleted.push(name); } }))), true);
  assert.deepEqual(deleted, [`Camera/${uuid}.jpg`]);
});
test("camera cleanup failures block another face capture but return without aborting POS startup", () => {
  assert.equal(cleanupFaceCameraFiles("file:///cache/", () => { throw new Error("unreadable"); }), false);
  assert.equal(cleanupFaceCameraFiles("file:///cache/", () => [{ uri: "file:///cache/Camera/9e839c20-2076-41ec-845a-092672edc3bc.jpg", delete: () => { throw new Error("permission"); } }]), false);
});
