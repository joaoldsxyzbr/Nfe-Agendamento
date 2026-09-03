namespace NfeAgendamento.App.Updates;

internal static class UpdateSigningKey
{
    // RSA 3072-bit SubjectPublicKeyInfo. A chave privada correspondente nunca deve ser versionada.
    private const string SubjectPublicKeyInfoBase64 =
        "MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEA4hP+qNyx/4lX/3muWRiBgyQOqc0yIYAP+sSBdwxHiE6qqyHR+Yw5vZC7rr3QmWTrXKemtSuAMKOhJwL15zlzemBYnt1yGosKbyIcRsybzlwWOofrAYHgFu5oU5AZwPjicvY8HmZLhFyfmQ+91Uj8TgzxRGrDTDBPfUtjo3g+QksBeUHTKavxCdeQKTafp1TCWXgrrWcXIv3mnJQYHowBSLjJ3eYw6a7pvVlqHBlj9a2tPJsm1JiFQCttpmsI9ZRuTGONQ6FG/A/VoErGBUG/SvnxCoAPglrC2F0NSjTpzN8ZpA5pJbvoNn8o8esTikcRdu4DDpsaBmKx4omZaDR3rUGBnHvxiF86jS3/8jmu26s0Mcu8S5ekyApADyjhB3tyn1zAnocGnASVgGMC+bNbh+X+p/0shVprDjqiLdUApnCyA9krx7DIlX0XakRCU0Id1XMT+syXzcqzZ7kkTCwQXwSEBXsnOJvwkkVaUlBSC8u3DdChsNIcCzUXkTFOa/CjAgMBAAE=";

    public static byte[] GetSubjectPublicKeyInfo() => Convert.FromBase64String(SubjectPublicKeyInfoBase64);
}
