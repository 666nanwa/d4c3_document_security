using D4C3Jiami.Core.Crypto;

var tests = new CoreCryptoTests();

await tests.AesGcmRoundTripRestoresOriginalBytesAndName();
await tests.ChaChaRoundTripRestoresOriginalBytesAndName();
await tests.CbcHmacRoundTripRestoresOriginalBytesAndName();
await tests.ThreeLayerMixedRoundTripRestoresOriginalBytesAndName();
await tests.WrongPasswordFailsAndDoesNotCreateOutput();
await tests.HeaderInspectionReportsOriginalMetadata();
await tests.EmptyFileRoundTripRestoresZeroByteFile();

Console.WriteLine("All D4C3Jiami.Core tests passed.");

internal sealed class CoreCryptoTests
{
    private readonly CryptoPipeline _pipeline = new();

    public async Task AesGcmRoundTripRestoresOriginalBytesAndName()
    {
        await RoundTripSingleLayerAsync(JiamiAlgorithm.Aes256Gcm, "aes-gcm-password");
    }

    public async Task ChaChaRoundTripRestoresOriginalBytesAndName()
    {
        await RoundTripSingleLayerAsync(JiamiAlgorithm.ChaCha20Poly1305, "chacha-password");
    }

    public async Task CbcHmacRoundTripRestoresOriginalBytesAndName()
    {
        await RoundTripSingleLayerAsync(JiamiAlgorithm.Aes256CbcHmacSha256, "cbc-hmac-password");
    }

    public async Task ThreeLayerMixedRoundTripRestoresOriginalBytesAndName()
    {
        using var workspace = new TestWorkspace();
        var input = workspace.WriteBytes("holiday-video.mp4", DeterministicBytes(2_250_123));
        var package = Path.Combine(workspace.Root, "locked.jpg");
        var output = Path.Combine(workspace.Root, "restore");

        await _pipeline.EncryptFileAsync(
            input,
            package,
            new[]
            {
                new EncryptionLayerConfig(JiamiAlgorithm.Aes256Gcm, "first password"),
                new EncryptionLayerConfig(JiamiAlgorithm.ChaCha20Poly1305, "second password"),
                new EncryptionLayerConfig(JiamiAlgorithm.Aes256CbcHmacSha256, "third password")
            });

        var result = await _pipeline.DecryptFileAsync(
            package,
            output,
            new[] { "first password", "second password", "third password" });

        AssertFileBytesEqual(input, result.OutputFilePath);
        AssertEqual("holiday-video.mp4", Path.GetFileName(result.OutputFilePath));
        AssertEqual(3, result.Header.Layers.Count);
    }

    public async Task WrongPasswordFailsAndDoesNotCreateOutput()
    {
        using var workspace = new TestWorkspace();
        var input = workspace.WriteBytes("secret.txt", "important text"u8.ToArray());
        var package = Path.Combine(workspace.Root, "secret.enc");
        var output = Path.Combine(workspace.Root, "restore");

        await _pipeline.EncryptFileAsync(
            input,
            package,
            new[] { new EncryptionLayerConfig(JiamiAlgorithm.Aes256Gcm, "correct") });

        await AssertThrowsAsync<PasswordVerificationException>(
            () => _pipeline.DecryptFileAsync(package, output, new[] { "wrong" }));

        AssertFalse(File.Exists(Path.Combine(output, "secret.txt")), "Wrong password must not create an output file.");
    }

    public async Task HeaderInspectionReportsOriginalMetadata()
    {
        using var workspace = new TestWorkspace();
        var input = workspace.WriteBytes("photo.png", DeterministicBytes(512));
        var package = Path.Combine(workspace.Root, "photo.mp4");

        await _pipeline.EncryptFileAsync(
            input,
            package,
            new[]
            {
                new EncryptionLayerConfig(JiamiAlgorithm.ChaCha20Poly1305, "layer-one"),
                new EncryptionLayerConfig(JiamiAlgorithm.Aes256Gcm, "layer-two")
            });

        var header = await _pipeline.InspectHeaderAsync(package);

        AssertEqual("photo.png", header.OriginalFileName);
        AssertEqual(".png", header.OriginalExtension);
        AssertEqual(512L, header.OriginalLength);
        AssertEqual(2, header.Layers.Count);
        AssertEqual(JiamiAlgorithm.ChaCha20Poly1305, header.Layers[0].Algorithm);
        AssertEqual(JiamiAlgorithm.Aes256Gcm, header.Layers[1].Algorithm);
    }

    public async Task EmptyFileRoundTripRestoresZeroByteFile()
    {
        using var workspace = new TestWorkspace();
        var input = workspace.WriteBytes("empty.dat", Array.Empty<byte>());
        var package = Path.Combine(workspace.Root, "empty.enc");
        var output = Path.Combine(workspace.Root, "restore");

        await _pipeline.EncryptFileAsync(
            input,
            package,
            new[] { new EncryptionLayerConfig(JiamiAlgorithm.Aes256Gcm, "empty-password") });

        var result = await _pipeline.DecryptFileAsync(package, output, new[] { "empty-password" });

        AssertFileBytesEqual(input, result.OutputFilePath);
        AssertEqual(0L, new FileInfo(result.OutputFilePath).Length);
    }

    private async Task RoundTripSingleLayerAsync(JiamiAlgorithm algorithm, string password)
    {
        using var workspace = new TestWorkspace();
        var input = workspace.WriteBytes("sample.bin", DeterministicBytes(65_777));
        var package = Path.Combine(workspace.Root, "sample.txt");
        var output = Path.Combine(workspace.Root, "restore");

        await _pipeline.EncryptFileAsync(
            input,
            package,
            new[] { new EncryptionLayerConfig(algorithm, password) });

        var result = await _pipeline.DecryptFileAsync(package, output, new[] { password });

        AssertFileBytesEqual(input, result.OutputFilePath);
        AssertEqual("sample.bin", Path.GetFileName(result.OutputFilePath));
        AssertEqual(algorithm, result.Header.Layers[0].Algorithm);
    }

    private static byte[] DeterministicBytes(int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)((i * 31 + 17) % 251);
        }

        return bytes;
    }

    private static void AssertFileBytesEqual(string expectedPath, string actualPath)
    {
        var expected = File.ReadAllBytes(expectedPath);
        var actual = File.ReadAllBytes(actualPath);
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException($"File bytes differ: {expectedPath} vs {actualPath}");
        }
    }

    private static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
        }
    }

    private static void AssertFalse(bool condition, string message)
    {
        if (condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected exception {typeof(TException).Name}.");
    }
}

internal sealed class TestWorkspace : IDisposable
{
    public TestWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), $"d4c3-jiami-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string WriteBytes(string fileName, byte[] bytes)
    {
        var path = Path.Combine(Root, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // Test cleanup is best-effort.
        }
    }
}
