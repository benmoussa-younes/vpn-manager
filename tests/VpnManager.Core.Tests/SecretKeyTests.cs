using VpnManager.Core.Model;

namespace VpnManager.Core.Tests;

/// <summary>
/// The redaction contract: a key cannot reach a log line or a view through
/// string formatting, and reading it requires the one explicit call.
/// </summary>
public sealed class SecretKeyTests
{
    [Fact]
    public void A_key_is_redacted_by_ToString()
    {
        var key = SecretKey.FromValue(TestData.InterfacePrivateKey);

        Assert.Equal("[redacted]", key.ToString());
        Assert.DoesNotContain(TestData.InterfacePrivateKey, key.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_key_says_so_instead_of_printing_nothing()
    {
        Assert.Equal("(none)", SecretKey.Empty.ToString());
        Assert.True(SecretKey.Empty.IsEmpty);
    }

    [Fact]
    public void Interpolation_of_a_key_is_redacted()
    {
        var key = SecretKey.FromValue(TestData.InterfacePrivateKey);

        var rendered = $"key={key}";

        Assert.Equal("key=[redacted]", rendered);
    }

    [Fact]
    public void The_raw_value_is_only_reachable_through_Reveal()
    {
        var key = SecretKey.FromValue(TestData.PresharedKey);

        Assert.Equal(TestData.PresharedKey, key.Reveal());
        Assert.False(key.IsEmpty);
    }

    [Fact]
    public void Keys_compare_by_value()
    {
        var first = SecretKey.FromValue(TestData.PeerOneKey);
        var second = SecretKey.FromValue(TestData.PeerOneKey);
        var other = SecretKey.FromValue(TestData.PeerTwoKey);

        Assert.True(first.Equals(second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.False(first.Equals(other));
        Assert.False(first.Equals(null));
        Assert.False(first.Equals((object)null!));
        Assert.True(first.Equals((object)second));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_missing_value_becomes_the_empty_key(string? value)
    {
        var key = SecretKey.FromValue(value);

        Assert.True(key.IsEmpty);
        Assert.Equal(string.Empty, key.Reveal());
    }
}
