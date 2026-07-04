import assert from "node:assert/strict";
import {
  checkAndDownloadNativeAppUpdate,
  checkLegacyNativeAppUpdate,
  getBuildBoundNativeAppDownloadUrl,
  getStableNativeAppDownloadUrl,
  type NativeAppBuildInfo,
  type NativeAppUpdateDependencies,
} from "./native-app-update";

type NativeAppUpdateTestDependencies = NativeAppUpdateDependencies & {
  downloaded: string[];
  deleted: string[];
  getDownloadUrl?: (build: NativeAppBuildInfo) => string | null;
};

type CachedApkTestFile = { exists: true; size: number; isDirectory: false; modificationTime: number };

function createDependencies(
  overrides?: Partial<NativeAppUpdateDependencies> & {
    getDownloadUrl?: (build: NativeAppBuildInfo) => string | null;
  }
): NativeAppUpdateTestDependencies {
  const downloaded: string[] = [];
  const deleted: string[] = [];
  let fileExists = false;
  return {
    platform: "android",
    getCurrentBuildVersion: () => "10",
    getBuildProfile: () => "production",
    getDownloadDirectory: () => "file:///cache",
    getFileInfo: async () => ({ exists: fileExists, size: fileExists ? 1024 : undefined }),
    downloadFile: async (url, fileUri) => {
      downloaded.push(`${url} -> ${fileUri}`);
      fileExists = true;
      return { uri: fileUri, status: 200, mimeType: "application/vnd.android.package-archive" };
    },
    deleteFile: async (fileUri) => {
      deleted.push(fileUri);
      fileExists = false;
    },
    apiClient: {
      get: async () => ({
        data: {
          easBuildId: "build-11",
          appVersion: "1.0.2",
          appBuildVersion: "11",
          artifactUrl: "https://expo.dev/artifacts/eas/build-11.apk",
          buildProfile: "production",
        },
      }),
    },
    downloaded,
    deleted,
    ...overrides,
  } as NativeAppUpdateTestDependencies;
}

function createCachedApkFiles(entries: Array<[string, number]>) {
  return new Map<string, CachedApkTestFile>(
    entries.map(([fileName, modificationTime]) => [
      `file:///cache/${fileName}`,
      { exists: true, size: 1024, isDirectory: false, modificationTime },
    ])
  );
}

