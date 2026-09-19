import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { SectionList, StyleSheet, View } from "react-native";
import { useRouter } from "expo-router";
import { Button, Dialog, IconButton, Portal, Snackbar, Switch, Text } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { EmptyState } from "@/components/ui/EmptyState";
import { PosterKindTag } from "@/components/promo-posters/PosterKindTag";
import { PosterScreenHeader } from "@/components/promo-posters/PosterScreenHeader";
import { PromoPosterPreview } from "@/components/promo-posters/PromoPosterPreview";
import { usePosterQueueSummary } from "@/components/promo-posters/use-poster-queue-summary";
import { isIosReviewSessionActive } from "@/modules/ios-review/session";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";
import { formatPromoPosterError } from "./format-error";
import {
  buildPromoPosterPdfRequest,
  countPosterPages,
  formatPosterPriceText,
  groupPostersBySize,
} from "./logic";
import { downloadPromoPosterPdf, openPromoPosterPdf, type PromoPosterPdfAction, type PromoPosterPdfFile } from "./pdf";
import { usePromoPosterQueueHydration, usePromoPosterQueueStore } from "./queue-store";
import type { PromoPosterQueueItem, PromoPosterSize } from "./types";

const PRODUCT_QUERY_PATH = "/(shell)/product-query";
const THUMB_WIDTH = 52;
const WARNING_TEXT = "#7A2E0E";

interface QueueSection {
  key: PromoPosterSize;
  size: PromoPosterSize;
  count: number;
  imposedPages: number;
  data: PromoPosterQueueItem[];
}

interface GeneratedResult {
  file: PromoPosterPdfFile;
  pages: number;
  /** 本次 PDF 包含的条目，清空时只清这些。 */
  itemIds: string[];
}

