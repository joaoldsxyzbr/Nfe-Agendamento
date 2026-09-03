# Hardening pós-auditoria — design

## Objetivo

Fechar os riscos identificados na auditoria da `main` sem reescrever a arquitetura atual do NFe Agendamento. O foco é impedir processamento fiscal concorrente em cenários extremos de failover, tornar atualizações recuperáveis e autenticadas, modernizar o runtime, endurecer CI/release e reduzir o risco de vazamento acidental de material sensível no repositório público.

## Escopo

Este hardening cobre cinco frentes:

1. fencing fiscal imediatamente antes da chamada real à SEFAZ;
2. atualização com staging, assinatura verificável e rollback;
3. migração de .NET 8 para .NET 10 LTS;
4. hardening de GitHub/CI/release e proteção contra commit acidental de segredos;
5. testes automatizados adicionais e checklist físico multi-PC.

Não serão alterados:

- o uso de `http://127.0.0.1:17345` por instalação;
- o compartilhamento `P:\01-Nfe agendamento`;
- o modelo de liderança automática por `central.lock`;
- o pareamento atual dos clientes;
- a criptografia AES-GCM/RSA/HMAC existente da fila;
- o uso do certificado A1 instalado localmente em cada PC candidato;
- o fluxo de DANFE, lote e Portal além das proteções necessárias;
- a regra do projeto de trabalhar diretamente na `main`.

## Princípios

- **Fail closed:** dúvida sobre liderança, estado fiscal, pacote de atualização ou assinatura impede a operação de risco.
- **No automatic fiscal retry:** falha ambígua nunca cria uma segunda chamada automática à SEFAZ.
- **Sem refatoração cosmética:** alterações estruturais só entram quando necessárias às garantias deste documento.
- **Compatibilidade operacional:** clientes já autorizados e estado atual da fila devem continuar válidos.
- **TDD:** cada comportamento crítico novo recebe regressão automatizada antes da implementação.

---

## 1. Fencing fiscal forte

### Problema

Hoje `SharedQueueCentralService.CanProcessWork()` verifica se a instância ainda é líder e se o handle de `central.lock` continua saudável antes do processamento da fila. Porém existe uma janela entre essa verificação e `NfeDistributionTransport.QueryByAccessKeyAsync()`. Se o compartilhamento falhar ou o lock for perdido dentro dessa janela, outro PC pode assumir enquanto o antigo líder ainda avança para a chamada fiscal.

### Design

Será introduzida uma abstração `IFiscalLeadershipGuard` com responsabilidade única: confirmar, no último ponto seguro antes de uma operação fiscal externa, que a instância ainda possui autoridade para iniciar essa operação.

Interface prevista:

```csharp
public interface IFiscalLeadershipGuard
{
    void EnsureCanStartFiscalOperation();
}
```

A implementação de produção usará `SharedQueueCentralService.CanProcessWork()`. Se a liderança não estiver válida, lançará uma exceção específica `FiscalLeadershipLostException`.

O guard será injetado no `NfeDistributionTransport` e chamado imediatamente antes de `HttpClient.PostAsync`.

Fluxo final:

```text
pedido validado
  -> cache
  -> deduplicação
  -> fila fiscal local
  -> cooldown
  -> montar SOAP
  -> IFiscalLeadershipGuard.EnsureCanStartFiscalOperation()
  -> POST SEFAZ
```

A verificação anterior do hosted service continuará existindo. O novo guard não a substitui; ele fecha a janela restante no boundary externo.

### Semântica de erro

`FiscalLeadershipLostException` será tratada como falha segura e não será repetida automaticamente. A mensagem ao usuário indicará que a liderança mudou antes do envio e que a consulta deve ser refeita explicitamente.

Não haverá tentativa de reaproveitar a mesma requisição automaticamente após troca de líder.

### Testes

Cobertura mínima:

- transporte não envia HTTP quando o guard reprova;
- transporte envia exatamente uma vez quando o guard aprova;
- `NfeLookupService` converte perda de liderança em falha segura;
- regressão garante ausência de retry automático;
- fluxo normal de líder permanece inalterado.

---

## 2. Atualizador autenticado e recuperável

### Problema

