namespace Hbpos.Client.Wpf.Services;

internal static class LinklyLocalTransactionIdentity
{
    internal static bool Matches(
        string? expectedTxnRef,
        string? expectedTxnType,
        decimal expectedAmount,
        PaymentAuthorizationResult result)
    {
        if ((!string.Equals(expectedTxnType, "P", StringComparison.Ordinal) &&
             !string.Equals(expectedTxnType, "R", StringComparison.Ordinal)) ||
            !LinklyLocalTxnRef.TryNormalizeHistoricalReference(expectedTxnRef, out var normalizedExpectedTxnRef) ||
            !string.Equals(expectedTxnType, result.TxnType, StringComparison.Ordinal))
        {
            return false;
        }

        var hasReturnedTxnRef = false;
        if (!MatchesReturnedTxnRef(result.TxnRef, normalizedExpectedTxnRef, ref hasReturnedTxnRef) ||
            !MatchesReturnedTxnRef(result.Reference, normalizedExpectedTxnRef, ref hasReturnedTxnRef))
        {
            return false;
        }

        if (result.CardTransactions is not null)
        {
            foreach (var transaction in result.CardTransactions)
            {
                if (!MatchesReturnedTxnRef(transaction.TxnRef, normalizedExpectedTxnRef, ref hasReturnedTxnRef) ||
                    transaction.Amount != 0m && transaction.Amount != expectedAmount)
                {
                    return false;
                }
            }
        }

        if (!hasReturnedTxnRef)
        {
            return false;
        }

        if (result.Approved)
        {
            return result.AuthorizedAmount is decimal approvedAmount && approvedAmount == expectedAmount;
        }

        return result.AuthorizedAmount is not decimal finalAmount ||
            finalAmount == 0m ||
            finalAmount == expectedAmount;
    }

    private static bool MatchesReturnedTxnRef(
        string? candidate,
        string expectedTxnRef,
        ref bool hasReturnedTxnRef)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return true;
        }

        hasReturnedTxnRef = true;
        return LinklyLocalTxnRef.TryNormalizeHistoricalReference(candidate, out var normalizedCandidate) &&
            string.Equals(expectedTxnRef, normalizedCandidate, StringComparison.Ordinal);
    }
}
