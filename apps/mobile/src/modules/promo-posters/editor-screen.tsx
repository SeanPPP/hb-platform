import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { KeyboardAvoidingView, Platform, ScrollView, StyleSheet, View, useWindowDimensions } from "react-native";
import { useQuery } from "@tanstack/react-query";
import { useLocalSearchParams, useRouter } from "expo-router";
import {
  ActivityIndicator,
  Button,
  Chip,
  Dialog,
  HelperText,
  Portal,
  Snackbar,
  Switch,
  Text,
  TextInput,
} from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { MonthDatePickerField } from "@/components/attendance/MonthDatePicker";
import { PosterQueueBadgeButton, PosterScreenHeader } from "@/components/promo-posters/PosterScreenHeader";
import { PosterSegmented } from "@/components/promo-posters/PosterSegmented";
import { PromoPosterPreview, type PromoPosterPreviewData } from "@/components/promo-posters/PromoPosterPreview";
import { isIosReviewSessionActive } from "@/modules/ios-review/session";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";
import { getPromoPosterDefaults } from "./api";
import { formatPromoPosterError } from "./format-error";
import {
  addDaysToDateOnly,
  applyMultiBuyOffer,
  applyPosterKind,
  buildPosterSpec,
  buildPromoPosterPdfRequest,
  createPosterDraft,
  formatDateOnly,
  formatPosterMoney,
  normalizePosterTitle,
  parsePosterPrice,
  PROMO_POSTER_PER_A4,
  PROMO_POSTER_QUEUE_LIMIT,
  PROMO_POSTER_SIZE_MM,
  resolveDefaultsAvailability,
  resolveInitialPosterKind,
  resolvePriceMismatch,
  type PosterFieldErrorCode,
  type PosterTitleError,
  type PromoPosterDraftErrors,
} from "./logic";
import { downloadPromoPosterPdf, openPromoPosterPdf } from "./pdf";
import { usePromoPosterQueueHydration, usePromoPosterQueueStore } from "./queue-store";
import {
  PROMO_POSTER_KINDS,
  PROMO_POSTER_SIZES,
  PROMO_POSTER_STYLES,
  type PromoPosterDefaults,
  type PromoPosterDraft,
  type PromoPosterKind,
  type PromoPosterSize,
  type PromoPosterSpec,
  type PromoPosterStyle,
} from "./types";

const PRODUCT_QUERY_PATH = "/(shell)/product-query";
const QUEUE_PATH = "/(shell)/promo-poster-queue";
/** 特价有效期默认两周（含当天）。 */
const DEFAULT_VALIDITY_DAYS = 13;

const first = (value: string | string[] | undefined) => (Array.isArray(value) ? value[0] : value)?.trim() || "";

export function PromoPosterEditorScreen() {
  const { t, language } = useAppTranslation(["productQuery", "common"]);
  const router = useRouter();
  const params = useLocalSearchParams<{
    productCode?: string | string[];
    storeCode?: string | string[];
    kind?: string | string[];
  }>();
  const productCode = first(params.productCode);
  const storeCode = first(params.storeCode);
  const requestedKind = first(params.kind);
  // 审核演示会话完全离线，海报需要后端生成 PDF，直接不开放。
  const review = isIosReviewSessionActive();
  const hydrated = usePromoPosterQueueHydration();
  const goBack = useCallback(() => {
    if (router.canGoBack()) router.back();
    else router.replace(PRODUCT_QUERY_PATH as Parameters<typeof router.replace>[0]);
  }, [router]);

  const defaultsQuery = useQuery({
    queryKey: ["promoPosters", "defaults", storeCode, productCode],
    queryFn: ({ signal }) => getPromoPosterDefaults(storeCode, productCode, signal),
    enabled: Boolean(productCode && storeCode) && !review,
    // 价格随时可能被改：不复用缓存（gcTime 0），草稿只用本次进入后读到的数据初始化。
    staleTime: 0,
    gcTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
  });
  const defaults = defaultsQuery.isFetchedAfterMount ? defaultsQuery.data : undefined;

  let body: ReactNode;
  if (review) {
    body = <CenteredMessage message={t("poster.editor.reviewUnavailable")} />;
  } else if (!productCode || !storeCode) {
    body = <CenteredMessage message={t("poster.editor.missingParams")} />;
  } else if (defaults && hydrated) {
    return (
      <PosterEditorForm
        key={`${storeCode}:${productCode}`}
        defaults={defaults}
        storeCode={storeCode}
        requestedKind={requestedKind}
        onBack={goBack}
      />
    );
  } else if (defaultsQuery.isError) {
    body = (
      <CenteredMessage
        message={formatPromoPosterError(defaultsQuery.error, t, language, "poster.editor.loadFailed")}
        actionLabel={t("poster.editor.retry")}
        onAction={() => void defaultsQuery.refetch()}
      />
    );
  } else {
    body = <CenteredMessage message={t("poster.editor.loading")} loading />;
  }

  return (
    <SafeAreaView style={styles.screen} edges={["top", "left", "right"]}>
      <PosterScreenHeader title={t("poster.editor.title")} onBack={goBack} />
      {body}
    </SafeAreaView>
  );
}

