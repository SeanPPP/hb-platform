import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import test from 'node:test';

const sourcePath = new URL('../node_modules/expo-updates/android/src/main/java/expo/modules/updates/launcher/DatabaseLauncher.kt', import.meta.url);
const source = readFileSync(sourcePath, 'utf8');
const signature = 'suspend fun getLaunchableUpdate(database: UpdatesDatabase): UpdateEntity? {';
const start = source.indexOf(signature);
assert.notEqual(start, -1, 'patched DatabaseLauncher method not found');
let depth = 0;
let end = -1;
for (let index = source.indexOf('{', start); index < source.length; index += 1) {
  if (source[index] === '{') depth += 1;
  if (source[index] === '}') {
    depth -= 1;
    if (depth === 0) { end = index + 1; break; }
  }
}
assert.notEqual(end, -1, 'could not extract patched DatabaseLauncher method');
const actualMethod = source.slice(start, end);
const patchText = readFileSync(new URL('../patches/expo-updates+29.0.20.patch', import.meta.url), 'utf8');
assert.equal((patchText.match(/^diff --git /gm) ?? []).length, 1, 'OTA patch must contain only DatabaseLauncher.kt');
test('native patch keeps exact embedded identity and existing launch gates', () => {
  assert.match(patchText, /^diff --git a\/node_modules\/expo-updates\/android\/src\/main\/java\/expo\/modules\/updates\/launcher\/DatabaseLauncher\.kt /m);
  assert.match(actualMethod, /configuration\.hasEmbeddedUpdate && configuration\.updateUrl == configuration\.originalEmbeddedUpdateUrl/);
  assert.match(actualMethod, /it\.id == currentEmbeddedId/);
  assert.match(actualMethod, /it\.runtimeVersion == configuration\.getRuntimeVersion\(\)/);
  assert.match(actualMethod, /SelectionPolicies\.matchesFilters\(it, manifestFilters\)/);
  assert.ok(actualMethod.indexOf('selectionPolicy.selectUpdateToLaunch') < actualMethod.indexOf('val currentEmbeddedId'));
  assert.match(actualMethod, /loadLaunchableUpdatesForScope/);
});

