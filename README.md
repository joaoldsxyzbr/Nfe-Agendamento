# NFe Agendamento

Ferramenta interna para consultar e baixar NF-e por chave usando o certificado A1 já instalado no Windows.

## Objetivo

O projeto deve permanecer simples e direto. A primeira etapa entrega apenas a fundação local do aplicativo. Consulta única, lote, DANFE e atualização entram por marcos separados e testáveis.

## Arquitetura alvo

Cada PC roda sua própria instalação. O navegador acessa somente `http://127.0.0.1:17345` no próprio computador. Não existe servidor compartilhado na LAN, login, usuário, `distNSU`, dashboard ou sincronização entre máquinas.

## Segurança

- certificado e chave privada permanecem no Windows Certificate Store;
- nenhuma interface fiscal deve ficar acessível pela LAN;
- XMLs em repouso serão criptografados localmente;
- CI nunca consulta a SEFAZ real nem usa certificado/XML fiscal real.

## Documentação

- Design: `docs/superpowers/specs/2026-08-31-local-browser-nfe-design.md`
- Plano: `docs/superpowers/plans/2026-08-31-foundation-single-lookup.md`
