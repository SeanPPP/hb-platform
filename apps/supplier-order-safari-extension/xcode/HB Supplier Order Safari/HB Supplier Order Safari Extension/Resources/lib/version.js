// 语义化版本解析与比较（构建/发布版本状态判断用）
export function parseSemver(v) {
  if (typeof v !== 'string') return null;
  const m = /^(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?$/.exec(v.trim());
  if (!m) return null;
  return {
    major: Number(m[1]),
    minor: Number(m[2]),
    patch: Number(m[3]),
    prerelease: m[4] || null,
    raw: v.trim(),
  };
}

export function isValidSemver(v) {
  return parseSemver(v) !== null;
}

export function compareSemver(a, b) {
  const A = parseSemver(a);
  const B = parseSemver(b);
  if (!A || !B) throw new Error('invalid semver');
  for (const k of ['major', 'minor', 'patch']) {
    if (A[k] !== B[k]) return A[k] < B[k] ? -1 : 1;
  }
  if (!A.prerelease && B.prerelease) return 1;
  if (A.prerelease && !B.prerelease) return -1;
  if (A.prerelease && B.prerelease) {
    if (A.prerelease === B.prerelease) return 0;
    return A.prerelease < B.prerelease ? -1 : 1;
  }
  return 0;
}

// 根据 release 返回版本状态：blocked(低于最低要求) / update-available(可更新) / latest(已最新)
export function evaluateReleaseStatus({ currentVersion, latestVersion, minVersion }) {
  if (minVersion && compareSemver(currentVersion, minVersion) < 0) return 'blocked';
  if (latestVersion && compareSemver(currentVersion, latestVersion) < 0) return 'update-available';
  return 'latest';
}

export function assertSameVersion(chromeVersion, edgeVersion) {
  return compareSemver(chromeVersion, edgeVersion) === 0;
}