O atualizador atual já valida HTTPS, tamanho, nome do asset, SHA-256 e zip traversal. Porém o digest é obtido da mesma origem que hospeda o pacote. Além disso, a instalação copia arquivos sobre a pasta atual; uma falha no meio pode deixar arquivos de versões diferentes.

### Autenticidade

A próxima geração de releases terá um manifesto pequeno junto ao ZIP:

```text
Nfe-Agendamento-win-x64.zip
Nfe-Agendamento-update.json
Nfe-Agendamento-update.sig
```

`Nfe-Agendamento-update.json` conterá no mínimo:

```json
{
  "schema": 1,
  "version": "0.1.25",
  "asset": "Nfe-Agendamento-win-x64.zip",
  "size": 80000000,
  "sha256": "...",
  "targetCommit": "..."
}
```

A assinatura será RSA-PSS/SHA-256 sobre os bytes exatos do manifesto. A chave pública ficará embutida no aplicativo. A chave privada de assinatura não será versionada no repositório.

O workflow de release consumirá uma chave privada PKCS#8 em segredo protegido do ambiente de release. A ausência da chave aborta a publicação antes de criar uma release utilizável.

O atualizador aceitará instalação somente quando:

- a versão do manifesto for maior que a versão local;
- o nome do asset for exatamente o esperado;
- tamanho e SHA-256 corresponderem ao ZIP baixado;
- `targetCommit` possuir formato SHA-1 Git válido;
- a assinatura RSA-PSS for válida contra a chave pública embutida;
- o executável extraído existir;
- a versão do executável extraído corresponder ao manifesto.

A verificação atual do digest publicado pelo GitHub será mantida como defesa adicional, não como raiz única de confiança.

### Limitação operacional explícita

A configuração inicial da chave privada de assinatura no GitHub é um segredo externo ao código e não pode ser armazenada no repositório. O código e o workflow serão preparados para exigi-la; a chave pública correspondente será fixa no aplicativo.

### Instalação transacional

O processo será alterado para:

```text
download
 -> verificar assinatura/hash
 -> extrair em staging
 -> validar conteúdo
 -> encerrar app
 -> mover instalação atual para backup versionado
 -> mover staging para instalação
 -> iniciar nova versão
 -> aguardar health check local
 -> sucesso: remover backup
 -> falha: restaurar backup e reiniciar versão anterior
```

O health check será local e simples: o instalador aguardará o processo novo abrir e a porta local responder a `/api/bootstrap` dentro de uma janela limitada.

A troca deverá preferir rename/move dentro do mesmo volume em vez de copiar arquivo por arquivo quando a topologia de diretório permitir.

### Proteções adicionais

- nenhum downgrade;
- nenhum pacote com versão igual;
- nenhum arquivo escapando do staging;
- limite de tamanho mantido;
- backup limitado à versão imediatamente anterior;
- temporários antigos podem ser limpos de forma best-effort;
- falha de rollback será exibida explicitamente, sem apagar o backup.

### Testes

Cobertura mínima:

- assinatura válida aceita;
- assinatura inválida rejeitada;
- manifesto adulterado rejeitado;
- SHA divergente rejeitado;
- versão divergente rejeitada;
- downgrade rejeitado;
- zip traversal continua rejeitado;
- script de instalação contém caminho de rollback;
- health check falho aciona restauração;
- health check bem-sucedido remove backup.

---

## 3. Migração para .NET 10 LTS

### Objetivo

Migrar aplicação e testes de `net8.0-windows` para `net10.0-windows`, mantendo o pacote `win-x64` autocontido e single-file.

### Regras

- atualizar `TargetFramework` dos projetos compatíveis;
- CI e release passam a usar SDK `10.0.x`;
- manter `UseWindowsForms=true`;
- manter `PublishSingleFile=true` e `IncludeNativeLibrariesForSelfExtract=true`;
- atualizar pacotes somente quando necessário para compatibilidade/segurança;
- nenhuma mudança funcional será misturada à migração além das adaptações obrigatórias.

### Testes

Antes de considerar a migração aceita:

- todos os testes .NET;
- todas as regressões JS;
- build Release;
- publish `win-x64` autocontido;
- execução dos testes estáticos de WebView2/Portal/DANFE;
- pacote ZIP gerado pelo CI.

