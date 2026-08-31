using NfeAgendamento.App.Fiscal;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class AccessKeyValidatorTests
{
    private const string ValidKey = "42260812345678000123550010000012341000012342";

    [Fact]
    public void Valid_44_digit_key_with_correct_check_digit_is_accepted() =>
        Assert.True(AccessKeyValidator.IsValid(ValidKey));

    [Theory]
    [InlineData("")]
    [InlineData("4226081234567800012355001000001234100001234")]
    [InlineData("422608123456780001235500100000123410000123420")]
    [InlineData("4226081234567800012355001000001234100001234X")]
    [InlineData("42260812345678000123550010000012341000012343")]
    public void Invalid_keys_are_rejected(string value) =>
        Assert.False(AccessKeyValidator.IsValid(value));
}