export function PromoPosterQueueScreen() {
  const { t, language } = useAppTranslation(["productQuery", "common"]);
  const router = useRouter();
  const hydrated = usePromoPosterQueueHydration();
  const items = usePromoPosterQueueStore((state) => state.items);
  const impose = usePromoPosterQueueStore((state) => state.impose);
  const setImpose = usePromoPosterQueueStore((state) => state.setImpose);
  const removeItem = usePromoPosterQueueStore((state) => state.remove);
  const removeMany = usePromoPosterQueueStore((state) => state.removeMany);
  const restoreItem = usePromoPosterQueueStore((state) => state.restore);
  const clearQueue = usePromoPosterQueueStore((state) => state.clear);
  const summary = usePosterQueueSummary();
  const review = isIosReviewSessionActive();
  const [generating, setGenerating] = useState(false);
  const [result, setResult] = useState<GeneratedResult | null>(null);
  const [opening, setOpening] = useState<PromoPosterPdfAction | null>(null);
  const [clearConfirmVisible, setClearConfirmVisible] = useState(false);
  const [snackbar, setSnackbar] = useState<{ message: string; undo?: { item: PromoPosterQueueItem; index: number } } | null>(null);
  const mountedRef = useRef(true);
  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  const sections = useMemo<QueueSection[]>(
    () =>
      groupPostersBySize(items).map((group) => ({
        key: group.size,
        size: group.size,
        count: group.count,
        imposedPages: group.imposedPages,
        data: group.items,
      })),
    [items],
  );

  const goBack = useCallback(() => {
    if (router.canGoBack()) router.back();
    else router.replace(PRODUCT_QUERY_PATH as Parameters<typeof router.replace>[0]);
  }, [router]);
  // 与底栏「扫码查询」一致：弹回扫码页（可能隔着编辑页），不在栈里时直接替换过去。
  const continueScan = useCallback(() => {
    router.dismissTo(PRODUCT_QUERY_PATH as Parameters<typeof router.dismissTo>[0]);
  }, [router]);

  const handleRemove = (item: PromoPosterQueueItem) => {
    const index = items.findIndex((candidate) => candidate.id === item.id);
    removeItem(item.id);
    setSnackbar({ message: t("poster.queue.removed"), undo: { item, index } });
  };

  const handleGenerate = async () => {
    if (items.length === 0 || generating) return;
    // 入队时已保证同一分店；PDF 请求只有一个 storeCode。
    const storeCode = items[0].storeCode;
    const posters = items.map((item) => item.poster);
    const itemIds = items.map((item) => item.id);
    setGenerating(true);
    try {
      const file = await downloadPromoPosterPdf(buildPromoPosterPdfRequest(storeCode, impose, posters));
      if (!mountedRef.current) return;
      setResult({
        file,
        pages: file.pageCount ?? countPosterPages(posters.map((poster) => poster.size), impose),
        itemIds,
      });
    } catch (error) {
      if (mountedRef.current) {
        setSnackbar({ message: formatPromoPosterError(error, t, language, "poster.messages.pdfFailed") });
      }
    } finally {
      if (mountedRef.current) setGenerating(false);
    }
  };

  const handleOpen = async (action: PromoPosterPdfAction) => {
    if (!result || opening) return;
    setOpening(action);
    try {
      await openPromoPosterPdf(result.file.fileUri, action);
    } catch (error) {
      if (mountedRef.current) {
        setSnackbar({ message: formatPromoPosterError(error, t, language, "poster.messages.openFailed") });
      }
    } finally {
      if (mountedRef.current) setOpening(null);
    }
  };

  const handleClearAfterPrint = () => {
    if (result) removeMany(result.itemIds);
    setResult(null);
  };

  if (review) {
    return (
      <SafeAreaView style={styles.screen} edges={["top", "left", "right"]}>
        <PosterScreenHeader title={t("poster.queue.title")} onBack={goBack} />
        <View style={styles.centered}>
          <Text style={styles.centeredText}>{t("poster.editor.reviewUnavailable")}</Text>
        </View>
      </SafeAreaView>
    );
  }

  const busy = generating || opening !== null;

  return (
    <SafeAreaView style={styles.screen} edges={["top", "left", "right"]}>
      <PosterScreenHeader
        title={t("poster.queue.title")}
        subtitle={summary.count > 0 ? t("poster.queue.subtitle", { count: summary.count, pages: summary.pagesText }) : undefined}
        onBack={goBack}
        right={
          <Button compact mode="text" onPress={() => setClearConfirmVisible(true)} disabled={items.length === 0 || busy}>
            {t("poster.queue.clear")}
          </Button>
        }
      />
      <SectionList
        sections={hydrated ? sections : []}
        keyExtractor={(item) => item.id}
        stickySectionHeadersEnabled={false}
        contentContainerStyle={styles.content}
        ListHeaderComponent={
          <View style={[styles.card, styles.imposeCard]}>
            <View style={styles.imposeCopy}>
              <Text style={styles.imposeTitle}>{t("poster.queue.imposeTitle")}</Text>
              <Text style={styles.imposeDescription}>{t("poster.queue.imposeDescription")}</Text>
            </View>
            <Switch value={impose} onValueChange={setImpose} disabled={busy} accessibilityLabel={t("poster.queue.imposeTitle")} />
          </View>
        }
        renderSectionHeader={({ section }) => (
          <View style={styles.groupHeader}>
            <Text style={styles.groupTitle}>{t("poster.queue.groupTitle", { size: section.size, count: section.count })}</Text>
            <Text style={styles.groupMeta}>
              {impose && section.size !== "A4"
                ? t("poster.queue.groupImposed", { count: section.imposedPages })
                : t("poster.queue.groupPages", { count: section.count })}
            </Text>
          </View>
        )}
        renderItem={({ item, index, section }) => {
          const isFirst = index === 0;
          const isLast = index === section.data.length - 1;
          return (
            <View style={[styles.itemRow, isFirst ? styles.itemFirst : styles.itemDivider, isLast ? styles.itemLast : null]}>
              <View style={styles.thumb}>
                <PromoPosterPreview data={item.poster} width={THUMB_WIDTH} />
              </View>
              <View style={styles.itemCopy}>
                <Text style={styles.itemTitle} numberOfLines={1}>
                  {item.poster.title}
                </Text>
                <View style={styles.itemMeta}>
                  <PosterKindTag kind={item.poster.kind} />
                  <Text style={styles.itemPrice}>{formatPosterPriceText(item.poster)}</Text>
                  <Text style={styles.itemSub} numberOfLines={1}>
                    {[
                      item.poster.itemNumber ? t("poster.queue.itemNumber", { value: item.poster.itemNumber }) : "",
                      t(`poster.styles.${item.poster.style}`),
                    ]
                      .filter(Boolean)
                      .join(" · ")}
                  </Text>
                </View>
              </View>
              <IconButton
                icon="trash-can-outline"
                size={20}
                iconColor={HB_COLORS.textSecondary}
                disabled={busy}
                onPress={() => handleRemove(item)}
                accessibilityLabel={t("poster.queue.remove")}
                style={styles.removeButton}
              />
            </View>
          );
        }}
        renderSectionFooter={() => <View style={styles.sectionGap} />}
        ListEmptyComponent={
          hydrated ? (
            <View style={styles.empty}>
              <EmptyState title={t("poster.queue.empty")} description={t("poster.queue.emptyHint")} />
            </View>
          ) : null
        }
      />

      <View style={styles.footer}>
        <Button
          mode="outlined"
          icon="barcode-scan"
          onPress={continueScan}
          disabled={generating}
          style={styles.footerButton}
          contentStyle={styles.footerButtonContent}
        >
          {t("poster.queue.continueScan")}
        </Button>
        <Button
          mode="contained"
          icon="file-pdf-box"
          onPress={() => void handleGenerate()}
          loading={generating}
          disabled={items.length === 0 || busy}
          style={styles.footerButton}
          contentStyle={styles.footerButtonContent}
        >
          {generating ? t("poster.queue.generating") : t("poster.queue.generate")}
        </Button>
      </View>

      {/* 页内 Paper Dialog：本页没有原生 Modal，不会出现 BusinessSheet 压住 Portal 的问题。 */}
      <Portal>
        <Dialog visible={clearConfirmVisible} onDismiss={() => setClearConfirmVisible(false)}>
          <Dialog.Title>{t("poster.queue.clearConfirmTitle")}</Dialog.Title>
          <Dialog.Content>
            <Text>{t("poster.queue.clearConfirmBody", { count: items.length })}</Text>
          </Dialog.Content>
          <Dialog.Actions>
            <Button onPress={() => setClearConfirmVisible(false)}>{t("common:actions.cancel")}</Button>
            <Button
              textColor={HB_COLORS.danger}
              onPress={() => {
                clearQueue();
                setClearConfirmVisible(false);
              }}
            >
              {t("poster.queue.clearConfirm")}
            </Button>
          </Dialog.Actions>
        </Dialog>

        <Dialog visible={result !== null} onDismiss={() => setResult(null)}>
          <Dialog.Title>{t("poster.queue.resultTitle")}</Dialog.Title>
          <Dialog.Content style={styles.resultContent}>
            <Text style={styles.resultBody}>
              {result ? t("poster.queue.resultBody", { fileName: result.file.fileName, pages: result.pages }) : ""}
            </Text>
            <View style={styles.tip}>
              <MaterialCommunityIcons name="printer-outline" size={18} color={WARNING_TEXT} />
              <Text style={styles.tipText}>{t("poster.messages.scaleTip")}</Text>
            </View>
            <Button
              mode="contained"
              icon="printer-outline"
              onPress={() => void handleOpen("preview")}
              loading={opening === "preview"}
              disabled={opening !== null}
              style={styles.resultButton}
            >
              {t("poster.queue.preview")}
            </Button>
            <Button
              mode="outlined"
              icon="share-variant-outline"
              onPress={() => void handleOpen("share")}
              loading={opening === "share"}
              disabled={opening !== null}
              style={styles.resultButton}
            >
              {t("poster.queue.share")}
            </Button>
            <Text style={styles.resultQuestion}>{t("poster.queue.resultQuestion")}</Text>
          </Dialog.Content>
          <Dialog.Actions>
            <Button onPress={() => setResult(null)} disabled={opening !== null}>
              {t("poster.queue.keep")}
            </Button>
            <Button onPress={handleClearAfterPrint} disabled={opening !== null}>
              {t("poster.queue.clearAfter")}
            </Button>
          </Dialog.Actions>
        </Dialog>
      </Portal>

      <Snackbar
        visible={snackbar !== null}
        onDismiss={() => setSnackbar(null)}
        duration={4000}
        action={
          snackbar?.undo
            ? {
                label: t("poster.queue.undo"),
                onPress: () => {
                  if (snackbar.undo) restoreItem(snackbar.undo.item, snackbar.undo.index);
                },
              }
            : undefined
        }
      >
        {snackbar?.message ?? ""}
      </Snackbar>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: {
    flex: 1,
    backgroundColor: HB_COLORS.background,
  },
  centered: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    padding: HB_SPACING.lg,
  },
  centeredText: {
    textAlign: "center",
    color: HB_COLORS.textSecondary,
  },
  content: {
    paddingHorizontal: HB_SPACING.md,
    paddingTop: HB_SPACING.sm,
    paddingBottom: HB_SPACING.lg,
  },
  card: {
    backgroundColor: HB_COLORS.white,
    borderWidth: 1,
    borderColor: HB_COLORS.outlineMuted,
    borderRadius: HB_RADIUS.surface,
  },
  imposeCard: {
    flexDirection: "row",
    alignItems: "center",
    gap: HB_SPACING.sm,
    paddingHorizontal: 14,
    paddingVertical: HB_SPACING.sm,
    marginBottom: HB_SPACING.sm,
  },
  imposeCopy: {
    flex: 1,
    gap: 2,
  },
  imposeTitle: {
    fontSize: 14,
    lineHeight: 20,
    fontWeight: "600",
    color: HB_COLORS.textPrimary,
  },
  imposeDescription: {
    fontSize: 12,
    lineHeight: 17,
    color: HB_COLORS.textSecondary,
  },
  groupHeader: {
    flexDirection: "row",
    alignItems: "baseline",
    justifyContent: "space-between",
    paddingHorizontal: HB_SPACING.xxs,
    paddingBottom: 6,
  },
  groupTitle: {
    fontSize: 13,
    fontWeight: "700",
    color: HB_COLORS.textPrimary,
  },
  groupMeta: {
    fontSize: 12,
    color: HB_COLORS.textSecondary,
  },
  sectionGap: {
    height: HB_SPACING.sm,
  },
  // 每组用首尾行的圆角和边框拼出一张卡片，保持 SectionList 虚拟化。
  itemRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: HB_SPACING.sm,
    paddingVertical: 10,
    paddingLeft: HB_SPACING.sm,
    paddingRight: HB_SPACING.xxs,
    backgroundColor: HB_COLORS.white,
    borderLeftWidth: 1,
    borderRightWidth: 1,
    borderColor: HB_COLORS.outlineMuted,
  },
  itemFirst: {
    borderTopWidth: 1,
    borderTopLeftRadius: HB_RADIUS.surface,
    borderTopRightRadius: HB_RADIUS.surface,
  },
  itemDivider: {
    borderTopWidth: StyleSheet.hairlineWidth,
  },
  itemLast: {
    borderBottomWidth: 1,
    borderBottomLeftRadius: HB_RADIUS.surface,
    borderBottomRightRadius: HB_RADIUS.surface,
  },
  thumb: {
    shadowColor: "#101828",
    shadowOpacity: 0.18,
    shadowRadius: 2,
    shadowOffset: { width: 0, height: 1 },
    elevation: 1,
    backgroundColor: HB_COLORS.white,
  },
  itemCopy: {
    flex: 1,
    minWidth: 0,
    gap: HB_SPACING.xxs,
  },
  itemTitle: {
    fontSize: 14,
    lineHeight: 19,
    fontWeight: "600",
    color: HB_COLORS.textPrimary,
  },
  itemMeta: {
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
  },
  itemPrice: {
    fontSize: 13,
    fontWeight: "700",
    color: HB_COLORS.textPrimary,
    fontVariant: ["tabular-nums"],
  },
  itemSub: {
    flex: 1,
    minWidth: 0,
    fontSize: 12,
    color: HB_COLORS.textSecondary,
  },
  removeButton: {
    margin: 0,
  },
  empty: {
    paddingTop: HB_SPACING.lg,
  },
  footer: {
    flexDirection: "row",
    gap: 10,
    paddingHorizontal: HB_SPACING.md,
    paddingVertical: HB_SPACING.sm,
    backgroundColor: HB_COLORS.white,
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: HB_COLORS.outlineMuted,
  },
  footerButton: {
    flex: 1,
    borderRadius: HB_RADIUS.control,
  },
  footerButtonContent: {
    minHeight: 44,
  },
  resultContent: {
    gap: 10,
  },
  resultBody: {
    color: HB_COLORS.textPrimary,
  },
  tip: {
    flexDirection: "row",
    alignItems: "flex-start",
    gap: HB_SPACING.xs,
    paddingHorizontal: HB_SPACING.sm,
    paddingVertical: 10,
    borderRadius: HB_RADIUS.control,
    backgroundColor: "#FFFAEB",
  },
  tipText: {
    flex: 1,
    fontSize: 13,
    lineHeight: 19,
    color: WARNING_TEXT,
  },
  resultButton: {
    borderRadius: HB_RADIUS.control,
  },
  resultQuestion: {
    fontSize: 12,
    lineHeight: 17,
    color: HB_COLORS.textSecondary,
  },
});
