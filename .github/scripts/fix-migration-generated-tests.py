from pathlib import Path

path = Path("tests/NfeAgendamento.App.Tests/UpdateServiceTests.cs")
text = path.read_text(encoding="utf-8")
replacements = {
    'VerifiedPackageClient(zip, "{"mediaType":"test"}")': 'VerifiedPackageClient(zip, "{}")',
    'Assert.Equal("{"mediaType":"test"}", verifier.LastBundle);': 'Assert.Equal("{}", verifier.LastBundle);',
}
for old, new in replacements.items():
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"esperado 1 match, encontrado {count}: {old}")
    text = text.replace(old, new, 1)
path.write_text(text, encoding="utf-8")
print("generated test strings fixed")
