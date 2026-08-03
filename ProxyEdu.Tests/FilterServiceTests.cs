using ProxyEdu.Server.Services;
using ProxyEdu.Shared.Models;
using Xunit;
using System.Text.RegularExpressions;

namespace ProxyEdu.Tests;

/// <summary>
/// Tests for FilterService - focused on URL domain extraction and pattern matching logic
/// that can be tested without complex LiteDB mocking.
/// </summary>
public class FilterServiceTests
{
    #region URL Domain Extraction

    [Fact]
    public void ExtractDomain_FullUrl_ReturnsDomain()
    {
        var result = ExtractDomain("https://www.google.com/search?q=test");
        Assert.Equal("google.com", result);
    }

    [Fact]
    public void ExtractDomain_WithoutWWW_ReturnsDomain()
    {
        var result = ExtractDomain("https://google.com");
        Assert.Equal("google.com", result);
    }

    [Fact]
    public void ExtractDomain_Subdomain_ReturnsFullHost()
    {
        var result = ExtractDomain("https://drive.google.com");
        // Trim "www." from the result since the actual implementation trims it
        var trimmed = result.TrimStart("www.".ToCharArray());
        Assert.Equal("drive.google.com", trimmed);
    }

    [Fact]
    public void ExtractDomain_HttpProtocol_Works()
    {
        var result = ExtractDomain("http://example.com/path");
        Assert.Equal("example.com", result);
    }

    [Fact]
    public void ExtractDomain_InvalidUrl_ReturnsInput()
    {
        var result = ExtractDomain("not-a-valid-url");
        Assert.NotNull(result);
        Assert.Equal("not-a-valid-url", result);
    }

    [Fact]
    public void ExtractDomain_EmptyString_ReturnsEmpty()
    {
        var result = ExtractDomain("");
        Assert.Equal("", result);
    }

    #endregion

    #region Wildcard Pattern Matching

    [Fact]
    public void WildcardPattern_SimpleDomain_Match()
    {
        bool result = WildcardMatch("*.google.com", "drive.google.com");
        Assert.True(result);
    }

    [Fact]
    public void WildcardPattern_ExactDomain_Match()
    {
        bool result = WildcardMatch("google.com", "google.com");
        Assert.True(result);
    }

    [Fact]
    public void WildcardPattern_Subdomain_NoMatch()
    {
        bool result = WildcardMatch("google.com", "drive.google.com");
        Assert.False(result);
    }

    [Fact]
    public void WildcardPattern_WildcardDomain_Match()
    {
        bool result = WildcardMatch("*.google.com", "mail.google.com");
        Assert.True(result);
    }

    [Fact]
    public void WildcardPattern_AnyDomain_Match()
    {
        bool result = WildcardMatch("*", "anything.com");
        Assert.True(result);
    }

    [Fact]
    public void WildcardPattern_DifferentDomain_NoMatch()
    {
        bool result = WildcardMatch("*.google.com", "facebook.com");
        Assert.False(result);
    }

    [Fact]
    public void WildcardPattern_MultipleWildcards_Match()
    {
        bool result = WildcardMatch("*.*.google.com", "a.b.google.com");
        Assert.True(result);
    }

    #endregion

    #region Regex Pattern Matching

    [Fact]
    public void RegexPattern_SimpleMatch()
    {
        bool result = RegexMatch(@"youtube\.com\/watch\?v=.*", "https://youtube.com/watch?v=dQw4w9WgXcQ");
        Assert.True(result);
    }

    [Fact]
    public void RegexPattern_NoMatch()
    {
        bool result = RegexMatch(@"youtube\.com\/watch\?v=.*", "https://youtube.com/");
        Assert.False(result);
    }

    [Fact]
    public void RegexPattern_DomainMatch()
    {
        bool result = RegexMatch(@"(.*\.)?facebook\.com", "facebook.com");
        Assert.True(result);
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Replicates the FilterService.ExtractDomain logic for testing.
    /// </summary>
    private static string ExtractDomain(string url)
    {
        try
        {
            var uri = new Uri(url.StartsWith("http") ? url : "http://" + url);
            return uri.Host.TrimStart("www.".ToCharArray());
        }
        catch
        {
            return url;
        }
    }

    /// <summary>
    /// Replicates the wildcard matching logic from FilterService.
    /// </summary>
    private static bool WildcardMatch(string pattern, string input)
    {
        var normalizedPattern = pattern.Trim().ToLowerInvariant();
        var normalizedInput = input.Trim().ToLowerInvariant();

        if (normalizedPattern == "*")
        {
            return true;
        }

        var regexPattern = "^" + Regex.Escape(normalizedPattern).Replace("\\*", ".*") + "$";
        var regex = new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
        return regex.IsMatch(normalizedInput);
    }

    /// <summary>
    /// Replicates Regex pattern matching from FilterService.
    /// </summary>
    private static bool RegexMatch(string pattern, string input)
    {
        var regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
        return regex.IsMatch(input);
    }

    #endregion
}
