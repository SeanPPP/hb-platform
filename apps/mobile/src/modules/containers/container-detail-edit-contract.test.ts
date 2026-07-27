import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const directory = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(directory, "container-detail-screen.tsx"), "utf8");
const compact = (value: string) => value.replace(/\s+/g, " ").trim();

function extract(startMarker: string, endMarker: string) {
  const start = source.indexOf(startMarker);
  const end = source.indexOf(endMarker, start);
  assert.ok(start >= 0 && end > start, `应能隔离源码：${startMarker}`);
  return source.slice(start, end);
}

const updateMutation = compact(extract(
  "const updateMutation = useMutation({",
  "const bulkMutation = useMutation({",
));
const editModal = compact(extract(
  '<Modal visible={Boolean(editingDetail && editForm)}',
  '<Modal visible={Boolean(bulkModalType)}',
));

assert.match(source, /const \[editEnglishNameError, setEditEnglishNameError\] = useState\(""\)/);
assert.match(source, /function closeEditModal\(\)/);
assert.match(source, /function openEditModal\(detail: ContainerDetail\)/);
assert.match(source, /function handleEditEnglishNameChange\(value: string\)/);

assert.ok(
  updateMutation.includes("onSuccess: (result) =>"),
  "保存成功回调必须读取 batch-update-details 结构化结果",
);
assert.ok(
  updateMutation.includes('error.field === "英文名称"'),
  "必须仅把英文名称字段校验错误映射到英文名称输入框",
);
assert.ok(
  updateMutation.includes('error.field === "*"') &&
    updateMutation.includes("setSnackbar(rowError.message);"),
  "整行失效必须显示表单级提示，不能伪装成英文名称输入错误",
);
assert.match(
  updateMutation,
  /if \(rowError\) \{[\s\S]*closeEditModal\(\);[\s\S]*setSnackbar\(rowError\.message\);[\s\S]*return;[\s\S]*\}/,
  "整行失效后必须关闭不可恢复的编辑弹窗并阻止成功提示",
);
assert.ok(
  updateMutation.includes("invalidateDetail();"),
  "同一请求中其它字段可能已保存，收到字段错误时也必须刷新详情",
);
assert.ok(
  updateMutation.includes("setEditEnglishNameError(englishNameError.message);"),
  "后端英文名称错误必须写入字段错误状态",
);
assert.match(
  updateMutation,
  /if \(englishNameError\) \{[\s\S]*setEditEnglishNameError\(englishNameError\.message\);[\s\S]*return;[\s\S]*\}[\s\S]*closeEditModal\(\);[\s\S]*setSnackbar\("明细已保存"\);/,
  "字段错误必须保留弹窗和草稿；无字段错误时才关闭并显示全成功",
);

assert.ok(
  editModal.includes("onDismiss={closeEditModal}"),
  "关闭编辑弹窗必须统一清理旧字段错误",
);
assert.ok(
  editModal.includes('label="英文名称"'),
  "编辑弹窗必须保留英文名称输入",
);
assert.ok(
  editModal.includes("error={Boolean(editEnglishNameError)}"),
  "英文名称输入必须进入 react-native-paper 错误态",
);
assert.ok(
  editModal.includes('<HelperText type="error" visible={Boolean(editEnglishNameError)}>'),
  "英文名称输入下方必须显示 react-native-paper HelperText",
);
assert.ok(
  editModal.includes("{editEnglishNameError}"),
  "HelperText 必须展示后端原始错误信息",
);
assert.ok(
  editModal.includes("onChangeText={handleEditEnglishNameChange}"),
  "修改英文名称时必须经过清错处理",
);
assert.ok(
  editModal.includes("<Button onPress={closeEditModal}>取消</Button>"),
  "取消编辑必须统一清理旧字段错误",
);
assert.match(
  source,
  /function closeEditModal\(\) \{[\s\S]*setEditEnglishNameError\(""\);[\s\S]*setEditingDetail\(null\);[\s\S]*setEditForm\(null\);[\s\S]*\}/,
  "关闭弹窗必须清理错误、当前明细和表单",
);
assert.match(
  source,
  /function openEditModal\(detail: ContainerDetail\) \{[\s\S]*setEditEnglishNameError\(""\);[\s\S]*setEditingDetail\(detail\);[\s\S]*setEditForm\(buildEditForm\(detail\)\);[\s\S]*\}/,
  "重新打开编辑必须从干净错误状态开始",
);
assert.match(
  source,
  /function handleEditEnglishNameChange\(value: string\) \{[\s\S]*setEditEnglishNameError\(""\);[\s\S]*englishName: value[\s\S]*\}/,
  "用户修改英文名称时必须立即清理旧服务端错误并保留新输入",
);
assert.match(
  source,
  /onEdit=\{\(\) => openEditModal\(detail\)\}/,
  "每次打开编辑必须使用统一的清错入口",
);

console.log("container-detail-edit-contract.test.ts: ok");
