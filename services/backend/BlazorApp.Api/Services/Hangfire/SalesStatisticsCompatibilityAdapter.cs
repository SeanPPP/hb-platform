using System.Collections;
using System.Collections.ObjectModel;

namespace BlazorApp.Api.Services;

/// <summary>
/// canonical 快照与任意外层兼容 DTO 之间的无 owner 适配基元。具体 legacy 类型和字段映射由 façade 提供。
/// </summary>
internal static class SalesStatisticsCompatibilityAdapter
{
    internal sealed class HBSalesSnapshotView<TLegacyRow, TLegacySignature>
    {
        private readonly IReadOnlyDictionary<DateTime, IReadOnlyList<TLegacyRow>> _rowsByDate;

        internal HBSalesSnapshotView(
            HBSales2025BatchSnapshot canonical,
            Func<ProductStoreDailySourceRow, TLegacyRow> mapRow,
            Func<HBSales2025DailySnapshotSignature, TLegacySignature> mapSignature)
        {
            Canonical = canonical;
            _rowsByDate = new ReadOnlyDictionary<DateTime, IReadOnlyList<TLegacyRow>>(
                canonical.Signatures.Keys.ToDictionary(
                    date => date,
                    date => (IReadOnlyList<TLegacyRow>)new MappedReadOnlyList<
                        ProductStoreDailySourceRow,
                        TLegacyRow
                    >(canonical.GetRows(date), mapRow)
                )
            );
            Signatures = new ReadOnlyDictionary<DateTime, TLegacySignature>(
                canonical.Signatures.ToDictionary(
                    entry => entry.Key,
                    entry => mapSignature(entry.Value)
                )
            );
        }

        internal HBSales2025BatchSnapshot Canonical { get; }

        internal IReadOnlyDictionary<DateTime, TLegacySignature> Signatures { get; }

        internal IReadOnlyList<TLegacyRow> GetRows(DateTime date) =>
            _rowsByDate.TryGetValue(date.Date, out var rows)
                ? rows
                : throw new InvalidOperationException($"批量快照不包含日期: {date:yyyy-MM-dd}");
    }

    internal sealed class PosmSnapshotView<
        TLegacyRow,
        TLegacyPayment,
        TLegacyOrder,
        TLegacySignature>
    {
        internal PosmSnapshotView(
            Posm2025DailySnapshot canonical,
            Func<ProductStoreDailySourceRow, TLegacyRow> mapRow,
            Func<StoreStatisticPaymentRow, TLegacyPayment> mapPayment,
            Func<StoreStatisticOrderRow, TLegacyOrder> mapOrder,
            Func<Posm2025DailySnapshotSignature, TLegacySignature> mapSignature)
        {
            Canonical = canonical;
            DetailRows = new MappedReadOnlyList<ProductStoreDailySourceRow, TLegacyRow>(
                canonical.DetailRows,
                mapRow
            );
            SupplementalReturnRows = new MappedReadOnlyList<
                ProductStoreDailySourceRow,
                TLegacyRow
            >(canonical.SupplementalReturnRows, mapRow);
            PaymentRows = new MappedReadOnlyList<StoreStatisticPaymentRow, TLegacyPayment>(
                canonical.PaymentRows,
                mapPayment
            );
            OrderRows = new MappedReadOnlyList<StoreStatisticOrderRow, TLegacyOrder>(
                canonical.OrderRows,
                mapOrder
            );
            DeviceBranchMap = new Dictionary<string, string>(
                canonical.DeviceBranchMap,
                StringComparer.Ordinal
            );
            Signature = mapSignature(canonical.Signature);
        }

        internal Posm2025DailySnapshot Canonical { get; }
        internal IReadOnlyList<TLegacyRow> DetailRows { get; }
        internal IReadOnlyList<TLegacyRow> SupplementalReturnRows { get; }
        internal IReadOnlyList<TLegacyPayment> PaymentRows { get; }
        internal IReadOnlyList<TLegacyOrder> OrderRows { get; }
        internal Dictionary<string, string> DeviceBranchMap { get; }
        internal TLegacySignature Signature { get; }
    }

    internal sealed class MappedReadOnlyList<TSource, TTarget> : IReadOnlyList<TTarget>
    {
        private readonly IReadOnlyList<TSource> _source;
        private readonly Func<TSource, TTarget> _map;

        internal MappedReadOnlyList(IReadOnlyList<TSource> source, Func<TSource, TTarget> map)
        {
            _source = source;
            _map = map;
        }

        public int Count => _source.Count;

        public TTarget this[int index] => _map(_source[index]);