function CenteredMessage({
  message,
  loading = false,
  actionLabel,
  onAction,
}: {
  message: string;
  loading?: boolean;
  actionLabel?: string;
  onAction?: () => void;
}) {
  return (
    <View style={styles.centered}>
      {loading ? <ActivityIndicator /> : null}
      <Text style={styles.centeredText} accessibilityLiveRegion="polite">
        {message}
      </Text>
      {actionLabel && onAction ? (
        <Button mode="outlined" onPress={onAction}>
          {actionLabel}
        </Button>
      ) : null}
    </View>
  );
}

interface PosterEditorFormProps {
  defaults: PromoPosterDefaults;
  storeCode: string;
  requestedKind: string;
  onBack: () => void;
}

function PosterEditorForm({ defaults, storeCode, requestedKind, onBack }: PosterEditorFormProps) {
  const { t, language } = useAppTranslation(["productQuery", "common"]);
  const router = useRouter();
  const { width: windowWidth } = useWindowDimensions();
  const queueCount = usePromoPosterQueueStore((state) => state.items.length);
  const impose = usePromoPosterQueueStore((state) => state.impose);
  const showLogo = usePromoPosterQueueStore((state) => state.showLogo);
  const setShowLogo = usePromoPosterQueueStore((state) => state.setShowLogo);
  const addToQueue = usePromoPosterQueueStore((state) => state.add);
  const replaceQueue = usePromoPosterQueueStore((state) => state.replaceAll);
  const rememberStyle = usePromoPosterQueueStore((state) => state.setStyle);
  const rememberSize = usePromoPosterQueueStore((state) => state.setSize);
  const availability = useMemo(() => resolveDefaultsAvailability(defaults), [defaults]);
  const today = useMemo(() => formatDateOnly(new Date()), []);
  const [draft, setDraft] = useState<PromoPosterDraft>(() => {
    const remembered = usePromoPosterQueueStore.getState();
    return createPosterDraft(defaults, {
      kind: resolveInitialPosterKind(requestedKind, availability),
      style: remembered.style,
      size: remembered.size,
      today,
    });
  });
  // 「必填」类错误只在店员尝试提交后显示；格式错误（中文字符、价格格式等）实时显示。
  const [showRequired, setShowRequired] = useState(false);
  const [printing, setPrinting] = useState(false);
  const [snackbar, setSnackbar] = useState<string | null>(null);
  const [conflictSpec, setConflictSpec] = useState<PromoPosterSpec | null>(null);
  const mountedRef = useRef(true);
  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  const result = useMemo(() => buildPosterSpec(draft, defaults), [defaults, draft]);
  const errors: PromoPosterDraftErrors = result.ok ? {} : result.errors;
  const mismatch = resolvePriceMismatch(draft, defaults);
  const visible = (code: PosterFieldErrorCode | undefined) => (code && (code !== "required" || showRequired) ? code : undefined);

  const setField = (patch: Partial<PromoPosterDraft>) => setDraft((current) => ({ ...current, ...patch }));
  const handleKind = (kind: PromoPosterKind) => {
    if (kind === draft.kind) return;
    setDraft((current) => applyPosterKind(current, defaults, kind, today));
    setShowRequired(false);
  };
  const handleStyle = (style: PromoPosterStyle) => {
    setField({ style });
    rememberStyle(style);
  };
  const handleSize = (size: PromoPosterSize) => {
    setField({ size });
    rememberSize(size);
  };

  const validityEnabled = Boolean(draft.validFrom || draft.validTo);
  const toggleValidity = (enabled: boolean) =>
    setField(enabled ? { validFrom: today, validTo: addDaysToDateOnly(today, DEFAULT_VALIDITY_DAYS) } : { validFrom: "", validTo: "" });

  const previewData = useMemo<PromoPosterPreviewData>(() => {
    const quantity = Number(draft.quantity);
    return {
      kind: draft.kind,
      style: draft.style,
      size: draft.size,
      title: normalizePosterTitle(draft.title),
      itemNumber: defaults.itemNumber,
      price: parsePosterPrice(draft.price),
      wasPrice: parsePosterPrice(draft.wasPrice),
      quantity: Number.isInteger(quantity) && quantity > 0 ? quantity : null,
      unitPrice: parsePosterPrice(draft.unitPrice),
      mixAndMatch: draft.mixAndMatch,
      validFrom: draft.validFrom,
      validTo: draft.validTo,
      inStoreSince: draft.inStoreSince,
      showLogo,
    };
  }, [defaults.itemNumber, draft, showLogo]);

  const validSpec = () => {
    if (result.ok) return result.spec;
    setShowRequired(true);
    setSnackbar(t("poster.errors.fixBeforeSubmit"));
    return null;
  };

  const handleAdd = () => {
    const spec = validSpec();
    if (!spec) return;
    const input = { storeCode, productName: defaults.productName, poster: spec };
    const outcome = addToQueue(input);
    if (outcome === "ok") {
      // 回扫码页继续扫下一个商品，页面底部浮条会显示最新张数。
      onBack();
    } else if (outcome === "full") {
      setSnackbar(t("poster.messages.queueFull", { max: PROMO_POSTER_QUEUE_LIMIT }));
    } else {
      setConflictSpec(spec);
    }
  };

  const confirmReplaceQueue = () => {
    if (!conflictSpec) return;
    replaceQueue({ storeCode, productName: defaults.productName, poster: conflictSpec });
    setConflictSpec(null);
    onBack();
  };

  const handlePrintNow = async () => {
    const spec = validSpec();
    if (!spec || printing) return;
    setPrinting(true);
    try {
      const file = await downloadPromoPosterPdf(buildPromoPosterPdfRequest(storeCode, impose, [spec], showLogo));
      await openPromoPosterPdf(file.fileUri, "preview");
      if (mountedRef.current) setSnackbar(t("poster.messages.scaleTip"));
    } catch (error) {
      if (mountedRef.current) setSnackbar(formatPromoPosterError(error, t, language, "poster.messages.pdfFailed"));
    } finally {
      if (mountedRef.current) setPrinting(false);
    }
  };

  const unavailableText = PROMO_POSTER_KINDS.filter((kind) => !availability[kind])
    .map((kind) => t("poster.unavailableItem", { kind: t(`poster.kinds.${kind}`), reason: t(`poster.unavailable.${kind}`) }))
    .join(t("poster.unavailableSeparator"));

  const previewWidth = Math.min(200, Math.floor((windowWidth - 2 * HB_SPACING.md - 2 * HB_SPACING.sm - 14) * 0.58));
  const sizeMm = PROMO_POSTER_SIZE_MM[draft.size];
  const subtitle = [defaults.productName, defaults.itemNumber ? t("poster.editor.itemNumber", { value: defaults.itemNumber }) : ""]
    .filter(Boolean)
    .join(" · ");

  return (
    <SafeAreaView style={styles.screen} edges={["top", "left", "right"]}>
      <PosterScreenHeader
        title={t("poster.editor.title")}
        subtitle={subtitle}
        onBack={onBack}
        right={
          <PosterQueueBadgeButton
            count={queueCount}
            onPress={() => router.push(QUEUE_PATH as Parameters<typeof router.push>[0])}
          />
        }
      />
      <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === "ios" ? "padding" : undefined}>
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          <View style={[styles.card, styles.cardPadded]}>
            <PosterSegmented
              label={t("poster.editor.kindLabel")}
              value={draft.kind}
              options={PROMO_POSTER_KINDS.map((kind) => ({
                value: kind,
                label: t(`poster.kinds.${kind}`),
                disabled: !availability[kind],
              }))}
              onChange={handleKind}
            />
            {unavailableText ? <Text style={styles.unavailable}>{unavailableText}</Text> : null}
            <PosterSegmented
              label={t("poster.editor.styleLabel")}
              value={draft.style}
              options={PROMO_POSTER_STYLES.map((style) => ({ value: style, label: t(`poster.styles.${style}`) }))}
              onChange={handleStyle}
            />
            {draft.style === "low-ink" ? <Text style={styles.fieldLabel}>{t("poster.lowInkHint")}</Text> : null}
            <PosterSegmented
              label={t("poster.editor.sizeLabel")}
              value={draft.size}
              options={PROMO_POSTER_SIZES.map((size) => ({ value: size, label: size }))}
              onChange={handleSize}
            />
            <View style={styles.logoRow}>
              <View style={styles.flex}>
                <Text style={styles.validityTitle}>{t("poster.showLogo")}</Text>
                <Text style={styles.fieldLabel}>{t("poster.showLogoHint")}</Text>
              </View>
              <Switch value={showLogo} onValueChange={setShowLogo} disabled={printing} accessibilityLabel={t("poster.showLogo")} />
            </View>
          </View>

          <View style={styles.previewPanel}>
            <View style={styles.previewShadow}>
              <PromoPosterPreview data={previewData} width={previewWidth} />
            </View>
            <View style={styles.previewInfo}>
              <Text style={styles.previewTitle}>{t("poster.editor.previewSize", { size: draft.size })}</Text>
              <Text style={styles.previewText}>{t("poster.editor.previewMm", sizeMm)}</Text>
              <Text style={styles.previewText}>
                {draft.size === "A4"
                  ? t("poster.editor.previewImposeA4")
                  : t("poster.editor.previewImpose", { count: PROMO_POSTER_PER_A4[draft.size] })}
              </Text>
              <Text style={styles.previewHint}>{t("poster.editor.previewHint")}</Text>
            </View>
          </View>

          <View style={[styles.card, styles.cardPadded]}>
            <View>
              <TextInput
                mode="outlined"
                dense
                label={t("poster.editor.titleLabel")}
                placeholder={t("poster.editor.titlePlaceholder")}
                value={draft.title}
                onChangeText={(title) => setField({ title })}
                autoCapitalize="words"
                autoCorrect={false}
                maxLength={120}
                error={Boolean(titleErrorText(errors.title, showRequired, t))}
              />
              <TitleHelper error={errors.title} showRequired={showRequired} emptyDefault={!defaults.posterTitle} />
            </View>

            {draft.kind === "special" || draft.kind === "clearance" ? (
              <View style={styles.fieldRow}>
                <PriceField
                  label={t(draft.kind === "special" ? "poster.editor.priceNow" : "poster.editor.clearancePrice")}
                  value={draft.price}
                  onChange={(price) => setField({ price })}
                  hint={
                    draft.kind === "special"
                      ? defaults.discountedPrice !== null
                        ? t("poster.editor.hintDiscounted", { price: formatPosterMoney(defaults.discountedPrice) })
                        : undefined
                      : defaults.clearancePrice !== null
                        ? t("poster.editor.hintClearance", { price: formatPosterMoney(defaults.clearancePrice) })
                        : undefined
                  }
                  error={visible(errors.price)}
                />
                <PriceField
                  label={t(draft.kind === "special" ? "poster.editor.priceWasOptional" : "poster.editor.priceWas")}
                  value={draft.wasPrice}
                  onChange={(wasPrice) => setField({ wasPrice })}
                  hint={defaults.retailPrice !== null ? t("poster.editor.hintRetail", { price: formatPosterMoney(defaults.retailPrice) }) : undefined}
                  error={visible(errors.wasPrice)}
                />
              </View>
            ) : null}

            {draft.kind === "new" ? (
              <View style={styles.fieldRow}>
                <PriceField
                  label={t("poster.editor.salePrice")}
                  value={draft.price}
                  onChange={(price) => setField({ price })}
                  hint={defaults.retailPrice !== null ? t("poster.editor.hintRetail", { price: formatPosterMoney(defaults.retailPrice) }) : undefined}
                  error={visible(errors.price)}
                />
                <View style={styles.flex}>
                  <MonthDatePickerField
                    label={t("poster.editor.inStoreSince")}
                    value={draft.inStoreSince || today}
                    onChange={(inStoreSince) => setField({ inStoreSince })}
                  />
                </View>
              </View>
            ) : null}

            {draft.kind === "multibuy" ? (
              <MultiBuySection
                defaults={defaults}
                draft={draft}
                errors={{ offer: visible(errors.offer), quantity: visible(errors.quantity), unitPrice: visible(errors.unitPrice), price: visible(errors.price) }}
                onSelectOffer={(offerId) => setDraft((current) => applyMultiBuyOffer(current, defaults, offerId))}
                onChange={setField}
                validityError={visible(errors.validity)}
              />
            ) : null}

            {mismatch ? (
              <View style={styles.warning} accessibilityRole="alert">
                <MaterialCommunityIcons name="alert-outline" size={18} color={WARNING_TEXT} />
                <Text style={styles.warningText}>
                  {t(`poster.mismatch.${mismatch.kind}`, { price: formatPosterMoney(mismatch.expected) })}
                </Text>
              </View>
            ) : null}

            {draft.kind === "special" ? (
              <View style={styles.validity}>
                <View style={styles.validityHeader}>
                  <MaterialCommunityIcons name="calendar-range" size={18} color={HB_COLORS.textSecondary} />
                  <Text style={styles.validityTitle}>{t("poster.editor.validity")}</Text>
                  <Switch value={validityEnabled} onValueChange={toggleValidity} accessibilityLabel={t("poster.editor.validity")} />
                </View>
                {validityEnabled ? (
                  <View style={styles.fieldRow}>
                    <View style={styles.flex}>
                      <Text style={styles.fieldLabel}>{t("poster.editor.validFrom")}</Text>
                      <MonthDatePickerField
                        compact
                        label={t("poster.editor.validFrom")}
                        value={draft.validFrom || today}
                        onChange={(validFrom) => setField({ validFrom })}
                      />
                    </View>
                    <View style={styles.flex}>
                      <Text style={styles.fieldLabel}>{t("poster.editor.validTo")}</Text>
                      <MonthDatePickerField
                        compact
                        label={t("poster.editor.validTo")}
                        value={draft.validTo || today}
                        minDate={draft.validFrom || undefined}
                        onChange={(validTo) => setField({ validTo })}
                      />
                    </View>
                  </View>
                ) : null}
                {visible(errors.validity) ? (
                  <HelperText type="error" visible>
                    {t(`poster.errors.validity.${visible(errors.validity)}`)}
                  </HelperText>
                ) : null}
              </View>
            ) : null}
          </View>
        </ScrollView>
      </KeyboardAvoidingView>

      <View style={styles.footer}>
        <Button
          mode="outlined"
          icon="plus"
          onPress={handleAdd}
          disabled={printing}
          style={styles.footerButton}
          contentStyle={styles.footerButtonContent}
        >
          {t("poster.editor.addToQueue")}
        </Button>
        <Button
          mode="contained"
          icon="printer-outline"
          onPress={() => void handlePrintNow()}
          loading={printing}
          disabled={printing}
          style={styles.footerButton}
          contentStyle={styles.footerButtonContent}
        >
          {t("poster.editor.printNow")}
        </Button>
      </View>

      <Portal>
        <Dialog visible={conflictSpec !== null} onDismiss={() => setConflictSpec(null)}>
          <Dialog.Title>{t("poster.messages.storeConflictTitle")}</Dialog.Title>
          <Dialog.Content>
            <Text>{t("poster.messages.storeConflictBody", { count: queueCount })}</Text>
          </Dialog.Content>
          <Dialog.Actions>
            <Button onPress={() => setConflictSpec(null)}>{t("common:actions.cancel")}</Button>
            <Button onPress={confirmReplaceQueue}>{t("poster.messages.storeConflictConfirm")}</Button>
          </Dialog.Actions>
        </Dialog>
      </Portal>

      <Snackbar visible={snackbar !== null} onDismiss={() => setSnackbar(null)} duration={4000}>
        {snackbar ?? ""}
      </Snackbar>
    </SafeAreaView>
  );
}