const harness = `
package harness
import kotlin.coroutines.*
data class Uri(val value: String)
class Context
class JSONObject(val value: String)
enum class UpdateStatus { PENDING, READY, EMBEDDED }
class UpdateEntity(val id: String, val runtimeVersion: String, val status: UpdateStatus, val commitTime: Long, val manifestFilter: String)
class UpdatesConfiguration(val scopeKey: String, val hasEmbeddedUpdate: Boolean, val updateUrl: Uri, val originalEmbeddedUpdateUrl: Uri, private val runtime: String) { fun getRuntimeVersion() = runtime }
class UpdateDao(private val updates: List<UpdateEntity>) { suspend fun loadLaunchableUpdatesForScope(scope: String) = updates.filter { it.status == UpdateStatus.READY || it.status == UpdateStatus.EMBEDDED } }
class UpdatesDatabase(private val dao: UpdateDao) { fun updateDao() = dao }
class EmbeddedUpdate(val updateEntity: UpdateEntity)
object EmbeddedManifestUtils { var current: EmbeddedUpdate? = null; fun getOriginalEmbeddedUpdate(context: Context, configuration: UpdatesConfiguration) = current }
object ManifestMetadata { var filters: JSONObject? = null; fun getManifestFilters(database: UpdatesDatabase, configuration: UpdatesConfiguration) = filters }
object SelectionPolicies { fun matchesFilters(update: UpdateEntity, filters: JSONObject?) = filters == null || update.manifestFilter == filters.value }
interface SelectionPolicy { fun selectUpdateToLaunch(updates: List<UpdateEntity>, filters: JSONObject?): UpdateEntity? }
class NullSelectionPolicy : SelectionPolicy { override fun selectUpdateToLaunch(updates: List<UpdateEntity>, filters: JSONObject?) = updates.firstOrNull { it.status == UpdateStatus.READY && it.manifestFilter == filters?.value && it.id.startsWith("ota-") } }
class DatabaseLauncherHarness(private val context: Context, private val configuration: UpdatesConfiguration, private val selectionPolicy: SelectionPolicy) {
${actualMethod}
}
fun runSuspend(block: suspend () -> UpdateEntity?): UpdateEntity? { var result: Result<UpdateEntity?>? = null; block.startCoroutine(object : Continuation<UpdateEntity?> { override val context = EmptyCoroutineContext; override fun resumeWith(value: Result<UpdateEntity?>) { result = value } }); return result!!.getOrThrow() }
fun main() {
  val context = Context(); val config = UpdatesConfiguration("scope", true, Uri("embedded"), Uri("embedded"), "1.0.6")
  val embedded = UpdateEntity("embedded", "1.0.6", UpdateStatus.EMBEDDED, 10, "filters"); EmbeddedManifestUtils.current = EmbeddedUpdate(embedded); ManifestMetadata.filters = JSONObject("filters")
  fun launch(updates: List<UpdateEntity>, configuration: UpdatesConfiguration = config) = runSuspend { DatabaseLauncherHarness(context, configuration, NullSelectionPolicy()).getLaunchableUpdate(UpdatesDatabase(UpdateDao(updates))) }
  check(launch(listOf(embedded, UpdateEntity("ota-new", "1.0.6", UpdateStatus.READY, 2, "filters")))?.id == "ota-new") { "matching OTA remains preferred even when older than embedded" }
  check(launch(listOf(UpdateEntity("old-channel", "1.0.6", UpdateStatus.READY, 3, "filters"), embedded))?.id == "embedded") { "old channel fallback" }
  check(launch(listOf(UpdateEntity("embedded", "1.0.6", UpdateStatus.READY, 4, "filters")))?.id == "embedded") { "READY embedded fallback" }
  check(launch(listOf(UpdateEntity("old-id", "1.0.6", UpdateStatus.READY, 4, "filters"))) == null) { "old ID rejected" }
  check(launch(listOf(UpdateEntity("embedded", "1.0.5", UpdateStatus.READY, 4, "filters"))) == null) { "wrong runtime" }
  ManifestMetadata.filters = JSONObject("other"); check(launch(listOf(embedded)) == null) { "wrong filters" }; ManifestMetadata.filters = JSONObject("filters")
  check(launch(listOf(embedded), UpdatesConfiguration("scope", false, Uri("embedded"), Uri("embedded"), "1.0.6")) == null) { "embedded disabled" }
  check(launch(listOf(UpdateEntity("embedded", "1.0.6", UpdateStatus.PENDING, 4, "filters"))) == null) { "pending rejected" }
  check(launch(listOf(embedded), UpdatesConfiguration("scope", true, Uri("override"), Uri("embedded"), "1.0.6")) == null) { "overridden URL rejected" }
  check(launch(listOf(UpdateEntity("old-id", "1.0.6", UpdateStatus.EMBEDDED, 4, "filters"))) == null) { "old embedded ID rejected" }
  println("DatabaseLauncher patched method harness: 10 passed")
}
`;

// Node CI 不要求 Android 工具链；有显式 KOTLINC 或 PATH 编译器时必须运行真实 Kotlin 方法。
const compiler = process.env.KOTLINC || 'kotlinc';
const probe = spawnSync(compiler, ['-version'], { encoding: 'utf8' });
const compilerUnavailable = !process.env.KOTLINC && probe.error?.code === 'ENOENT';
test('actual DatabaseLauncher method passes 10 JVM scenarios', {
  skip: compilerUnavailable ? 'Kotlin compiler unavailable; source/patch contract was checked' : false,
}, () => {
  assert.equal(probe.status, 0, probe.error?.message || probe.stderr || probe.stdout);
  const dir = mkdtempSync(join(tmpdir(), 'hb-expo-updates-launcher-'));
  try {
    const sourceFile = join(dir, 'LauncherHarness.kt');
    const jarFile = join(dir, 'launcher-harness.jar');
    writeFileSync(sourceFile, harness);
    const compile = spawnSync(compiler, [sourceFile, '-include-runtime', '-d', jarFile], { encoding: 'utf8' });
    assert.equal(compile.status, 0, compile.error?.message || compile.stderr || compile.stdout);
    const run = spawnSync('java', ['-jar', jarFile], { encoding: 'utf8' });
    assert.equal(run.status, 0, run.error?.message || run.stderr || run.stdout);
    assert.match(run.stdout, /10 passed/);
    console.log(run.stdout.trim());
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
