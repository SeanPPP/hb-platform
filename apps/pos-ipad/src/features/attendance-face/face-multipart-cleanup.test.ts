import assert from "node:assert/strict";
import test from "node:test";

import { cleanupFaceMultipartFiles } from "./face-multipart-cleanup";

test("multipart startup cleanup ignores cache enumeration failure", () => {
  assert.doesNotThrow(() => cleanupFaceMultipartFiles("file:///cache/", () => { throw new Error("cache unavailable"); }));
});

test("multipart startup cleanup only deletes its own face JPEG prefix", () => {
  const deleted: string[] = []; const file = (uri: string) => ({ uri, delete: () => deleted.push(uri) });
  cleanupFaceMultipartFiles("file:///cache/", () => [file("file:///cache/face-crash.jpg"), file("file:///cache/camera.jpg"), file("file:///cache/face-crash.png"), file("file:///other/face-crash.jpg")]);
  assert.deepEqual(deleted, ["file:///cache/face-crash.jpg"]);
});
