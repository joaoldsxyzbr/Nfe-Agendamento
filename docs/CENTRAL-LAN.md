# Central/LAN — arquitetura desativada

> **Documento histórico.** A arquitetura com PC Central, líder automático, standby, pareamento e fila remota foi desativada em 08/09/2026.

O NFe Agendamento **não deve mais ser configurado como Central** e nenhum PC precisa ser pareado.

## Arquitetura vigente

Cada PC:

- executa a interface apenas em `http://127.0.0.1:17345`;
- usa seu próprio certificado A1 local;
- consulta a SEFAZ diretamente;
- usa `P:\01-Nfe agendamento\status\fiscal.lock` para serializar operações fiscais entre computadores;
- usa `status\fiscal-cooldown.bin` para compartilhar o cooldown de `cStat=656`;
- mantém XML/cache somente local, protegido por DPAPI.

Não existe servidor HTTP LAN, `central.lock`, heartbeat operacional, código de pareamento ou lista ativa de PCs autorizados no fluxo de produção.

## Migração

Atualize todos os PCs que utilizam a mesma pasta compartilhada antes de voltar a fazer consultas simultâneas. Não mantenha versões antigas baseadas em líder/pareamento operando ao mesmo tempo que a arquitetura nova.

Arquivos/diretórios antigos podem continuar fisicamente no compartilhamento, mas não participam do fluxo ativo.

Para documentação atual, consulte:

- [README](../README.md)
- [Validação física multi-PC](TESTE-MULTI-PC.md)
- [Design da arquitetura sem central](superpowers/specs/2026-09-08-no-central-shared-lock-design.md)
