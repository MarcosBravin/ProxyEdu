using ProxyEdu.Server.Security;
using Xunit;

namespace ProxyEdu.Tests;

/// <summary>
/// Tests for PasswordHasher - a pure utility class that doesn't need mocking.0
/// AuthService tests require complex LiteDB mocking, covered by integration tests.
/// </summary>
public class AuthServiceTests
{
    [Fact]
    public void HashPassword_ReturnsHashAndSalt()
    {
        var (hash, salt) = PasswordHasher.HashPassword("test-password");

        Assert.NotNull(hash);
        Assert.NotNull(salt);
        Assert.NotEmpty(hash);
        Assert.NotEmpty(salt);
        Assert.NotEqual(hash, salt);
    }

    [Fact]
    public void VerifyPassword_CorrectPassword_ReturnsTrue()
    {
        var password = "my-secure-password-123";
        var (hash, salt) = PasswordHasher.HashPassword(password);

        var result = PasswordHasher.VerifyPassword(password, hash, salt);

        Assert.True(result);
    }

    [Fact]
    public void VerifyPassword_WrongPassword_ReturnsFalse()
    {
        var (hash, salt) = PasswordHasher.HashPassword("correct-password");

        var result = PasswordHasher.VerifyPassword("wrong-password", hash, salt);

        Assert.False(result);
    }

    [Fact]
    public void VerifyPassword_EmptyPassword_ReturnsFalse()
    {
        var (hash, salt) = PasswordHasher.HashPassword("some-password");

        var result = PasswordHasher.VerifyPassword("", hash, salt);

        Assert.False(result);
    }

    [Fact]
    public void HashPassword_DifferentPasswords_DifferentHashes()
    {
        var (hash1, _) = PasswordHasher.HashPassword("password-1");
        var (hash2, _) = PasswordHasher.HashPassword("password-2");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void HashPassword_SamePassword_DifferentSalts()
    {
        var (hash1, salt1) = PasswordHasher.HashPassword("same-password");
        var (hash2, salt2) = PasswordHasher.HashPassword("same-password");

        // Same password should produce different salts (and thus different hashes)
        Assert.NotEqual(salt1, salt2);
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void VerifyPassword_InvalidBase64Hash_ReturnsFalse()
    {
        var result = PasswordHasher.VerifyPassword("password", "not-valid-base64!!!", "YWJjZGVmZw==");

        Assert.False(result);
    }

    [Fact]
    public void VerifyPassword_InvalidBase64Salt_ReturnsFalse()
    {
        var (_, salt) = PasswordHasher.HashPassword("test");
        var result = PasswordHasher.VerifyPassword("password", "YWJjZGVmZw==", salt);

        Assert.False(result);
    }

    [Fact]
    public void VerifyPassword_TamperedHash_ReturnsFalse()
    {
        var password = "my-password";
        var (hash, salt) = PasswordHasher.HashPassword(password);

        // Tamper with the hash slightly
        var hashBytes = System.Convert.FromBase64String(hash);
        hashBytes[0] ^= 0x01; // Flip one bit
        var tamperedHash = System.Convert.ToBase64String(hashBytes);

        var result = PasswordHasher.VerifyPassword(password, tamperedHash, salt);

        Assert.False(result);
    }

    [Fact]
    public void HashPassword_ProducesValidPBKDF2Output()
    {
        var (hash, salt) = PasswordHasher.HashPassword("password");

        // PBKDF2 with SHA256 produces 32-byte keys
        // Base64 encoded = 44 characters
        var hashBytes = System.Convert.FromBase64String(hash);
        Assert.Equal(32, hashBytes.Length);

        // Salt is 16 bytes
        var saltBytes = System.Convert.FromBase64String(salt);
        Assert.Equal(16, saltBytes.Length);
    }
}
