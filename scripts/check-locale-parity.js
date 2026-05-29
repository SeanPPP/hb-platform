const fs = require("fs");
const path = require("path");

const LOCALES_ROOT = path.resolve(__dirname, "../src/locales");
const LOCALE_NAMES = ["zh", "en"];

function readJson(filePath) {
  return JSON.parse(fs.readFileSync(filePath, "utf8"));
}

function flattenKeys(value, prefix = "") {
  if (Array.isArray(value)) {
    return value.flatMap((item, index) => flattenKeys(item, `${prefix}[${index}]`));
  }

  if (value && typeof value === "object") {
    return Object.entries(value).flatMap(([key, nestedValue]) => {
      const nextPrefix = prefix ? `${prefix}.${key}` : key;
      return flattenKeys(nestedValue, nextPrefix);
    });
  }

  return prefix ? [prefix] : [];
}

function listJsonFiles(directory) {
  if (!fs.existsSync(directory)) {
    return [];
  }

  return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      return listJsonFiles(fullPath);
    }
    return entry.isFile() && entry.name.endsWith(".json") ? [fullPath] : [];
  });
}

function toRelativeLocalePath(filePath, localeName) {
  const localeRoot = path.join(LOCALES_ROOT, localeName);
  return path.relative(localeRoot, filePath);
}

function compareLocaleFile(relativePath) {
  const baseFiles = LOCALE_NAMES.map((localeName) => path.join(LOCALES_ROOT, localeName, relativePath));
  const existingFiles = baseFiles.filter((filePath) => fs.existsSync(filePath));

  if (existingFiles.length !== LOCALE_NAMES.length) {
    return {
      relativePath,
      missingFiles: LOCALE_NAMES.filter((localeName) => !fs.existsSync(path.join(LOCALES_ROOT, localeName, relativePath))),
      missingKeys: [],
      extraKeys: [],
    };
  }

  const [zhKeys, enKeys] = LOCALE_NAMES.map((localeName) =>
    new Set(flattenKeys(readJson(path.join(LOCALES_ROOT, localeName, relativePath))))
  );

  const missingInEn = [...zhKeys].filter((key) => !enKeys.has(key));
  const missingInZh = [...enKeys].filter((key) => !zhKeys.has(key));

  return {
    relativePath,
    missingFiles: [],
    missingKeys: missingInEn,
    extraKeys: missingInZh,
  };
}

function main() {
  // 以中文资源文件为主集合，再补上英文独有文件，确保双向都能被校验到。
  const relativePaths = new Set(
    LOCALE_NAMES.flatMap((localeName) =>
      listJsonFiles(path.join(LOCALES_ROOT, localeName)).map((filePath) => toRelativeLocalePath(filePath, localeName))
    )
  );

  const results = [...relativePaths]
    .sort((left, right) => left.localeCompare(right))
    .map(compareLocaleFile)
    .filter((item) => item.missingFiles.length || item.missingKeys.length || item.extraKeys.length);

  if (results.length === 0) {
    console.log("Locale parity check passed.");
    return;
  }

  console.error("Locale parity check failed:");
  results.forEach((item) => {
    console.error(`- ${item.relativePath}`);
    if (item.missingFiles.length) {
      console.error(`  missing files: ${item.missingFiles.join(", ")}`);
    }
    if (item.missingKeys.length) {
      console.error(`  missing in en: ${item.missingKeys.join(", ")}`);
    }
    if (item.extraKeys.length) {
      console.error(`  missing in zh: ${item.extraKeys.join(", ")}`);
    }
  });
  process.exitCode = 1;
}

main();