async function run() {
  {
    assert.equal(
      getStableNativeAppDownloadUrl("https://hotbargain.vip/api", "preview"),
      "https://hotbargain.vip/api/mobile-app-builds/android-latest/download?profile=preview",
      "legacy latest helper 应生成 android-latest 稳定下载入口"
    );
  }

  {
    assert.equal(
      getBuildBoundNativeAppDownloadUrl("https://hotbargain.vip/api", {
        easBuildId: "eas/build 11",
        appVersion: "1.0.2",
        appBuildVersion: "11",
        artifactUrl: "https://expo.dev/artifacts/eas/build-11.apk",
        buildProfile: "preview",
      }),
      "https://hotbargain.vip/api/mobile-app-builds/android/eas%2Fbuild%2011/download?profile=preview",
      "新安装器 helper 应生成绑定 easBuildId 的稳定下载入口"
    );
  }

  {
    const dependencies = createDependencies();
    const result = await checkAndDownloadNativeAppUpdate(dependencies);

    assert.equal(result.status, "downloaded", "远端 buildVersion 更高时应下载 APK");
    assert.equal(dependencies.downloaded.length, 1, "APK 应在后台下载一次");
  }

  {
    const dependencies = createDependencies({
      apiClient: {
        get: async () => ({
          data: {
            success: true,
            data: {
              easBuildId: "build-11",
              appVersion: "1.0.2",
              appBuildVersion: "11",
              artifactUrl: "https://expo.dev/artifacts/eas/build-11.apk",
              buildProfile: "production",
            },
          },
        }),
      },
    });
    const result = await checkAndDownloadNativeAppUpdate(dependencies);

    assert.equal(result.status, "downloaded", "后端 ApiResponse.data 包装返回时应仍能识别最新 APK");
    assert.equal(dependencies.downloaded.length, 1, "包装响应里的新 APK 应触发下载");
  }

  {
    const dependencies = createDependencies({
      getDownloadUrl: (build) => getBuildBoundNativeAppDownloadUrl("https://hotbargain.vip/api", build),
    });
    const result = await checkAndDownloadNativeAppUpdate(dependencies);

    assert.equal(result.status, "downloaded", "新安装器分支检测到新 APK 时应下载");
    assert.equal(
      dependencies.downloaded[0],
      "https://hotbargain.vip/api/mobile-app-builds/android/build-11/download?profile=production -> file:///cache/hb-build-11.apk",
      "新安装器分支应使用绑定 build 的后端稳定下载入口，避免 latest 指向漂移"
    );
  }

  {
    const certificateErrors = [
      "NET::ERR_CERT_DATE_INVALID",
      "javax.net.ssl.SSLHandshakeException: CertPathValidatorException",
      "CERTIFICATE_VERIFY_FAILED: Trust anchor for certification path not found",
    ];
    for (const certificateError of certificateErrors) {
      let fileExists = false;
      let attempts = 0;
      const dependencies = createDependencies({
        apiClient: {
          get: async () => ({
            data: {
              easBuildId: "build-11",
              appVersion: "1.0.2",
              appBuildVersion: "11",
              artifactUrl: "https://cos.hotbargain.top/mobile-app-builds/production/build-11.apk",
              buildProfile: "production",
            },
          }),
        },
        getDownloadUrl: () => "https://download.hotbargain.top/mobile-app-builds/android-latest/download?profile=production",
        getFileInfo: async () => ({ exists: fileExists, size: fileExists ? 1024 : undefined }),
        downloadFile: async (url, fileUri) => {
          dependencies.downloaded.push(`${url} -> ${fileUri}`);
          attempts += 1;
          if (attempts === 1) {
            throw new Error(certificateError);
          }
          fileExists = true;
          return { uri: fileUri, status: 200, mimeType: "application/vnd.android.package-archive" };
        },
        deleteFile: async (fileUri) => {
          dependencies.deleted.push(fileUri);
          fileExists = false;
        },
      });
      const result = await checkAndDownloadNativeAppUpdate(dependencies);

      assert.equal(result.status, "downloaded", `稳定入口证书错误后应重试最终 APK 地址: ${certificateError}`);
      assert.deepEqual(
        dependencies.downloaded,
        [
          "https://download.hotbargain.top/mobile-app-builds/android-latest/download?profile=production -> file:///cache/hb-build-11.apk",
          "https://cos.hotbargain.top/mobile-app-builds/production/build-11.apk -> file:///cache/hb-build-11.apk",
        ],
        `证书异常后只应从稳定入口切到 COS 最终文件一次: ${certificateError}`
      );
      assert.deepEqual(dependencies.deleted, ["file:///cache/hb-build-11.apk"], "重试前应删除半成品 APK");
    }
  }

  {
    let fileExists = false;
    const dependencies = createDependencies({
      apiClient: {
        get: async () => ({
          data: {
            easBuildId: "build-11",
            appVersion: "1.0.2",
            appBuildVersion: "11",
            artifactUrl: "https://cos.hotbargain.top/mobile-app-builds/production/build-11.apk",
            buildProfile: "production",
          },
        }),
      },
      getDownloadUrl: () => "https://download.hotbargain.top/mobile-app-builds/android-latest/download?profile=production",
      getFileInfo: async () => ({ exists: fileExists, size: fileExists ? 1024 : undefined }),
      downloadFile: async (url, fileUri) => {
        dependencies.downloaded.push(`${url} -> ${fileUri}`);
        fileExists = true;
        return {
          uri: fileUri,
          status: 200,
          mimeType: "text/html; charset=utf-8",
        };
      },
      deleteFile: async (fileUri) => {
        dependencies.deleted.push(fileUri);
        fileExists = false;
      },
    });

    await assert.rejects(
      () => checkAndDownloadNativeAppUpdate(dependencies),
      /文件类型异常/,
      "稳定入口返回 HTML 错误页时不应按证书异常兜底"
    );
    assert.equal(dependencies.downloaded.length, 1, "HTML 错误页不应重试最终文件地址");
    assert.deepEqual(dependencies.deleted, ["file:///cache/hb-build-11.apk"], "HTML 半成品仍应清理");
  }

  {
    let fileExists = false;
    const dependencies = createDependencies({
      apiClient: {
        get: async () => ({
          data: {
            easBuildId: "build-11",
            appVersion: "1.0.2",
            appBuildVersion: "11",
            artifactUrl: "https://cos.hotbargain.top/mobile-app-builds/production/build-11.apk",
            buildProfile: "production",
          },
        }),
      },
      getDownloadUrl: () => "https://download.hotbargain.top/mobile-app-builds/android-latest/download?profile=production",
      getFileInfo: async () => ({ exists: fileExists, size: fileExists ? 1024 : undefined }),
      downloadFile: async (url, fileUri) => {
        dependencies.downloaded.push(`${url} -> ${fileUri}`);
        fileExists = true;
        return {
          uri: fileUri,
          status: 503,
          mimeType: "application/vnd.android.package-archive",
        };
      },
      deleteFile: async (fileUri) => {
        dependencies.deleted.push(fileUri);
        fileExists = false;
      },
    });

    await assert.rejects(
      () => checkAndDownloadNativeAppUpdate(dependencies),
      /HTTP 状态码: 503/,
      "稳定入口非 2xx 时不应按证书异常兜底"
    );
    assert.equal(dependencies.downloaded.length, 1, "503 不应重试最终文件地址");
    assert.deepEqual(dependencies.deleted, ["file:///cache/hb-build-11.apk"], "503 半成品仍应清理");
  }

  {
    for (const mimeType of ["application/json", "application/problem+json", "application/problem+xml"]) {
      let fileExists = false;
      const dependencies = createDependencies({
        apiClient: {
          get: async () => ({
            data: {
              easBuildId: "build-11",
              appVersion: "1.0.2",
              appBuildVersion: "11",
              artifactUrl: "https://cos.hotbargain.top/mobile-app-builds/production/build-11.apk",
              buildProfile: "production",
            },
          }),
        },
        getDownloadUrl: () => "https://download.hotbargain.top/mobile-app-builds/android-latest/download?profile=production",
        getFileInfo: async () => ({ exists: fileExists, size: fileExists ? 1024 : undefined }),
        downloadFile: async (url, fileUri) => {
          dependencies.downloaded.push(`${url} -> ${fileUri}`);
          fileExists = true;
          return {
            uri: fileUri,
            status: 200,
            mimeType,
          };
        },
        deleteFile: async (fileUri) => {
          dependencies.deleted.push(fileUri);
          fileExists = false;
        },
      });

      await assert.rejects(
        () => checkAndDownloadNativeAppUpdate(dependencies),
        /文件类型异常/,
        `稳定入口返回 ${mimeType} 时不应按证书异常兜底`
      );
      assert.equal(dependencies.downloaded.length, 1, `${mimeType} 错误响应不应重试最终文件地址`);
      assert.deepEqual(dependencies.deleted, ["file:///cache/hb-build-11.apk"], `${mimeType} 半成品仍应清理`);
    }
  }

  {
    let fileExists = false;
    const dependencies = createDependencies({
      apiClient: {
        get: async () => ({
          data: {
            easBuildId: "build-11",
            appVersion: "1.0.2",
            appBuildVersion: "11",
            artifactUrl: "https://cos.hotbargain.top/mobile-app-builds/production/build-11.apk",
            buildProfile: "production",
          },
        }),
      },
      getDownloadUrl: (build) => getBuildBoundNativeAppDownloadUrl("https://hotbargain.vip/api", build),
      getFileInfo: async () => ({ exists: fileExists, size: fileExists ? 1024 : undefined }),
      downloadFile: async (url, fileUri) => {
        dependencies.downloaded.push(`${url} -> ${fileUri}`);
        fileExists = true;
        return { uri: fileUri, status: 503, mimeType: "application/vnd.android.package-archive" };
      },
      deleteFile: async (fileUri) => {
        dependencies.deleted.push(fileUri);
        fileExists = false;
      },
    });

    await assert.rejects(
      () => checkAndDownloadNativeAppUpdate(dependencies),
      /HTTP 状态码: 503/,
      "非 download.hotbargain.top 稳定入口失败时不应绕过后端入口"
    );
    assert.equal(dependencies.downloaded.length, 1, "hotbargain.vip 入口失败时不应重试最终地址");
    assert.deepEqual(dependencies.deleted, ["file:///cache/hb-build-11.apk"], "失败文件仍应清理");
  }

  {
    let fileExists = false;
    const dependencies = createDependencies({
      apiClient: {
        get: async () => ({
          data: {
            easBuildId: "build-11",
            appVersion: "1.0.2",
            appBuildVersion: "11",
            artifactUrl: "https://download.hotbargain.top/mobile-app-builds/production/build-11.apk",
            buildProfile: "production",
          },
        }),
      },
      getDownloadUrl: () => "https://download.hotbargain.top/mobile-app-builds/android-latest/download?profile=production",
      getFileInfo: async () => ({ exists: fileExists, size: fileExists ? 1024 : undefined }),
      downloadFile: async (url, fileUri) => {
        dependencies.downloaded.push(`${url} -> ${fileUri}`);
        fileExists = true;
        throw new Error("certificate has expired");
      },
      deleteFile: async (fileUri) => {
        dependencies.deleted.push(fileUri);
        fileExists = false;
      },
    });

    await assert.rejects(
      () => checkAndDownloadNativeAppUpdate(dependencies),
      /certificate has expired/,
      "最终地址仍在坏证书域名时不应继续兜底"
    );
    assert.equal(dependencies.downloaded.length, 1, "最终地址仍在坏证书域名时不应重试");
    assert.deepEqual(dependencies.deleted, ["file:///cache/hb-build-11.apk"], "证书失败文件仍应清理");
  }

  {
    const files = createCachedApkFiles([
      ["hb-build-7.apk", 40],
      ["hb-build-8.apk", 30],
      ["hb-build-9.apk", 20],
      ["hb-build-10.apk", 10],
    ]);
    const dependencies = createDependencies({
      readDirectory: async () => [
        "hb-build-7.apk",
        "hb-build-8.apk",
        "hb-build-9.apk",
        "hb-build-10.apk",
        "hb-build-11.apk",
        "other.apk",
        "note.txt",
      ],
      getFileInfo: async (fileUri) => files.get(fileUri) ?? { exists: false },
      downloadFile: async (url, fileUri) => {
        dependencies.downloaded.push(`${url} -> ${fileUri}`);
        files.set(fileUri, { exists: true, size: 1024, isDirectory: false, modificationTime: 1 });
        return { uri: fileUri, status: 200, mimeType: "application/vnd.android.package-archive" };
      },
      deleteFile: async (fileUri) => {
        dependencies.deleted.push(fileUri);
        files.delete(fileUri);
      },
    });
    const result = await checkAndDownloadNativeAppUpdate(dependencies);

    assert.equal(result.status, "downloaded", "下载新版 APK 后仍应返回可安装状态");
    assert.deepEqual(
      dependencies.deleted,
      ["file:///cache/hb-build-9.apk", "file:///cache/hb-build-10.apk"],
      "新版下载后只应删除最旧的本 App APK"
    );
    assert.equal(files.has("file:///cache/hb-build-11.apk"), true, "当前准备安装的 APK 即使更旧也必须保留");
    assert.equal(dependencies.deleted.includes("file:///cache/other.apk"), false, "非 hb-*.apk 文件不应删除");
  }

  {
    const dependencies = createDependencies({
      readDirectory: async () => {
        throw new Error("read directory failed");
      },
    });
    const result = await checkAndDownloadNativeAppUpdate(dependencies);

    assert.equal(result.status, "downloaded", "读目录失败不应阻断安装提示");
    assert.equal(dependencies.downloaded.length, 1, "读目录失败前仍应完成 APK 下载");
    assert.deepEqual(dependencies.deleted, [], "读目录失败时不应误删文件");
  }

  {
    const files = createCachedApkFiles([
      ["hb-build-7.apk", 7],
      ["hb-build-8.apk", 8],
      ["hb-build-9.apk", 9],
      ["hb-build-10.apk", 10],
    ]);
    const dependencies = createDependencies({
      readDirectory: async () => [
        "hb-build-7.apk",
        "hb-build-8.apk",
        "hb-build-9.apk",
        "hb-build-10.apk",
        "hb-build-11.apk",
      ],
      getFileInfo: async (fileUri) => files.get(fileUri) ?? { exists: false },
      downloadFile: async (url, fileUri) => {
        dependencies.downloaded.push(`${url} -> ${fileUri}`);
        files.set(fileUri, { exists: true, size: 1024, isDirectory: false, modificationTime: 11 });
        return { uri: fileUri, status: 200, mimeType: "application/vnd.android.package-archive" };
      },
      deleteFile: async (fileUri) => {
        dependencies.deleted.push(fileUri);
        throw new Error(`delete failed: ${fileUri}`);
      },
    });
    const result = await checkAndDownloadNativeAppUpdate(dependencies);

    assert.equal(result.status, "downloaded", "删除旧 APK 失败不应阻断安装提示");
    assert.equal(files.has("file:///cache/hb-build-11.apk"), true, "删除失败时当前 APK 仍应保留");
    assert.deepEqual(
      dependencies.deleted,
      ["file:///cache/hb-build-8.apk", "file:///cache/hb-build-7.apk"],
      "删除失败也只应尝试清理超额的本 App APK"
    );
  }

  {
    const dependencies = createDependencies({
      getDownloadUrl: (build) => `https://hotbargain.vip/api/mobile-app-builds/android-latest/download?profile=${build.buildProfile}`,
    });
    const result = await checkLegacyNativeAppUpdate(dependencies);

    assert.equal(result.status, "available", "旧 APK OTA 检测到更高 buildVersion 时应提示下载");
    assert.equal(
      result.status === "available" ? result.url : "",
      "https://hotbargain.vip/api/mobile-app-builds/android-latest/download?profile=production",
      "旧 APK OTA 应返回稳定下载入口"
    );
    assert.equal(dependencies.downloaded.length, 0, "旧 APK OTA 不应下载 APK 文件");
  }

  {
    const dependencies = createDependencies({
      apiClient: {
        get: async () => ({
          data: {
            easBuildId: "build-11",
            appVersion: "1.0.2",
            appBuildVersion: "11",
            artifactUrl: "https://cos.hotbargain.top/mobile-app-builds/production/build-11.apk",
            buildProfile: "production",
          },
        }),
      },
      getDownloadUrl: () => "https://download.hotbargain.top/mobile-app-builds/android-latest/download?profile=production",
    });
    const result = await checkLegacyNativeAppUpdate(dependencies);

    assert.equal(result.status, "available", "旧浏览器 download.hotbargain.top 入口应提示更新");
    assert.equal(
      result.status === "available" ? result.url : "",
      "https://cos.hotbargain.top/mobile-app-builds/production/build-11.apk",
      "旧浏览器遇到 download.hotbargain.top 时应跳到最终 APK 地址"
    );
  }

  {
    const dependencies = createDependencies({
      apiClient: {
        get: async () => ({
          data: {
            easBuildId: "build-11",
            appVersion: "1.0.2",
            appBuildVersion: "11",
            artifactUrl: "http://cos.hotbargain.top/mobile-app-builds/production/build-11.apk",
            buildProfile: "production",
          },
        }),
      },
      getDownloadUrl: () => "https://download.hotbargain.top/mobile-app-builds/android-latest/download?profile=production",
    });
    const result = await checkLegacyNativeAppUpdate(dependencies);

    assert.equal(result.status, "not-available", "旧浏览器坏证书入口缺少 HTTPS 最终地址时不应提示下载");
  }

  {
    const unstableUrl = "https://download.hotbargain.top/mobile-app-builds/android-latest/download?profile=production";
    const dependencies = createDependencies({
      apiClient: {
        get: async () => ({
          data: {
            easBuildId: "build-11",
            appVersion: "1.0.2",
            appBuildVersion: "11",
            artifactUrl: unstableUrl,
            buildProfile: "production",
          },
        }),
      },
      getDownloadUrl: () => unstableUrl,
    });
    const result = await checkLegacyNativeAppUpdate(dependencies);

    assert.equal(result.status, "not-available", "旧浏览器最终地址等于坏证书入口时不应原地回退");
  }

  {
    const dependencies = createDependencies({
      apiClient: {
        get: async () => ({
          data: {
            easBuildId: "build-11",
            appVersion: "1.0.2",
            appBuildVersion: "11",
            artifactUrl: "https://download.hotbargain.top/mobile-app-builds/production/build-11.apk",
            buildProfile: "production",
          },
        }),
      },
      getDownloadUrl: () => "https://download.hotbargain.top/mobile-app-builds/android-latest/download?profile=production",
    });
    const result = await checkLegacyNativeAppUpdate(dependencies);

    assert.equal(result.status, "not-available", "旧浏览器最终地址仍在坏证书域名时不应提示下载");
  }

  {
    const dependencies = createDependencies({
      apiClient: {
        get: async () => ({
          data: {
            easBuildId: "build-11",
            appVersion: "1.0.2",
            appBuildVersion: "11",
            artifactUrl: "https://cos.hotbargain.top/mobile-app-builds/production/build-11.apk",
            buildProfile: "production",
          },
        }),
      },
      getDownloadUrl: () => "https://hotbargain.vip/api/mobile-app-builds/android-latest/download?profile=production",
    });
    const result = await checkLegacyNativeAppUpdate(dependencies);

    assert.equal(result.status, "available", "旧浏览器 hotbargain.vip 入口应提示更新");
    assert.equal(
      result.status === "available" ? result.url : "",
      "https://hotbargain.vip/api/mobile-app-builds/android-latest/download?profile=production",
      "旧浏览器 hotbargain.vip 入口应保持稳定入口"
    );
  }

  {
    const dependencies = createDependencies({
      getCurrentBuildVersion: () => "11",
    });
    const result = await checkLegacyNativeAppUpdate(dependencies);

    assert.equal(result.status, "not-available", "旧 APK 当前版本已最新时不应提示下载");
    assert.equal(dependencies.downloaded.length, 0, "旧 APK 无新包时不应下载 APK 文件");
  }

  {
    const dependencies = createDependencies();
    const result = await checkLegacyNativeAppUpdate(dependencies);

    assert.equal(result.status, "not-available", "旧 APK 缺少稳定下载入口时不应回退到 EAS artifact 地址");
    assert.equal(dependencies.downloaded.length, 0, "旧 APK 缺少稳定下载入口时不应下载 APK 文件");
  }

  {
    const dependencies = createDependencies({
      getCurrentBuildVersion: () => "11",
    });
    const result = await checkAndDownloadNativeAppUpdate(dependencies);

    assert.equal(result.status, "not-available", "当前安装包已是最新时不应下载");
    assert.equal(dependencies.downloaded.length, 0, "无新包时不应触发下载");
  }

  {
    const files = createCachedApkFiles([
      ["hb-build-7.apk", 7],
      ["hb-build-8.apk", 8],
      ["hb-build-9.apk", 9],
      ["hb-build-10.apk", 10],
      ["hb-build-11.apk", 11],
    ]);
    const dependencies = createDependencies({
      getCurrentBuildVersion: () => "11",
      readDirectory: async () => [
        "hb-build-7.apk",
        "hb-build-8.apk",
        "hb-build-9.apk",
        "hb-build-10.apk",
        "hb-build-11.apk",
        "other.apk",
      ],
      getFileInfo: async (fileUri) => files.get(fileUri) ?? { exists: false },
      deleteFile: async (fileUri) => {
        dependencies.deleted.push(fileUri);
        files.delete(fileUri);
      },
    });
    const result = await checkAndDownloadNativeAppUpdate(dependencies);

    assert.equal(result.status, "not-available", "无新版 APK 时仍应完成检查");
    assert.equal(dependencies.downloaded.length, 0, "无新版 APK 时不应下载");
    assert.deepEqual(
      dependencies.deleted,
      ["file:///cache/hb-build-8.apk", "file:///cache/hb-build-7.apk"],
      "无新版 APK 时也应只保留最近 3 个本 App APK"
    );
    assert.equal(dependencies.deleted.includes("file:///cache/other.apk"), false, "非本 App APK 不应被清理");
  }

  {
    const dependencies = createDependencies({
      platform: "ios",
    });
    const result = await checkAndDownloadNativeAppUpdate(dependencies);

    assert.equal(result.status, "unsupported-platform", "非 Android 平台不应检查 APK");
    assert.equal(dependencies.downloaded.length, 0, "非 Android 平台不应下载 APK");
  }

  {
    const dependencies = createDependencies({
      getFileInfo: async () => ({ exists: true, size: 1024 }),
    });
    const result = await checkAndDownloadNativeAppUpdate(dependencies);

    assert.equal(result.status, "downloaded", "已缓存 APK 时应直接进入可提示安装状态");
    assert.equal(dependencies.downloaded.length, 0, "已缓存同一 APK 时不应重复下载");
  }

  {
    let fileInfoCalls = 0;
    const dependencies = createDependencies({
      downloadFile: async (url, fileUri) => {
        dependencies.downloaded.push(`${url} -> ${fileUri}`);
        return { uri: fileUri, status: 403, mimeType: "text/html" };
      },
      getFileInfo: async () => ({
        exists: ++fileInfoCalls > 1,
        size: fileInfoCalls > 1 ? 1024 : undefined,
      }),
    });

    await assert.rejects(
      () => checkAndDownloadNativeAppUpdate(dependencies),
      /HTTP 状态码: 403/,
      "下载非 2xx 响应时不应进入已下载状态"
    );
    assert.equal(dependencies.downloaded.length, 1, "应先尝试下载一次");
    assert.deepEqual(dependencies.deleted, ["file:///cache/hb-build-11.apk"], "失败下载文件应被清理");
  }

  {
    let fileInfoCalls = 0;
    const dependencies = createDependencies({
      downloadFile: async (url, fileUri) => {
        dependencies.downloaded.push(`${url} -> ${fileUri}`);
        return { uri: fileUri, status: 200, mimeType: "text/html; charset=utf-8" };
      },
      getFileInfo: async () => ({
        exists: ++fileInfoCalls > 1,
        size: fileInfoCalls > 1 ? 1024 : undefined,
      }),
    });

    await assert.rejects(
      () => checkAndDownloadNativeAppUpdate(dependencies),
      /文件类型异常/,
      "下载到 HTML 错误页时不应提示安装"
    );
    assert.equal(dependencies.downloaded.length, 1, "应先尝试下载一次");
    assert.deepEqual(dependencies.deleted, ["file:///cache/hb-build-11.apk"], "非 APK 下载结果应被清理");
  }

  {
    let fileInfoCalls = 0;
    const dependencies = createDependencies({
      getFileInfo: async () => {
        fileInfoCalls += 1;
        return { exists: true, size: fileInfoCalls === 1 ? 0 : 1024 };
      },
    });

    const result = await checkAndDownloadNativeAppUpdate(dependencies);

    assert.equal(result.status, "downloaded", "已有空文件时应清理并重新下载");
    assert.equal(dependencies.downloaded.length, 1, "空缓存文件应触发重新下载");
    assert.deepEqual(dependencies.deleted, ["file:///cache/hb-build-11.apk"], "空缓存文件应先被删除");
  }

  {
    let fileInfoCalls = 0;
    const dependencies = createDependencies({
      getFileInfo: async () => ({
        exists: ++fileInfoCalls > 1,
        size: 0,
      }),
    });

    await assert.rejects(
      () => checkAndDownloadNativeAppUpdate(dependencies),
      /文件为空或不存在/,
      "下载后文件为空时不应提示安装"
    );
    assert.equal(dependencies.downloaded.length, 1, "应尝试下载一次");
    assert.deepEqual(dependencies.deleted, ["file:///cache/hb-build-11.apk"], "空下载结果应被清理");
  }

  console.log("native-app-update.test.ts: ok");
}

void run();
