const LEGACY_DATS_CODE = 'DATS';
const DATS_BUSINESS_CODE = '240';
const DATS_ORIGIN = 'https://www.dats.com.au/*';

// 仅迁移已知的旧 DATS 内置配置，避免改写同名但来自其他域名的后台供应商。
export function migrateProfileConfig(raw) {
  if (!raw || typeof raw !== 'object' || !Array.isArray(raw.profiles)) return raw;
  let changed = false;
  const profiles = raw.profiles.map((profile) => {
    const isLegacyDats =
      profile
      && profile.supplierCode === LEGACY_DATS_CODE
      && Array.isArray(profile.origins)
      && profile.origins.includes(DATS_ORIGIN);
    if (!isLegacyDats) return profile;
    changed = true;
    return { ...profile, supplierCode: DATS_BUSINESS_CODE };
  });
  return changed ? { ...raw, profiles } : raw;
}
