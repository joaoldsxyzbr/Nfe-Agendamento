namespace NfeAgendamento.App.Fiscal;

public static class AccessKeyValidator
{
    public static bool IsValid(string? accessKey)
    {
        if (accessKey is null || accessKey.Length != 44 || accessKey.Any(character => character is < '0' or > '9'))
            return false;

        var sum = 0;
        var weight = 2;
        for (var index = 42; index >= 0; index--)
        {
            sum += (accessKey[index] - '0') * weight;
            weight = weight == 9 ? 2 : weight + 1;
        }

        var checkDigit = 11 - (sum % 11);
        if (checkDigit >= 10)
            checkDigit = 0;

        return accessKey[43] - '0' == checkDigit;
    }
}
