import { memo } from "react";
import { StyleSheet, View } from "react-native";
import { Button, IconButton, Text, TextInput } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { newSubItem, type ProductDraft } from "./create-batch-draft";
import { ProductCreationType } from "./types";
import { CreateBatchNumberInput } from "./CreateBatchNumberInput";

export const CreateBatchProductEditor = memo(function CreateBatchProductEditor({ product, index, onChange, onDelete, onSaveTemplate }: {
  product: ProductDraft; index: number; onChange: (key: string, patch: Partial<ProductDraft>) => void; onDelete: (key: string) => void; onSaveTemplate: (product: ProductDraft) => void;
}) {
  const { t } = useAppTranslation("domesticPurchase");
  const expanded = product.subItemsExpanded ?? true;
  const isSet = product.productType === ProductCreationType.Set;
  const order = index * 10000;
  const change = (patch: Partial<ProductDraft>) => onChange(product.key, patch);
  return <View style={styles.card} testID={`creation-product-${index}`}>
    <View style={styles.header}>
      <Text variant="titleSmall" style={styles.heading}>{index + 1}. {t(isSet ? "types.set" : "types.normal")}</Text>
      {isSet ? <Button compact onPress={() => onSaveTemplate(product)}>{t("wizard.saveTemplate")}</Button> : null}
      <IconButton icon="delete-outline" accessibilityLabel={t("wizard.deleteProduct", { row: index + 1 })} onPress={() => onDelete(product.key)} />
    </View>
    <TextInput mode="outlined" dense label={t("fields.productName")} value={product.productName} onChangeText={(productName) => change({ productName })} style={styles.input} />
    <CreateBatchNumberInput id={`${product.key}-price`} order={order} group={product.key} last={!isSet} label={t("fields.privateLabelPriceLabel")} value={product.privateLabelPrice} onChangeText={(privateLabelPrice) => change({ privateLabelPrice })} />
    {isSet ? <>
      <View style={styles.numbers}>
        <CreateBatchNumberInput id={`${product.key}-count`} order={order + 1} group={product.key} label={t("wizard.createCount")} integer value={product.createCount} onChangeText={(createCount) => change({ createCount })} />
        <CreateBatchNumberInput id={`${product.key}-quantity`} order={order + 2} group={product.key} label={t("wizard.setQuantity")} integer value={product.setQuantity} onChangeText={(setQuantity) => change({ setQuantity })} />
      </View>
      <CreateBatchNumberInput id={`${product.key}-set-price`} order={order + 3} group={product.key} last={!expanded || !product.subItems.length} label={t("wizard.setPrice")} value={product.setPrice} onChangeText={(setPrice) => change({ setPrice })} />
      <Button icon={expanded ? "chevron-up" : "chevron-down"} onPress={() => change({ subItemsExpanded: !expanded })} contentStyle={styles.leftButton}>
        {t("wizard.subItems", { count: product.subItems.length })}
      </Button>
      {expanded ? <View style={styles.children}>
        {product.subItems.map((sub, subIndex) => <View key={sub.key} style={styles.child}>
          <View style={styles.header}>
            <Text style={styles.heading}>{t("wizard.subItem", { index: subIndex + 1 })}</Text>
            <IconButton icon="close" size={20} accessibilityLabel={t("wizard.deleteSubItem", { row: subIndex + 1 })} onPress={() => {
              const subItems = product.subItems.filter((item) => item.key !== sub.key);
              change({ subItems, setQuantity: String(Math.max(1, subItems.length)) });
            }} />
          </View>
          <TextInput mode="outlined" dense label={t("fields.productName")} value={sub.productName} onChangeText={(productName) => change({ subItems: product.subItems.map((item) => item.key === sub.key ? { ...item, productName } : item) })} style={styles.input} />
          <CreateBatchNumberInput id={`${sub.key}-price`} order={order + 4 + subIndex} group={product.key} last={subIndex === product.subItems.length - 1} label={t("fields.privateLabelPriceLabel")} value={sub.privateLabelPrice} onChangeText={(privateLabelPrice) => change({ subItems: product.subItems.map((item) => item.key === sub.key ? { ...item, privateLabelPrice } : item) })} />
        </View>)}
        <Button icon="plus" mode="outlined" onPress={() => {
          const subItems = [...product.subItems, newSubItem()];
          change({ subItems, setQuantity: String(subItems.length) });
        }}>{t("wizard.addSubItem")}</Button>
      </View> : null}
    </> : null}
  </View>;
});

const styles = StyleSheet.create({
  card: { borderWidth: 1, borderColor: "#D0D5DD", borderRadius: 10, padding: 12, gap: 8, backgroundColor: "#FFFFFF" },
  header: { flexDirection: "row", alignItems: "center", gap: 4 },
  heading: { flex: 1, fontWeight: "600" },
  input: { backgroundColor: "#FFFFFF" },
  numbers: { flexDirection: "row", flexWrap: "wrap", gap: 8 },
  leftButton: { justifyContent: "flex-start", minHeight: 44 },
  children: { gap: 12, paddingLeft: 10, borderLeftWidth: 2, borderLeftColor: "#91CAFF" },
  child: { gap: 8, paddingBottom: 12, borderBottomWidth: StyleSheet.hairlineWidth, borderColor: "#EAECF0" },
});