type TranslateFn = (key: string, options?: Record<string, unknown>) => string;

function titleErrorText(error: PosterTitleError | undefined, showRequired: boolean, t: TranslateFn) {
  if (!error) return null;
  if (error.code === "unprintable") return t("poster.errors.titleUnprintable", { chars: error.chars });
  if (error.code === "tooLong") return t("poster.errors.titleTooLong", { max: error.max });
  return showRequired ? t("poster.errors.titleRequired") : null;
}

function TitleHelper({ error, showRequired, emptyDefault }: { error?: PosterTitleError; showRequired: boolean; emptyDefault: boolean }) {
  const { t } = useAppTranslation(["productQuery"]);
  const errorText = titleErrorText(error, showRequired, t);
  if (errorText) {
    return (
      <HelperText type="error" visible>
        {errorText}
      </HelperText>
    );
  }
  // 后端没给出可打印英文名时提示店员手填（中文名印不出来）。
  if (emptyDefault) {
    return (
      <HelperText type="info" visible>
        {t("poster.editor.titleEmptyHint")}
      </HelperText>
    );
  }
  return null;
}

function priceErrorKey(code: PosterFieldErrorCode) {
  if (code === "wasNotHigher") return "poster.errors.wasNotHigher";
  if (code === "required") return "poster.errors.priceRequired";
  return "poster.errors.priceInvalid";
}

