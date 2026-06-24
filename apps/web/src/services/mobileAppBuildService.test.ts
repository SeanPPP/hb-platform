import { normalizeMobileAppBuild } from './mobileAppBuildService'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}: expected ${String(expected)}, got ${String(actual)}`)
  }
}

const build = normalizeMobileAppBuild({
  id: 'build-id',
  easBuildId: 'eas-build-id',
  artifactUrl: 'https://cos.example/hb.apk',
  originalArtifactUrl: 'https://expo.dev/artifacts/hb.apk',
  cosArtifactUrl: 'https://cos.example/hb.apk',
  cosObjectKey: 'mobile-app-builds/android/eas-build-id.apk',
  cosMirroredAt: '2026-06-23T01:02:03Z',
  cosMirrorError: 'mirror failed',
  cosMirrorStatus: 'failed',
  cosMirrorAttempts: '2',
  cosMirrorLastAttemptAtUtc: '2026-06-23T01:01:00Z',
})

assertEqual(build.artifactUrl, 'https://cos.example/hb.apk', '下载/复制应继续使用后端 artifactUrl')
assertEqual(build.originalArtifactUrl, 'https://expo.dev/artifacts/hb.apk', 'normalizer 应保留原始 artifactUrl')
assertEqual(build.cosArtifactUrl, 'https://cos.example/hb.apk', 'normalizer 应保留 COS 镜像 URL')
assertEqual(build.cosObjectKey, 'mobile-app-builds/android/eas-build-id.apk', 'normalizer 应保留 COS object key')
assertEqual(build.cosMirroredAt, '2026-06-23T01:02:03Z', 'normalizer 应保留 COS 镜像时间')
assertEqual(build.cosMirrorError, 'mirror failed', 'normalizer 应保留 COS 镜像错误')
assertEqual(build.cosMirrorStatus, 'failed', 'normalizer 应保留 COS 镜像状态')
assertEqual(build.cosMirrorAttempts, 2, 'normalizer 应保留 COS 镜像尝试次数')
assertEqual(build.cosMirrorLastAttemptAtUtc, '2026-06-23T01:01:00Z', 'normalizer 应保留 COS 上次尝试时间')

console.log('mobileAppBuildService.test.ts: ok')
