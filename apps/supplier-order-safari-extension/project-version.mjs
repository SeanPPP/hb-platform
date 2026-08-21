const WEB_EXTENSION_VERSION_PATTERN = /^\d+(?:\.\d+){0,3}$/;

function replaceBuildSetting(projectSource, settingName, version) {
  const settingPattern = new RegExp(`(${settingName}\\s*=\\s*)[^;\\n]+;`, 'g');
  let replacementCount = 0;
  const updated = projectSource.replace(settingPattern, (_match, prefix) => {
    replacementCount += 1;
    return `${prefix}${version};`;
  });

  if (replacementCount === 0) {
    throw new Error(`Xcode 项目缺少 ${settingName}`);
  }
  return updated;
}

export function synchronizeXcodeProjectVersions(projectSource, version) {
  const normalizedVersion = String(version || '').trim();
  if (!WEB_EXTENSION_VERSION_PATTERN.test(normalizedVersion)) {
    throw new Error(`Safari 扩展版本格式无效: ${version}`);
  }

  // 商店展示版本与构建版本均跟随扩展 manifest，避免三个发布位置发生漂移。
  const withMarketingVersion = replaceBuildSetting(
    projectSource,
    'MARKETING_VERSION',
    normalizedVersion,
  );
  return replaceBuildSetting(
    withMarketingVersion,
    'CURRENT_PROJECT_VERSION',
    normalizedVersion,
  );
}
