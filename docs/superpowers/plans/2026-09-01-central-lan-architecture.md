# Central LAN Architecture — plano histórico

## Status

**SUPERSEDED em 2026-09-02.**

Este plano registrou a etapa em que o NFe Agendamento expunha o servidor do PC Central diretamente para a LAN na porta `17345`.

Ele não representa mais a `main` e não deve ser usado para instalação, diagnóstico ou segurança.

A implementação atual está definida em:

- `docs/superpowers/specs/2026-09-02-shared-folder-queue-design.md`
- `docs/superpowers/plans/2026-09-02-shared-folder-queue.md`
- `docs/CENTRAL-LAN.md`

Na arquitetura atual, cada PC usa `127.0.0.1:17345` localmente e a comunicação entre computadores ocorre exclusivamente por envelopes criptografados em `P:\01-Nfe agendamento`.

O código de firewall, mDNS, descoberta de IPv4 e acesso HTTP remoto foi removido do caminho operacional.
