# Central LAN Architecture — plano e estado atual

**Objetivo:** operar o NFe Agendamento como uma Central Windows única para a rede interna, mantendo certificado A1, XMLs e comunicação fiscal somente no PC central.

**Stack:** .NET 8, Windows Forms, ASP.NET Core minimal APIs, xUnit, JavaScript e DPAPI.

**Especificação atual:** `docs/superpowers/specs/2026-09-01-central-lan-architecture-design.md`

> Este documento substitui o desenho inicial que previa senha local e consulta em lote. Durante a implementação, ambos foram removidos para simplificar a operação. O estado abaixo representa o código atual da `main`.

## Bloco 1 — Central Windows

- [x] Janela principal da Central.
- [x] Status ativa/parada.
- [x] IPv4, porta e URL de acesso.
- [x] Ações Iniciar Central, Parar Central e Abrir sistema.
- [x] Estado persistente entre reinicializações.
- [x] Integração com bandeja do Windows.

## Bloco 2 — Rede e Firewall

- [x] Seleção robusta de IPv4 da interface utilizável.
- [x] Listener preparado para a LAN na porta `17345`.
- [x] Diagnóstico de interface de rede.
- [x] Diagnóstico do listener TCP.
- [x] Diagnóstico do Firewall do Windows.
- [x] Configuração de firewall via UAC restrita a perfil Privado, TCP `17345` e executável atual.
- [x] Fallback operacional por IPv4 quando mDNS não funciona.

## Bloco 3 — Robustez fiscal

- [x] Fila fiscal única e serializada.
- [x] Deduplicação de consultas simultâneas da mesma chave.
- [x] Limite de 12 operações únicas admitidas.
- [x] `429 fila_ocupada` com `Retry-After`.
- [x] Cooldown persistente de uma hora para `cStat=656`.
- [x] Revalidação do cooldown para operações já enfileiradas.
- [x] Retry limitado para falhas transitórias.
- [x] Timeout/falha de rede/resposta inválida convertidos em erros controlados.
- [x] Falha fechada quando o estado fiscal persistido não pode ser validado.
- [x] Auditoria operacional sem dados fiscais completos.

## Bloco 4 — Operação LAN e feedback

- [x] Bandeja mostra endereço atual da Central.
- [x] Ação para copiar o endereço compartilhável.
- [x] Navegador diferencia fila cheia de bloqueio `656`.
- [x] Navegador usa `Retry-After` para fila cheia.
- [x] Navegador mostra horário de desbloqueio da SEFAZ.
- [x] README e guia operacional atualizados.

## Bloco 5 — Verificação e prontidão de release

### Verificação automatizada

- [x] `dotnet test Nfe-Agendamento.sln -c Release` em runner Windows/.NET 8.
- [x] `dotnet build Nfe-Agendamento.sln -c Release` em runner Windows/.NET 8.
- [x] Mesmo-key concorrente coberto por teste e produz uma única operação de transporte.
- [x] `656` comprovado entre novas instâncias do serviço, sem nova chamada ao transporte.
- [x] Bootstrap comprovado sem detalhes de certificado ou XML.
- [x] Segurança LAN ativa/parada coberta por testes do middleware.
- [x] Regressão Fernando Klein no CI.
- [x] Regressão do feedback fiscal no CI.
- [x] Regressão de prontidão de release no CI.
- [x] CI verificado sem certificado `.pfx/.p12`, credenciais fiscais ou transporte SEFAZ real.
- [x] Release Bridge executa todas as verificações antes de publicar.
- [x] Workflow legado de release por tag removido; há um único fluxo oficial de release.
- [x] Publish Windows x64 autocontido e ZIP validados no CI.

### Aceitação física da próxima release

Esses itens dependem da rede e dos computadores reais e não podem ser concluídos pelo GitHub Actions:

- [ ] instalar a próxima release no PC central;
- [ ] confirmar acesso local em `http://127.0.0.1:17345`;
- [ ] confirmar no painel **Rede: OK**, **Servidor: OK** e **Firewall: OK**;
- [ ] acessar `http://IP-DO-CENTRAL:17345` a partir de um segundo PC;
- [ ] consultar uma NF-e conhecida pelo segundo PC;
- [ ] validar DANFE e download XML pelo cliente;
- [ ] confirmar que o certificado A1 continua somente no PC central.

Não provocar um `cStat=656` real apenas para teste: a persistência do cooldown e o bloqueio de novas chamadas após reinício já são cobertos automaticamente.

## Critério para publicar a próxima versão

O código pode entrar no workflow **Release Bridge** somente com CI verde. A publicação cria um pacote Windows autocontido. A versão só deve ser considerada validada para uso LAN depois que o checklist físico acima for executado no ambiente real.
