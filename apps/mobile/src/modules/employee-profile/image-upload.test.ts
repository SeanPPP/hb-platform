import assert from "node:assert/strict";
import { completeEmployeeProfileImageUpload } from "./image-upload-workflow";

async function main() {
  const calls: string[] = [];
  const savedProfile = {
    username: "admin",
    bankBsb: "",
    bankAccountNumber: "",
    superannuationCompanyName: "",
    superannuationCompanyCode: "",
    superannuationAccountNumber: "",
    birthday: "",
    gender: "",
    employmentType: "",
    avatarUrl: "https://cdn/avatar.jpg",
    identityId: "",
    identityPhotoUrl: "",
    address: "",
  };

  const result = await completeEmployeeProfileImageUpload(
    {
      kind: "avatar",
      uri: "file://processed.jpg",
      fileName: "avatar.jpg",
      contentType: "image/jpeg",
      fileSize: 123,
    },
    {
      readBlob: async () => new Blob([new Uint8Array(4096)]),
      requestSignature: async (_kind, request) => {
        calls.push(`signature:${request.fileSize}:${String((request as { objectKey?: unknown }).objectKey)}`);
        return { url: "https://upload", objectKey: "employee/avatar/1.jpg", headers: {} };
      },
      uploadBlob: async () => {
        calls.push("upload");
        return "employee/avatar/1.jpg";
      },
      completeUpload: async (kind, objectKey) => {
        calls.push(`complete:${kind}:${objectKey}`);
        return savedProfile;
      },
    }
  );

  assert.equal(result.avatarUrl, savedProfile.avatarUrl, "完成接口成功后才返回已保存资料");
  assert.deepEqual(
    calls,
    [
      "signature:4096:undefined",
      "upload",
      "complete:avatar:employee/avatar/1.jpg",
    ],
    "签名、上传、完成必须严格串行且对象键由服务端生成"
  );

  await assert.rejects(
    () =>
      completeEmployeeProfileImageUpload(
        {
          kind: "identityPhoto",
          uri: "file://processed.jpg",
          fileName: "identity.jpg",
          contentType: "image/jpeg",
          fileSize: 4096,
        },
        {
          readBlob: async () => new Blob(["processed"]),
          requestSignature: async () => ({
            url: "https://upload",
            objectKey: "employee/identity/1.jpg",
            headers: {},
          }),
          uploadBlob: async () => "employee/identity/1.jpg",
          completeUpload: async () => {
            throw new Error("complete failed");
          },
        }
      ),
    /complete failed/,
    "完成接口失败时不得伪造已保存图片结果"
  );

  console.log("image-upload.test.ts: ok");
}

void main();