---

## 4. Hardening de repositório, CI e release

### `.gitignore`

Adicionar bloqueios explícitos para artefatos que nunca devem ser versionados:

```text
*.pfx
*.p12
*.pem
*.key
.env
.env.*
secrets.json
*.snk
```

Exceções só serão criadas se algum arquivo público de teste realmente precisar de uma dessas extensões, o que não é esperado hoje.

### Dependabot

Adicionar `.github/dependabot.yml` para:

- NuGet;
- GitHub Actions;
- frequência semanal;
- limite pequeno de PRs abertos para evitar ruído.

Dependabot é defesa de manutenção e não muda o fluxo direto na `main` do desenvolvimento normal.

### Auditoria de dependências

CI executará:

```text
dotnet list Nfe-Agendamento.sln package --vulnerable --include-transitive
```

O job falhará se houver vulnerabilidade reportada com dependência resolvida no grafo. A saída ficará disponível no Actions para diagnóstico.

### CodeQL

Adicionar workflow oficial para C# em push/PR da `main` e execução agendada semanal.

O CodeQL será complementar ao CI atual; não substitui testes.

### Release

O `Release Bridge` continuará sendo o único caminho oficial para publicar pacote instalável. Ele passará a:

1. validar versão;
2. restaurar dependências;
3. executar testes .NET e JS;
4. executar auditoria de dependências;
5. build/publish;
6. calcular hash e tamanho do ZIP;
7. criar manifesto com o SHA exato validado;
8. assinar manifesto;
9. verificar a própria assinatura antes de publicar;
10. criar release com ZIP + manifesto + assinatura.

Nenhuma release será criada se uma etapa anterior falhar.

### Branch `main`

Não será ativada uma política que impeça as alterações diretas solicitadas pelo projeto. O hardening será obtido por CI, release gating, CodeQL e testes, preservando a regra operacional atual.

---

## 5. Testes físicos e aceitação operacional

Automação não substitui a validação de SMB, DPAPI, A1, WebView2 e comportamento de lock entre máquinas Windows reais.

Após as alterações, a documentação terá um checklist obrigatório de aceitação física:

1. abrir pelo menos dois PCs autorizados;
2. confirmar exatamente um líder;
3. consultar no líder;
4. consultar pelo standby;
5. encerrar o líder e confirmar takeover;
6. repetir uma chave em cache e confirmar ausência de nova chamada fiscal;
7. simular perda do compartilhamento no líder e confirmar que nenhum novo POST fiscal começa;
8. restaurar compartilhamento e validar recuperação;
9. validar A1 em cada candidato;
10. validar Portal/WebView2 e download oficial em um líder;
11. executar atualização assinada em uma instalação de teste;
12. validar rollback com pacote/processo deliberadamente incapaz de passar no health check de teste.

Não será provocado `cStat=656` real apenas para teste.

---

## Ordem de implementação

A implementação será feita em blocos independentes e verificáveis:

1. fencing fiscal;
2. testes e atualização transacional;
3. assinatura de release/update;
4. migração .NET 10;
5. `.gitignore`, Dependabot, auditoria e CodeQL;
6. documentação final e checklist operacional;
7. CI completo e publish final.

Cada bloco deve deixar a `main` compilável e testada antes do próximo.

## Critérios de aceite finais

O hardening só estará tecnicamente concluído quando:

- nenhuma chamada à SEFAZ puder começar sem validação final da liderança;
- perda de liderança for fail-closed e sem retry automático;
- updater rejeitar pacote sem assinatura válida;
- atualização possuir rollback automático quando a nova versão não passar no health check;
- solução usar .NET 10 LTS;
- CI testar, compilar, publicar e auditar dependências;
- CodeQL e Dependabot estiverem configurados;
- `.gitignore` bloquear material sensível comum;
- release oficial publicar ZIP, manifesto e assinatura derivados do mesmo SHA testado;
- README e guias refletirem exatamente a arquitetura implementada;
- checklist físico multi-PC estiver documentado para validação em ambiente real.

A validação física permanece um critério operacional separado: o código pode ser considerado pronto para teste real após CI completo, mas não será declarado validado em ambiente da empresa antes dos testes em pelo menos dois PCs reais.