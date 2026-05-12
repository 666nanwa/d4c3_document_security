namespace D4C3Jiami.Core.Crypto;

public sealed record LayerDescriptor
{
    public required int Index { get; init; }

    public required JiamiAlgorithm Algorithm { get; init; }

    public required string Kdf { get; init; }

    public required int KdfIterations { get; init; }

    public required int ChunkSize { get; init; }

    public required byte[] Salt { get; init; }

    public required byte[] NonceBase { get; init; }
}
