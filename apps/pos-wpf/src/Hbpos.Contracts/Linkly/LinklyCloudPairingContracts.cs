namespace Hbpos.Contracts.Linkly;

public sealed record LinklyCloudBackendPairRequest(
    string Environment,
    string PairCode);
