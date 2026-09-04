# Hardening pós-auditoria v0.1.30 — plano concluído

**Goal:** eliminar os riscos pós-auditoria da v0.1.30 com bootstrap recuperável, revogação criptográfica, pareamento one-shot, supply chain fixada por SHA, atualização validada pela versão realmente iniciada e Portal/WebView2 robusto.

**Spec:** `docs/superpowers/specs/2026-09-04-post-audit-hardening-design.md`

## Status final

O hardening foi concluído e publicado como **v0.1.31**.

Release testada/publicada a partir do SHA:

```text
5ff487f92183d79199521ccc1e864bb5051458a8
```

Gates desse SHA:

- [x] `scripts/verify.ps1 -Restore` GREEN no Release Bridge;
- [x] CI GREEN;
- [x] CodeQL GREEN;
- [x] pacote Windows gerado;
- [x] assinatura Sigstore keyless criada;
- [x] assinatura Sigstore verificada antes da publicação;
- [x] release `v0.1.31` criada apontando exatamente para o SHA testado;
- [x] ZIP e bundle `.sigstore.json` publicados.

A validação física multi-PC continua separada em `docs/TESTE-MULTI-PC.md`. Ela não é uma pendência de implementação: depende de PCs Windows reais, SMB, certificado A1 e WebView2 reais e não pode ser marcada automaticamente por CI.

## Tasks

### Task 1 — Bootstrap recuperável e remoção do reflection legado

- [x] chave local preparada é reutilizada após interrupção;
- [x] bootstrap persiste a chave antes da identidade compartilhada;
- [x] migração deixou de acessar campo privado via reflection;
- [x] testes GREEN.

### Task 2 — Pareamento one-shot

- [x] código permanece válido se houver falha antes da autorização concluir;
- [x] código é consumido somente depois do sucesso;
- [x] mesmo código não autoriza outro PC;
- [x] duplicidade no mesmo cliente é serializada;
- [x] testes GREEN.

### Task 3 — Estado preparado para rotação

- [x] identidade, lista autorizada e cooldown possuem staging recuperável;
- [x] promoção atômica implementada;
- [x] reparse points operacionais rejeitados;
- [x] purge explícito do cache antigo implementado;
- [x] testes GREEN.

### Task 4 — Revogação e rotação recuperável

- [x] revogação gera nova chave do grupo e nova identidade RSA;
- [x] cooldown fiscal é preservado;
- [x] revogado não recebe bundle novo;
- [x] candidatos restantes recebem estado novo;
- [x] cadeia RSA assinada permite recuperação de candidato offline;
- [x] rotação pendente bloqueia novo trabalho fiscal;
- [x] recuperação via `rotation.json` implementada;
- [x] testes GREEN.

### Task 5 — Gerenciamento de PCs autorizados

- [x] `GET /api/pairing/clients` implementado;
- [x] `POST /api/pairing/revoke` implementado;
- [x] endpoints restritos ao líder;
- [x] segredo do cliente não é exposto;
- [x] autorrevogação do líder bloqueada;
- [x] interface com confirmação explícita;
- [x] testes GREEN.

### Task 6 — Health check vinculado à versão

- [x] `/api/bootstrap` expõe `appVersion`;
- [x] instalador exige HTTP 2xx + objeto JSON válido;
- [x] `appVersion` deve ser string escalar;
- [x] comparação ordinal exata com a versão preparada;
- [x] versão divergente provoca rollback;
- [x] regressões PowerShell/JS e testes .NET GREEN.

### Task 7 — GitHub Actions fixadas por SHA

- [x] CI fixado por SHAs exatos;
- [x] CodeQL fixado por SHAs exatos;
- [x] Release Bridge fixado por SHAs exatos;
- [x] Cosign installer fixado por SHA;
- [x] regressão impede retorno a tags móveis;
- [x] Dependabot permanece responsável por atualizações.

### Task 8 — Portal/WebView2 e threat model

- [x] lifecycle tardio do WebView2 tratado de forma estreita;
- [x] apenas `ObjectDisposedException`, `InvalidOperationException` e HRESULTs COM conhecidos de encerramento são tolerados;
- [x] falhas COM genéricas e I/O continuam visíveis;
- [x] callbacks de navegação/download não derrubam a janela durante encerramento esperado;
- [x] loopback threat model documentado;
- [x] SMB sem Offline Files documentado;
- [x] cancelamento fiscal conservador documentado;
- [x] TDD comprovado: RED em `ef1f6d4`, GREEN em `16cae2e`.

### Task 9 — Verificação integral e release

- [x] versão alinhada para `0.1.31`;
- [x] README alinhado;
- [x] `.github/release-request.json` alinhado;
- [x] gate integral GREEN;
- [x] CI GREEN;
- [x] CodeQL GREEN;
- [x] Release Bridge GREEN;
- [x] Sigstore verificado;
- [x] release aponta para o SHA testado;
- [x] v0.1.31 publicada.

## Pendência operacional externa ao código

Somente a **validação física multi-PC** permanece para aceitação em ambiente real. O roteiro completo está em `docs/TESTE-MULTI-PC.md` e cobre eleição/failover real, SMB, A1, pareamento/revogação, Portal/WebView2 e atualização/rollback em máquinas distintas.