        public IEnumerator<TTarget> GetEnumerator()
        {
            for (var index = 0; index < _source.Count; index++)
                yield return _map(_source[index]);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal static HBSalesSnapshotView<TLegacyRow, TLegacySignature> CreateHBSalesView<
        TLegacyRow,
        TLegacySignature>(
        HBSales2025BatchSnapshot canonical,
        Func<ProductStoreDailySourceRow, TLegacyRow> mapRow,
        Func<HBSales2025DailySnapshotSignature, TLegacySignature> mapSignature) =>
        new(canonical, mapRow, mapSignature);

    internal static HBSales2025BatchSnapshot ToCanonicalHBSalesSnapshot<
        TLegacyRow,
        TLegacySignature>(
        IReadOnlyDictionary<DateTime, List<TLegacyRow>> rowsByDate,
        IReadOnlyDictionary<DateTime, TLegacySignature> signatures,
        Func<TLegacyRow, ProductStoreDailySourceRow> mapRow,
        Func<TLegacySignature, HBSales2025DailySnapshotSignature> mapSignature)
    {
        var canonicalRows = new Dictionary<DateTime, List<ProductStoreDailySourceRow>>(
            rowsByDate.Count
        );
        foreach (var entry in rowsByDate)
            canonicalRows[entry.Key] = MapToList(entry.Value, mapRow);

        var canonicalSignatures = new Dictionary<DateTime, HBSales2025DailySnapshotSignature>(
            signatures.Count
        );
        foreach (var entry in signatures)
            canonicalSignatures[entry.Key] = mapSignature(entry.Value);

        return new HBSales2025BatchSnapshot(canonicalRows, canonicalSignatures);
    }

    internal static PosmSnapshotView<
        TLegacyRow,
        TLegacyPayment,
        TLegacyOrder,
        TLegacySignature> CreatePosmView<
        TLegacyRow,
        TLegacyPayment,
        TLegacyOrder,
        TLegacySignature>(
        Posm2025DailySnapshot canonical,
        Func<ProductStoreDailySourceRow, TLegacyRow> mapRow,
        Func<StoreStatisticPaymentRow, TLegacyPayment> mapPayment,
        Func<StoreStatisticOrderRow, TLegacyOrder> mapOrder,
        Func<Posm2025DailySnapshotSignature, TLegacySignature> mapSignature) =>
        new(canonical, mapRow, mapPayment, mapOrder, mapSignature);

    internal static Posm2025DailySnapshot ToCanonicalPosmSnapshot<
        TLegacyRow,
        TLegacyPayment,
        TLegacyOrder,
        TLegacySignature>(
        IReadOnlyList<TLegacyRow> detailRows,
        IReadOnlyList<TLegacyRow> supplementalReturnRows,
        IReadOnlyList<TLegacyPayment> paymentRows,
        IReadOnlyList<TLegacyOrder> orderRows,
        Dictionary<string, string> deviceBranchMap,
        TLegacySignature signature,
        Func<TLegacyRow, ProductStoreDailySourceRow> mapRow,
        Func<TLegacyPayment, StoreStatisticPaymentRow> mapPayment,
        Func<TLegacyOrder, StoreStatisticOrderRow> mapOrder,
        Func<TLegacySignature, Posm2025DailySnapshotSignature> mapSignature) =>
        new(
            MapToArray(detailRows, mapRow),
            MapToArray(supplementalReturnRows, mapRow),
            MapToArray(paymentRows, mapPayment),
            MapToArray(orderRows, mapOrder),
            new Dictionary<string, string>(deviceBranchMap, StringComparer.Ordinal),
            mapSignature(signature)
        );

    internal static IReadOnlyList<ProductStoreDailySourceRow>? ToCanonicalRows<TLegacyRow>(
        IReadOnlyCollection<TLegacyRow>? rows,
        Func<TLegacyRow, ProductStoreDailySourceRow> mapRow)
    {
        if (rows == null)
            return null;

        var mapped = new ProductStoreDailySourceRow[rows.Count];
        var index = 0;
        foreach (var row in rows)
            mapped[index++] = mapRow(row);
        return Array.AsReadOnly(mapped);
    }

    internal static TTarget[] MapToArray<TSource, TTarget>(
        IReadOnlyList<TSource> source,
        Func<TSource, TTarget> map)
    {
        var result = new TTarget[source.Count];
        for (var index = 0; index < source.Count; index++)
            result[index] = map(source[index]);
        return result;
    }

    internal static List<TTarget> MapToList<TSource, TTarget>(
        IReadOnlyList<TSource> source,
        Func<TSource, TTarget> map)
    {
        var result = new List<TTarget>(source.Count);
        for (var index = 0; index < source.Count; index++)
            result.Add(map(source[index]));
        return result;
    }
}
