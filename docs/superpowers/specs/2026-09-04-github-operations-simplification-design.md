# Simplificação operacional do GitHub — design

## Objetivo

Deixar o fluxo de desenvolvimento, CI e release do NFe Agendamento mais simples, previsível e operável diretamente pelo GitHub, reduzindo duplicação e trabalho manual sem criar novos subsistemas ou aumentar a complexidade estrutural do projeto.

## Princípios

- manter somente os workflows existentes: CI, CodeQL e Release Bridge;
- não criar serviço externo, bot, ambiente de staging, matriz de runners ou cadeia de reusable workflows;
- continuar trabalhando diretamente na `main`;
- preservar todos os gates atuais de segurança, testes, auditoria de dependências e assinatura Sigstore;
- evitar qualquer mudança que altere o comportamento fiscal do aplicativo;
- não publicar uma nova release apenas por causa deste hardening operacional.

## Estado atual

O repositório já possui:

- `.github/workflows/ci.yml` para testes, auditoria de dependências, build e artifact Windows;
- `.github/workflows/codeql.yml` para análise estática de C#;
- `.github/workflows/release-bridge.yml` para restore, auditoria, testes, build, publish, assinatura Sigstore, verificação e publicação da release;
- `tests/js/release-readiness-regression.test.js` protegendo invariantes do processo de release.

Os principais problemas atuais são:

1. CI e Release Bridge duplicam vários comandos de verificação;
2. a release depende de `workflow_dispatch`, exigindo acionamento manual;
3. as notas da release estão fixas em texto específico da v0.1.26;
4. o README ainda contém referências operacionais antigas da v0.1.25 e da assinatura RSA por secret;
5. pequenas proteções operacionais de CI, como `permissions`, timeout e retenção de artifact, podem ser explicitadas sem aumentar a arquitetura.

## Solução proposta

### 1. Script único de verificação

Criar `scripts/verify.ps1` como fonte única dos gates comuns entre CI e release.

Responsabilidades:

- `dotnet restore Nfe-Agendamento.sln` quando solicitado pelo chamador;
- auditoria NuGet com `--vulnerable --include-transitive --format json`;
- `dotnet test Nfe-Agendamento.sln -c Release --no-restore`;
- execução dos testes JS de regressão existentes;
- `dotnet build Nfe-Agendamento.sln -c Release --no-restore`.

O script deve falhar imediatamente quando qualquer gate falhar.

CI e Release Bridge passam a chamar o mesmo script, eliminando divergência entre os dois fluxos.

### 2. Release acionável pelo GitHub sem clique manual

Adicionar `.github/release-request.json` com um único campo de versão semântica, por exemplo:

```json
{
  "version": "0.1.26"
}
```

O Release Bridge continuará aceitando `workflow_dispatch` como fallback, mas também será disparado por `push` na `main` quando esse arquivo for alterado.

Regras:

- somente alteração desse arquivo dispara o caminho automático de release;
- a versão deve ser normalizada para `vX.Y.Z`;
- a versão precisa ser maior que a última tag existente;
- a versão precisa coincidir com `<Version>` do projeto;
- tag existente causa falha segura;
- checkout e release permanecem presos ao mesmo SHA imutável;
- um push comum na `main` não cria release.

Isso permite que a solicitação “gere a nova versão” seja executada de ponta a ponta pelo GitHub sem interação manual.

### 3. Release notes sem texto hard-coded

Remover notas fixas específicas da v0.1.26.

A release deve usar notas geradas pelo GitHub a partir das mudanças reais entre tags, mantendo título e tag explícitos.

O objetivo é evitar que uma versão futura publique descrição obsoleta.

### 4. Pequeno hardening do CI

Sem adicionar jobs ou workflows:

- declarar `permissions: contents: read` no CI;
- adicionar `timeout-minutes` ao job de CI;
- definir retenção curta no artifact de build de CI;
- manter `concurrency` e cancelamento de execução obsoleta;
- preservar CodeQL separado e simples.

### 5. Testes de regressão do fluxo

Atualizar `tests/js/release-readiness-regression.test.js` para proteger:

- existência do `scripts/verify.ps1`;
- uso do mesmo script por CI e Release Bridge;
- existência do `release-request.json`;
- trigger de `push` limitado ao arquivo de solicitação;
- manutenção de `workflow_dispatch` como fallback;
- validação de versão do request contra a versão do projeto;
- ausência de release notes hard-coded da v0.1.26;
- preservação de Sigstore keyless, OIDC e SHA imutável;
- ausência de workflow legado adicional.

### 6. Documentação

Atualizar README para refletir o estado real:

- última release publicada: v0.1.26;
- assinatura oficial via Sigstore keyless;
- ausência de chave RSA persistente ou secret de assinatura;
- novo fluxo de release por arquivo de solicitação;
- manutenção de `workflow_dispatch` como fallback operacional.

## Arquivos previstos

Criar:

- `scripts/verify.ps1`
- `.github/release-request.json`

Modificar:

- `.github/workflows/ci.yml`
- `.github/workflows/release-bridge.yml`
- `tests/js/release-readiness-regression.test.js`
- `README.md`

Não criar:

- novos workflows;
- serviços externos;
- bots;
- environments;
- branches de release;
- mecanismos paralelos de publicação.

## Fluxo final

### Alteração comum

```text
push na main
  -> CI
  -> CodeQL
```

### Release

```text
alterar release-request.json na main
  -> Release Bridge
  -> validar versão e SHA
  -> verify.ps1
  -> publish Windows
  -> Sigstore sign
  -> Sigstore verify
  -> GitHub release
```

### Fallback

```text
workflow_dispatch
  -> mesmo Release Bridge
  -> mesmos gates
```

## Critérios de sucesso

- somente três workflows permanecem no repositório: CI, CodeQL e Release Bridge;
- CI e release não duplicam a suíte de verificação;
- nenhum push comum na `main` publica release;
- a alteração explícita do request de release pode iniciar a publicação;
- versão do request, versão do projeto e tag são coerentes;
- release continua vinculada ao SHA testado;
- Sigstore keyless continua obrigatório;
- README reflete a release v0.1.26 e o fluxo atual;
- nenhuma nova infraestrutura operacional é introduzida.

## Fora de escopo

- alterar lógica fiscal, fila distribuída, cache, certificado A1 ou DANFE;
- adicionar staging;
- alterar arquitetura multi-PC;
- criar release nova apenas para validar este hardening;
- adicionar secrets persistentes;
- automatizar ações externas fora do GitHub.
