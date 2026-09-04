# Assinatura keyless das releases — design

## Contexto
O hardening RSA introduziu uma raiz de confiança externa, mas a chave privada correspondente nunca foi provisionada no GitHub Secret exigido pelo Release Bridge. O pipeline corretamente recusou publicar v0.1.26 sem assinatura.

## Decisão
Usar Sigstore keyless no Release Bridge. O GitHub Actions obtém um token OIDC efêmero, Fulcio emite o certificado de curta duração e a assinatura do ZIP é registrada no transparency log. Nenhuma chave privada persistente é armazenada no repositório, no runner ou em Secret.

## Política do updater
O updater mantém SHA-256 e HTTPS e exige `Nfe-Agendamento-win-x64.zip.sigstore.json`. A verificação aceita somente issuer do GitHub Actions, SAN exato do Release Bridge em main, repositório oficial, ref main, runner hospedado pelo GitHub, repositório público, transparency log e SCT válidos. Qualquer falha aborta antes da extração.

## Release
O Release Bridge recebe `id-token: write`, instala Cosign por action oficial, assina o ZIP, verifica a própria assinatura com identidade/issuer fixos e só então cria a release com ZIP + bundle.

## Compatibilidade
A v0.1.25 não depende da nova verificação para baixar a v0.1.26. A v0.1.26 passa a exigir Sigstore para todas as atualizações seguintes.
