import assert from "node:assert/strict";
import test from "node:test";

import { assertFaceJpegSize } from "./face-photo-validation";

const tooLargeUnpaddedJpeg = "/9j/4AAQ" + "A".repeat(2_796_196);

test("face JPEG validator rejects 2MiB plus one unpadded decoded byte", () => {
  assert.throws(() => assertFaceJpegSize(tooLargeUnpaddedJpeg), /FACE_PHOTO_TOO_LARGE/);
});

test("face JPEG validator rejects non-JPEG base64 before transport", () => {
  assert.throws(() => assertFaceJpegSize("QUJDRA=="), /FACE_PHOTO_INVALID/);
});