function PriceField({
  label,
  value,
  onChange,
  hint,
  error,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  hint?: string;
  error?: PosterFieldErrorCode;
}) {
  const { t } = useAppTranslation(["productQuery"]);
  return (
    <View style={styles.flex}>
      <TextInput
        mode="outlined"
        dense
        label={label}
        value={value}
        onChangeText={onChange}
        keyboardType="decimal-pad"
        left={<TextInput.Affix text="$" />}
        error={Boolean(error)}
      />
      {error ? (
        <HelperText type="error" visible>
          {t(priceErrorKey(error))}
        </HelperText>
      ) : hint ? (
        <HelperText type="info" visible>
          {hint}
        </HelperText>
      ) : null}
    </View>
  );
}

function MultiBuySection({
  defaults,
  draft,
  errors,
  onSelectOffer,
  onChange,
  validityError,
}: {
  defaults: PromoPosterDefaults;
  draft: PromoPosterDraft;
  errors: { offer?: PosterFieldErrorCode; quantity?: PosterFieldErrorCode; unitPrice?: PosterFieldErrorCode; price?: PosterFieldErrorCode };
  onSelectOffer: (offerId: string) => void;
  onChange: (patch: Partial<PromoPosterDraft>) => void;
  validityError?: PosterFieldErrorCode;
}) {
  const { t } = useAppTranslation(["productQuery"]);
  const offer = defaults.multiBuyOffers.find((item) => item.promotionId === draft.offerId);
  const unitPrice = parsePosterPrice(draft.unitPrice);
  const errorKey = offer
    ? errors.offer
      ? "poster.errors.offerRequired"
      : errors.unitPrice
        ? "poster.errors.unitPriceMissing"
        : errors.quantity || errors.price
          ? "poster.errors.offerInvalid"
          : null
    : null;
  return (
    <View style={styles.multiBuy}>
      {defaults.multiBuyOffers.length > 1 ? (
        <View style={styles.offerChips}>
          {defaults.multiBuyOffers.map((item) => (
            <Chip
              key={item.promotionId}
              compact
              selected={item.promotionId === draft.offerId}
              showSelectedOverlay
              onPress={() => onSelectOffer(item.promotionId)}
            >
              {t("poster.editor.offerSummary", { quantity: item.applyQuantity, price: formatPosterMoney(item.fixedPrice, true) })}
            </Chip>
          ))}
        </View>
      ) : null}
      {offer ? (
        <View style={styles.offerBox}>
          <View style={styles.offerRow}>
            <Text style={styles.fieldLabel}>{t("poster.editor.offerLabel")}</Text>
            <Text style={styles.offerValue}>
              {t("poster.editor.offerSummary", { quantity: offer.applyQuantity, price: formatPosterMoney(offer.fixedPrice, true) })}
            </Text>
          </View>
          <View style={styles.offerRow}>
            <Text style={styles.fieldLabel}>{t("poster.editor.unitPriceLabel")}</Text>
            <Text style={styles.offerValue}>{unitPrice !== null ? formatPosterMoney(unitPrice) : "--"}</Text>
          </View>
          {draft.validFrom && draft.validTo ? (
            <View style={styles.offerRow}>
              <Text style={styles.fieldLabel}>{t("poster.editor.offerValidityLabel")}</Text>
              <Text style={styles.offerValue}>{t("poster.editor.offerValidity", { from: draft.validFrom, to: draft.validTo })}</Text>
            </View>
          ) : null}
          {offer.name ? <Text style={styles.offerNote}>{offer.name}</Text> : null}
          {draft.mixAndMatch ? (
            <Text style={styles.offerNote}>{t("poster.editor.mixAndMatch", { count: offer.productsCount, quantity: offer.applyQuantity })}</Text>
          ) : null}
          <Text style={styles.offerNote}>{t("poster.editor.offerReadonly")}</Text>
        </View>
      ) : (
        <View style={styles.offerBox}>
          <View style={styles.fieldRow}>
            <View style={styles.flex}>
              <TextInput
                mode="outlined"
                dense
                label={t("poster.editor.quantityLabel")}
                value={draft.quantity}
                onChangeText={(quantity) => onChange({ quantity })}
                keyboardType="number-pad"
                error={Boolean(errors.quantity)}
              />
              {errors.quantity ? <HelperText type="error" visible>{t("poster.errors.quantityInvalid")}</HelperText> : null}
            </View>
            <PriceField
              label={t("poster.editor.comboPriceLabel")}
              value={draft.price}
              onChange={(price) => onChange({ price })}
              error={errors.price}
            />
          </View>
          <PriceField
            label={t("poster.editor.unitPriceLabel")}
            value={draft.unitPrice}
            onChange={(unitPrice) => onChange({ unitPrice })}
            hint={defaults.retailPrice !== null ? t("poster.editor.hintRetail", { price: formatPosterMoney(defaults.retailPrice) }) : undefined}
            error={errors.unitPrice}
          />
          <View style={styles.validityHeader}>
            <MaterialCommunityIcons name="calendar-range" size={18} color={HB_COLORS.textSecondary} />
            <Text style={styles.validityTitle}>{t("poster.editor.validity")}</Text>
            <Switch
              value={Boolean(draft.validFrom || draft.validTo)}
              onValueChange={(enabled) => onChange(enabled
                ? { validFrom: formatDateOnly(new Date()), validTo: addDaysToDateOnly(formatDateOnly(new Date()), DEFAULT_VALIDITY_DAYS) }
                : { validFrom: "", validTo: "" })}
              accessibilityLabel={t("poster.editor.validity")}
            />
          </View>
          {draft.validFrom || draft.validTo ? (
            <View style={styles.fieldRow}>
              <View style={styles.flex}>
                <MonthDatePickerField compact label={t("poster.editor.validFrom")} value={draft.validFrom || formatDateOnly(new Date())} onChange={(validFrom) => onChange({ validFrom })} />
              </View>
              <View style={styles.flex}>
                <MonthDatePickerField compact label={t("poster.editor.validTo")} value={draft.validTo || formatDateOnly(new Date())} onChange={(validTo) => onChange({ validTo })} />
              </View>
            </View>
          ) : null}
          {validityError ? <HelperText type="error" visible>{t(`poster.errors.validity.${validityError}`)}</HelperText> : null}
        </View>
      )}
      {errorKey ? (
        <HelperText type="error" visible>
          {t(errorKey)}
        </HelperText>
      ) : null}
    </View>
  );
}

