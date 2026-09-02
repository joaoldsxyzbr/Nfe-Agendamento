# NFe Agendamento — arquitetura LAN anterior

## Status

**SUPERSEDED em 2026-09-02.**

Este documento descrevia a arquitetura em que outros PCs acessavam diretamente o servidor HTTP do PC Central pela porta `17345`, com descoberta de IP/mDNS e regra de entrada no Firewall do Windows.

Essa arquitetura não é mais operacional na `main` e não deve ser usada como guia de instalação ou segurança.

## Arquitetura que substituiu esta

A decisão atual está documentada em:

- `docs/superpowers/specs/2026-09-02-shared-folder-queue-design.md`
- `docs/superpowers/plans/2026-09-02-shared-folder-queue.md`
- `docs/CENTRAL-LAN.md` — nome histórico do arquivo, conteúdo atualizado para o fluxo por pasta compartilhada.

Resumo da arquitetura atual:

```text
cada PC: http://127.0.0.1:17345
        ↓
P:\01-Nfe agendamento
        ↓
PC Central + certificado A1 + SEFAZ
```

A comunicação entre computadores usa envelopes criptografados na pasta compartilhada. O servidor web fica restrito ao loopback, e o aplicativo não cria regra de firewall, não anuncia `nfeagendamento.local` e não usa HTTP LAN como fallback.

Este arquivo permanece somente para registrar a decisão arquitetural que foi substituída.
