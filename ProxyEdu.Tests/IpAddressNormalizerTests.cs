using ProxyEdu.Server.Services;
using Xunit;

namespace ProxyEdu.Tests;

public class IpAddressNormalizerTests
{
    [Fact]
    public void Normalize_IPv4_ReturnsSame()
    {
        var result = IpAddressNormalizer.Normalize("192.168.1.100");
        Assert.Equal("192.168.1.100", result);
    }

    [Fact]
    public void Normalize_IPv6MappedToIPv4_ReturnsIPv4()
    {
        var result = IpAddressNormalizer.Normalize("::ffff:192.168.1.100");
        Assert.Equal("192.168.1.100", result);
    }

    [Fact]
    public void Normalize_IPv6LinkLocal_StripsZoneIndex()
    {
        var result = IpAddressNormalizer.Normalize("fe80::1%12");
        Assert.Equal("fe80::1", result);
    }

    [Fact]
    public void Normalize_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, IpAddressNormalizer.Normalize(null));
        Assert.Equal(string.Empty, IpAddressNormalizer.Normalize(""));
        Assert.Equal(string.Empty, IpAddressNormalizer.Normalize("   "));
    }

    [Fact]
    public void Normalize_TrimsWhitespace()
    {
        var result = IpAddressNormalizer.Normalize("  192.168.1.1  ");
        Assert.Equal("192.168.1.1", result);
    }

    [Fact]
    public void EqualsNormalized_SameIp_ReturnsTrue()
    {
        Assert.True(IpAddressNormalizer.EqualsNormalized("192.168.1.100", "192.168.1.100"));
    }

    [Fact]
    public void EqualsNormalized_OneNull_ReturnsFalse()
    {
        Assert.False(IpAddressNormalizer.EqualsNormalized(null, "192.168.1.1"));
    }

    [Fact]
    public void EqualsNormalized_BothNull_ReturnsTrue()
    {
        Assert.True(IpAddressNormalizer.EqualsNormalized(null, null));
    }

    [Fact]
    public void Normalize_Localhost_ReturnsLocalhost()
    {
        var result = IpAddressNormalizer.Normalize("127.0.0.1");
        Assert.Equal("127.0.0.1", result);
    }

    [Fact]
    public void Normalize_IPv6Localhost_ReturnsLocalhost()
    {
        var result = IpAddressNormalizer.Normalize("::1");
        Assert.Equal("::1", result);
    }
}
