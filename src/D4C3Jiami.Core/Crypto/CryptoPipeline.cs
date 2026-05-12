using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace D4C3Jiami.Core.Crypto;

public sealed class CryptoPipeline
{
    public const int MaxLayerCount = 3;
    public const int SaltSize = 16;
    public const int NonceBaseSize = 8;
    public const int DefaultChunkSize = 1024 * 1024;
    public const int Pbkdf2Iterations = 250_000;

    private const int AeadNonceSize = 12;
    private const int AeadTagSize = 16;
    private const int CbcIvSize = 16;
    private const int HmacSize = 32;
    private const string KdfName = "PBKDF2-HMAC-SHA256";

    public async Task EncryptFileAsync(
        string inputFilePath,
        string outputFilePath,
        IReadOnlyList<EncryptionLayerConfig> layers,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputFilePath))
        {
            throw new FileNotFoundException("找不到要加密的文件。", inputFilePath);
        }

        ValidateLayerConfigs(layers);

        var sourceInfo = new FileInfo(inputFilePath);
        var tempFiles = new List<string>();
        var descriptors = new List<LayerDescriptor>();

        try
        {
            var currentInput = inputFilePath;
            for (var i = 0; i < layers.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var tempOutput = Path.Combine(
                    Path.GetTempPath(),
                    $"d4c3-jiami-{Guid.NewGuid():N}.layer");
                tempFiles.Add(tempOutput);

                var descriptor = CreateLayerDescriptor(i + 1, layers[i].Algorithm);
                descriptors.Add(descriptor);

                var keyMaterial = DeriveKeyMaterial(layers[i].Password, descriptor);
                await TransformAsync(
                    currentInput,
                    tempOutput,
                    descriptor,
                    keyMaterial,
                    encrypt: true,
                    cancellationToken);

                if (currentInput != inputFilePath)
                {
                    TryDelete(currentInput);
                }

                currentInput = tempOutput;
            }

            var header = new EncryptedPackageHeader
            {
                Version = EncryptedPackageHeader.CurrentVersion,
                OriginalFileName = sourceInfo.Name,
                OriginalExtension = sourceInfo.Extension,
                OriginalLength = sourceInfo.Length,
                CreatedUtc = DateTimeOffset.UtcNow,
                Layers = descriptors
            };

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFilePath))!);
            await using var package = new FileStream(
                outputFilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                useAsync: true);

            await EncryptedPackageHeader.WriteAsync(package, header, cancellationToken);
            await using var encryptedPayload = new FileStream(
                currentInput,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                useAsync: true);
            await encryptedPayload.CopyToAsync(package, cancellationToken);
        }
        catch (CryptographicException ex)
        {
            TryDelete(outputFilePath);
            throw new PasswordVerificationException("加密失败，请检查文件是否可读取并重试。", ex);
        }
        catch
        {
            TryDelete(outputFilePath);
            throw;
        }
        finally
        {
            foreach (var file in tempFiles)
            {
                TryDelete(file);
            }
        }
    }

    public async Task<DecryptionResult> DecryptFileAsync(
        string packagePath,
        string outputDirectory,
        IReadOnlyList<string> passwordsInLayerOrder,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException("找不到要解密的文件。", packagePath);
        }

        Directory.CreateDirectory(outputDirectory);

        var tempFiles = new List<string>();
        string? payloadTemp = null;
        string? finalOutput = null;

        try
        {
            EncryptedPackageHeader header;
            await using (var package = new FileStream(
                             packagePath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             bufferSize: 128 * 1024,
                             useAsync: true))
            {
                header = await EncryptedPackageHeader.ReadAsync(package, cancellationToken);
                if (passwordsInLayerOrder.Count != header.Layers.Count)
                {
                    throw new PasswordVerificationException("输入的密码数量与加密层数不一致。");
                }

                payloadTemp = Path.Combine(
                    Path.GetTempPath(),
                    $"d4c3-jiami-{Guid.NewGuid():N}.payload");
                tempFiles.Add(payloadTemp);

                await using var payload = new FileStream(
                    payloadTemp,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 128 * 1024,
                    useAsync: true);
                await package.CopyToAsync(payload, cancellationToken);
            }

            var currentInput = payloadTemp;
            for (var i = header.Layers.Count - 1; i >= 0; i--)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var layer = header.Layers[i];
                var outputPath = Path.Combine(
                    Path.GetTempPath(),
                    $"d4c3-jiami-{Guid.NewGuid():N}.plain");
                tempFiles.Add(outputPath);

                var keyMaterial = DeriveKeyMaterial(passwordsInLayerOrder[i], layer);
                await TransformAsync(
                    currentInput,
                    outputPath,
                    layer,
                    keyMaterial,
                    encrypt: false,
                    cancellationToken);

                if (currentInput != payloadTemp)
                {
                    TryDelete(currentInput);
                }

                currentInput = outputPath;
            }

            finalOutput = GetAvailableOutputPath(outputDirectory, header.OriginalFileName);
            File.Copy(currentInput, finalOutput, overwrite: false);

            if (new FileInfo(finalOutput).Length != header.OriginalLength)
            {
                TryDelete(finalOutput);
                throw new InvalidPackageException("解密后的文件大小与原文件不一致，文件可能已损坏。");
            }

            return new DecryptionResult(finalOutput, header);
        }
        catch (CryptographicException ex)
        {
            if (finalOutput is not null)
            {
                TryDelete(finalOutput);
            }

            throw new PasswordVerificationException("密码错误，或加密文件已经损坏。", ex);
        }
        catch (InvalidDataException ex)
        {
            if (finalOutput is not null)
            {
                TryDelete(finalOutput);
            }

            throw new InvalidPackageException("加密文件数据无效，文件可能已损坏。", ex);
        }
        finally
        {
            foreach (var file in tempFiles)
            {
                TryDelete(file);
            }
        }
    }

    public async Task<EncryptedPackageHeader> InspectHeaderAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        return await EncryptedPackageHeader.ReadFromFileAsync(packagePath, cancellationToken);
    }

    private static void ValidateLayerConfigs(IReadOnlyList<EncryptionLayerConfig> layers)
    {
        if (layers.Count is < 1 or > MaxLayerCount)
        {
            throw new ArgumentException("加密层数必须是 1 到 3 层。", nameof(layers));
        }

        for (var i = 0; i < layers.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(layers[i].Password))
            {
                throw new ArgumentException($"第 {i + 1} 层密码不能为空。", nameof(layers));
            }
        }
    }

    private static LayerDescriptor CreateLayerDescriptor(int index, JiamiAlgorithm algorithm)
    {
        return new LayerDescriptor
        {
            Index = index,
            Algorithm = algorithm,
            Kdf = KdfName,
            KdfIterations = Pbkdf2Iterations,
            ChunkSize = DefaultChunkSize,
            Salt = RandomNumberGenerator.GetBytes(SaltSize),
            NonceBase = RandomNumberGenerator.GetBytes(NonceBaseSize)
        };
    }

    private static byte[] DeriveKeyMaterial(string password, LayerDescriptor descriptor)
    {
        var bytesNeeded = descriptor.Algorithm == JiamiAlgorithm.Aes256CbcHmacSha256 ? 64 : 32;
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            descriptor.Salt,
            descriptor.KdfIterations,
            HashAlgorithmName.SHA256,
            bytesNeeded);
    }

    private static async Task TransformAsync(
        string inputPath,
        string outputPath,
        LayerDescriptor descriptor,
        byte[] keyMaterial,
        bool encrypt,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            inputPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            useAsync: true);
        await using var output = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 128 * 1024,
            useAsync: true);

        switch (descriptor.Algorithm)
        {
            case JiamiAlgorithm.Aes256Gcm:
                await TransformAeadAsync(input, output, descriptor, keyMaterial, useChaCha: false, encrypt, cancellationToken);
                break;
            case JiamiAlgorithm.ChaCha20Poly1305:
                await TransformAeadAsync(input, output, descriptor, keyMaterial, useChaCha: true, encrypt, cancellationToken);
                break;
            case JiamiAlgorithm.Aes256CbcHmacSha256:
                await TransformCbcHmacAsync(input, output, descriptor, keyMaterial, encrypt, cancellationToken);
                break;
            default:
                throw new InvalidPackageException($"不支持的加密算法：{descriptor.Algorithm}。");
        }
    }

    private static async Task TransformAeadAsync(
        Stream input,
        Stream output,
        LayerDescriptor descriptor,
        byte[] key,
        bool useChaCha,
        bool encrypt,
        CancellationToken cancellationToken)
    {
        using var aesGcm = useChaCha ? null : new AesGcm(key, AeadTagSize);
        using var chacha = useChaCha ? new ChaCha20Poly1305(key) : null;

        var inputBuffer = new byte[descriptor.ChunkSize];
        var chunkIndex = 0;

        if (encrypt)
        {
            var wroteAnyChunk = false;
            while (true)
            {
                var read = await input.ReadAsync(inputBuffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                wroteAnyChunk = true;
                var nonce = BuildNonce(descriptor.NonceBase, chunkIndex);
                var cipher = new byte[read];
                var tag = new byte[AeadTagSize];
                var plaintext = inputBuffer.AsMemory(0, read);
                var additionalData = BuildAdditionalData(descriptor.Index, chunkIndex, read);

                if (useChaCha)
                {
                    chacha!.Encrypt(nonce, plaintext.Span, cipher, tag, additionalData);
                }
                else
                {
                    aesGcm!.Encrypt(nonce, plaintext.Span, cipher, tag, additionalData);
                }

                await WriteChunkAsync(output, cipher, tag, cancellationToken);
                chunkIndex++;
            }

            if (!wroteAnyChunk)
            {
                var nonce = BuildNonce(descriptor.NonceBase, chunkIndex);
                var cipher = Array.Empty<byte>();
                var tag = new byte[AeadTagSize];
                var additionalData = BuildAdditionalData(descriptor.Index, chunkIndex, 0);

                if (useChaCha)
                {
                    chacha!.Encrypt(nonce, ReadOnlySpan<byte>.Empty, cipher, tag, additionalData);
                }
                else
                {
                    aesGcm!.Encrypt(nonce, ReadOnlySpan<byte>.Empty, cipher, tag, additionalData);
                }

                await WriteChunkAsync(output, cipher, tag, cancellationToken);
            }
        }
        else
        {
            while (await TryReadChunkAsync(input, AeadTagSize, cancellationToken) is { } chunk)
            {
                var nonce = BuildNonce(descriptor.NonceBase, chunkIndex);
                var plaintext = new byte[chunk.Data.Length];
                var tag = chunk.Tag;
                var additionalData = BuildAdditionalData(descriptor.Index, chunkIndex, chunk.Data.Length);

                if (useChaCha)
                {
                    chacha!.Decrypt(nonce, chunk.Data, tag, plaintext, additionalData);
                }
                else
                {
                    aesGcm!.Decrypt(nonce, chunk.Data, tag, plaintext, additionalData);
                }

                await output.WriteAsync(plaintext, cancellationToken);
                chunkIndex++;
            }
        }
    }

    private static async Task TransformCbcHmacAsync(
        Stream input,
        Stream output,
        LayerDescriptor descriptor,
        byte[] keyMaterial,
        bool encrypt,
        CancellationToken cancellationToken)
    {
        var encryptionKey = keyMaterial.AsSpan(0, 32).ToArray();
        var macKey = keyMaterial.AsSpan(32, 32).ToArray();
        var inputBuffer = new byte[descriptor.ChunkSize];
        var chunkIndex = 0;

        if (encrypt)
        {
            var wroteAnyChunk = false;
            while (true)
            {
                var read = await input.ReadAsync(inputBuffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                wroteAnyChunk = true;
                var iv = RandomNumberGenerator.GetBytes(CbcIvSize);
                var plainChunk = inputBuffer.AsSpan(0, read).ToArray();
                var cipher = EncryptCbc(plainChunk, encryptionKey, iv);
                var tag = ComputeCbcMac(macKey, descriptor.Index, chunkIndex, cipher.Length, iv, cipher);

                await WriteChunkAsync(output, Combine(iv, cipher), tag, cancellationToken);
                chunkIndex++;
            }

            if (!wroteAnyChunk)
            {
                var iv = RandomNumberGenerator.GetBytes(CbcIvSize);
                var cipher = EncryptCbc(Array.Empty<byte>(), encryptionKey, iv);
                var tag = ComputeCbcMac(macKey, descriptor.Index, chunkIndex, cipher.Length, iv, cipher);

                await WriteChunkAsync(output, Combine(iv, cipher), tag, cancellationToken);
            }
        }
        else
        {
            while (await TryReadChunkAsync(input, HmacSize, cancellationToken) is { } chunk)
            {
                if (chunk.Data.Length < CbcIvSize)
                {
                    throw new InvalidDataException("CBC 加密块过短。");
                }

                var iv = chunk.Data[..CbcIvSize];
                var cipher = chunk.Data[CbcIvSize..];
                var expectedTag = ComputeCbcMac(macKey, descriptor.Index, chunkIndex, cipher.Length, iv, cipher);
                if (!CryptographicOperations.FixedTimeEquals(expectedTag, chunk.Tag))
                {
                    throw new CryptographicException("CBC-HMAC 校验失败。");
                }

                var plainChunk = DecryptCbc(cipher, encryptionKey, iv);
                await output.WriteAsync(plainChunk, cancellationToken);
                chunkIndex++;
            }
        }
    }

    private static byte[] EncryptCbc(byte[] plainChunk, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;

        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plainChunk, 0, plainChunk.Length);
    }

    private static byte[] DecryptCbc(byte[] cipherChunk, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(cipherChunk, 0, cipherChunk.Length);
    }

    private static byte[] ComputeCbcMac(
        byte[] macKey,
        int layerIndex,
        int chunkIndex,
        int plainLength,
        byte[] iv,
        byte[] cipher)
    {
        using var hmac = new HMACSHA256(macKey);
        var additionalData = BuildAdditionalData(layerIndex, chunkIndex, plainLength);
        hmac.TransformBlock(additionalData, 0, additionalData.Length, null, 0);
        hmac.TransformBlock(iv, 0, iv.Length, null, 0);
        hmac.TransformFinalBlock(cipher, 0, cipher.Length);
        return hmac.Hash!;
    }

    private static async Task WriteChunkAsync(
        Stream output,
        byte[] data,
        byte[] tag,
        CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, data.Length);
        await output.WriteAsync(lengthBytes, cancellationToken);
        await output.WriteAsync(data, cancellationToken);
        await output.WriteAsync(tag, cancellationToken);
    }

    private static async Task<EncryptedChunk?> TryReadChunkAsync(
        Stream input,
        int tagSize,
        CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[4];
        var firstRead = await input.ReadAsync(lengthBytes, cancellationToken);
        if (firstRead == 0)
        {
            return null;
        }

        if (firstRead < lengthBytes.Length)
        {
            await ReadExactlyAsync(input, lengthBytes.AsMemory(firstRead), cancellationToken);
        }

        var dataLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (dataLength < 0 || dataLength > DefaultChunkSize + CbcIvSize + 64)
        {
            throw new InvalidDataException("加密块长度无效。");
        }

        var data = new byte[dataLength];
        await ReadExactlyAsync(input, data, cancellationToken);
        var tag = new byte[tagSize];
        await ReadExactlyAsync(input, tag, cancellationToken);
        return new EncryptedChunk(data, tag);
    }

    private static async Task ReadExactlyAsync(
        Stream input,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await input.ReadAsync(buffer[offset..], cancellationToken);
            if (read == 0)
            {
                throw new InvalidDataException("加密块数据不完整。");
            }

            offset += read;
        }
    }

    private static byte[] BuildNonce(byte[] nonceBase, int chunkIndex)
    {
        var nonce = new byte[AeadNonceSize];
        nonceBase.CopyTo(nonce, 0);
        BinaryPrimitives.WriteInt32LittleEndian(nonce.AsSpan(NonceBaseSize), chunkIndex);
        return nonce;
    }

    private static byte[] BuildAdditionalData(int layerIndex, int chunkIndex, int plainLength)
    {
        var data = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0, 4), layerIndex);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4, 4), chunkIndex);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8, 4), plainLength);
        return data;
    }

    private static byte[] Combine(byte[] first, byte[] second)
    {
        var combined = new byte[first.Length + second.Length];
        first.CopyTo(combined, 0);
        second.CopyTo(combined, first.Length);
        return combined;
    }

    private static string GetAvailableOutputPath(string outputDirectory, string fileName)
    {
        var cleanName = Path.GetFileName(fileName);
        var candidate = Path.Combine(outputDirectory, cleanName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var stem = Path.GetFileNameWithoutExtension(cleanName);
        var extension = Path.GetExtension(cleanName);
        for (var i = 1; i < 10_000; i++)
        {
            candidate = Path.Combine(outputDirectory, $"{stem} ({i}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("无法生成不重复的输出文件名。");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup of temporary or partial files.
        }
    }

    private sealed record EncryptedChunk(byte[] Data, byte[] Tag);
}