const WARNING_TEXT = "#7A2E0E";

const styles = StyleSheet.create({
  screen: {
    flex: 1,
    backgroundColor: HB_COLORS.background,
  },
  flex: {
    flex: 1,
    minWidth: 0,
  },
  centered: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    gap: HB_SPACING.sm,
    padding: HB_SPACING.lg,
  },
  centeredText: {
    textAlign: "center",
    color: HB_COLORS.textSecondary,
  },
  content: {
    paddingHorizontal: HB_SPACING.md,
    paddingVertical: 10,
    gap: 10,
  },
  card: {
    backgroundColor: HB_COLORS.white,
    borderWidth: 1,
    borderColor: HB_COLORS.outlineMuted,
    borderRadius: HB_RADIUS.surface,
  },
  cardPadded: {
    padding: HB_SPACING.sm,
    gap: 10,
  },
  unavailable: {
    marginTop: -2,
    marginLeft: 42,
    fontSize: 12,
    lineHeight: 17,
    color: HB_COLORS.textSecondary,
  },
  logoRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: HB_SPACING.sm,
    minHeight: 44,
  },
  previewPanel: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: 14,
    padding: HB_SPACING.sm,
    borderRadius: HB_RADIUS.surface,
    backgroundColor: "#E4E7EC",
  },
  previewShadow: {
    shadowColor: "#101828",
    shadowOpacity: 0.18,
    shadowRadius: 3,
    shadowOffset: { width: 0, height: 1 },
    elevation: 2,
    backgroundColor: HB_COLORS.white,
  },
  previewInfo: {
    flex: 1,
    minWidth: 0,
    gap: 6,
  },
  previewTitle: {
    fontSize: 14,
    lineHeight: 20,
    fontWeight: "700",
    color: HB_COLORS.textPrimary,
  },
  previewText: {
    fontSize: 12,
    lineHeight: 17,
    color: HB_COLORS.textSecondary,
  },
  previewHint: {
    marginTop: 6,
    fontSize: 12,
    lineHeight: 17,
    fontWeight: "600",
    color: HB_COLORS.action,
  },
  fieldRow: {
    flexDirection: "row",
    alignItems: "flex-start",
    gap: 10,
  },
  fieldLabel: {
    fontSize: 12,
    lineHeight: 16,
    color: HB_COLORS.textSecondary,
  },
  warning: {
    flexDirection: "row",
    alignItems: "flex-start",
    gap: HB_SPACING.xs,
    paddingHorizontal: HB_SPACING.sm,
    paddingVertical: 10,
    borderRadius: HB_RADIUS.control,
    backgroundColor: "#FFFAEB",
  },
  warningText: {
    flex: 1,
    fontSize: 13,
    lineHeight: 19,
    color: WARNING_TEXT,
  },
  validity: {
    gap: 6,
  },
  validityHeader: {
    flexDirection: "row",
    alignItems: "center",
    gap: HB_SPACING.xs,
    minHeight: 40,
  },
  validityTitle: {
    flex: 1,
    fontSize: 14,
    color: HB_COLORS.textPrimary,
  },
  multiBuy: {
    gap: HB_SPACING.xs,
  },
  offerChips: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 6,
  },
  offerBox: {
    gap: 6,
    padding: HB_SPACING.sm,
    borderRadius: HB_RADIUS.control,
    backgroundColor: HB_COLORS.surfaceMuted,
  },
  offerRow: {
    flexDirection: "row",
    alignItems: "baseline",
    justifyContent: "space-between",
    gap: HB_SPACING.xs,
  },
  offerValue: {
    fontSize: 15,
    fontWeight: "700",
    color: HB_COLORS.textPrimary,
    fontVariant: ["tabular-nums"],
  },
  offerNote: {
    fontSize: 12,
    lineHeight: 17,
    color: HB_COLORS.textSecondary,
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
});
