namespace D4C3Jiami.Core.Crypto;

public class JiamiCryptoException : Exception
{
    public JiamiCryptoException(string message)
        : base(message)
    {
    }

    public JiamiCryptoException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class InvalidPackageException : JiamiCryptoException
{
    public InvalidPackageException(string message)
        : base(message)
    {
    }

    public InvalidPackageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class PasswordVerificationException : JiamiCryptoException
{
    public PasswordVerificationException(string message)
        : base(message)
    {
    }

    public PasswordVerificationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
