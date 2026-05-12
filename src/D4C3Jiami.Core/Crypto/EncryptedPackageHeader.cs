using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace D4C3Jiami.Core.Crypto;

public sealed record EncryptedPackageHeader
{
    public const ushort CurrentVersion = 1;
    public const string MagicText = "D4C3JIAM";
    public static readonly byte[] MagicBytes = Encoding.ASCII.GetBytes(MagicText);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public required ushort Version { get; init; }

    public required string OriginalFileName { get; init; }

    public required string OriginalExtension { get; init; }

    public required long OriginalLength { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public required IReadOnlyList<LayerDescriptor> Layers { get; init; }

    public static async Task WriteAsync(
        Stream destination,
        EncryptedPackageHeader header,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(header, JsonOptions);
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        if (jsonBytes.Length > 16 * 1024 * 1024)
        {
            throw new InvalidPackageException("加密文件头过大，无法写入。");
        }

        await destination.WriteAsync(MagicBytes, cancellationToken);

        var fixedHeader = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(fixedHeader.AsSpan(0, 2), header.Version);
        BinaryPrimitives.WriteInt32LittleEndian(fixedHeader.AsSpan(2), jsonBytes.Length);
        await destination.WriteAsync(fixedHeader, cancellationToken);
        await destination.WriteAsync(jsonBytes, cancellationToken);
    }

    public static async Task<EncryptedPackageHeader> ReadAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        var magic = new byte[MagicBytes.Length];
        await ReadExactlyAsync(source, magic, cancellationToken);
        if (!magic.AsSpan().SequenceEqual(MagicBytes))
        {
            throw new InvalidPackageException("这不是本软件生成的加密文件。");
        }

        var fixedHeader = new byte[6];
        await ReadExactlyAsync(source, fixedHeader, cancellationToken);

        var version = BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.AsSpan(..2));
        if (version != CurrentVersion)
        {
            throw new InvalidPackageException($"不支持的加密文件版本：{version}。");
        }

        var jsonLength = BinaryPrimitives.ReadInt32LittleEndian(fixedHeader.AsSpan(2));
        if (jsonLength <= 0 || jsonLength > 16 * 1024 * 1024)
        {
            throw new InvalidPackageException("加密文件头长度无效，文件可能已损坏。");
        }

        var jsonBytes = new byte[jsonLength];
        await ReadExactlyAsync(source, jsonBytes, cancellationToken);

        try
        {
            var header = JsonSerializer.Deserialize<EncryptedPackageHeader>(jsonBytes, JsonOptions);
            if (header is null)
            {
                throw new InvalidPackageException("加密文件头为空，文件可能已损坏。");
            }

            Validate(header);
            return header;
        }
        catch (JsonException ex)
        {
            throw new InvalidPackageException("加密文件头无法解析，文件可能已损坏。", ex);
        }
    }

    public static async Task<EncryptedPackageHeader> ReadFromFileAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(packagePath);
        return await ReadAsync(stream, cancellationToken);
    }

    private static void Validate(EncryptedPackageHeader header)
    {
        if (header.Version != CurrentVersion)
        {
            throw new InvalidPackageException($"不支持的加密文件版本：{header.Version}。");
        }

        if (string.IsNullOrWhiteSpace(header.OriginalFileName))
        {
            throw new InvalidPackageException("加密文件头缺少原文件名。");
        }

        if (header.OriginalLength < 0)
        {
            throw new InvalidPackageException("加密文件头中的原文件大小无效。");
        }

        if (header.Layers.Count is < 1 or > CryptoPipeline.MaxLayerCount)
        {
            throw new InvalidPackageException("加密层数无效。");
        }

        for (var i = 0; i < header.Layers.Count; i++)
        {
            var layer = header.Layers[i];
            if (layer.Index != i + 1)
            {
                throw new InvalidPackageException("加密层顺序无效。");
            }

            if (layer.Salt.Length != CryptoPipeline.SaltSize)
            {
                throw new InvalidPackageException("加密层盐值长度无效。");
            }

            if (layer.NonceBase.Length != CryptoPipeline.NonceBaseSize)
            {
                throw new InvalidPackageException("加密层随机数长度无效。");
            }
        }
    }

    internal static async Task ReadExactlyAsync(
        Stream source,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await source.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                throw new InvalidPackageException("加密文件不完整，文件可能已损坏。");
            }

            offset += read;
        }
    }
}
