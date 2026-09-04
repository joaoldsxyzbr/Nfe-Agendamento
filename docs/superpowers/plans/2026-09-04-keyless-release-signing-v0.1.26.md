# Keyless Release Signing v0.1.26 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publicar v0.1.26 sem chave privada persistente, mantendo assinatura criptográfica fail-closed.
**Architecture:** Release Bridge assina o ZIP via Sigstore keyless/GitHub OIDC; o updater valida SHA-256 e o bundle Sigstore contra a identidade fixa do workflow oficial antes do staging.
**Tech Stack:** .NET 10, Sigstore 0.5.0, GitHub Actions OIDC, Cosign 3.x.
**Spec:** `docs/superpowers/specs/2026-09-04-keyless-release-signing-design.md`

## Global Constraints
- Trabalhar direto na `main`.
- Não versionar chave privada.
- Não remover SHA-256, HTTPS, rollback ou health check.
- Release deve falhar antes da publicação se assinatura ou verificação falhar.

---

### Task 1: Verificação do updater
**Files:** `UpdateService.cs`, `SigstoreUpdateSignatureVerifier.cs`, `UpdateServiceTests.cs`
- [x] Trocar asset `.sig` por `.sigstore.json`.
- [x] Injetar verificador para testes sem rede.
- [x] Fixar issuer, workflow, repositório, ref e runner.
- [x] Validar bundle antes de extrair.

### Task 2: Release Bridge
**Files:** `.github/workflows/release-bridge.yml`, `tests/js/release-readiness-regression.test.js`
- [x] Remover dependência do Secret RSA.
- [x] Conceder `id-token: write`.
- [x] Assinar com Cosign keyless.
- [x] Verificar identidade/issuer antes de `gh release create`.
- [x] Publicar ZIP + bundle.

### Task 3: Versão e documentação
**Files:** `NfeAgendamento.App.csproj`, `README.md`, `docs/ATUALIZACAO-E-INICIALIZACAO.md`
- [x] Subir base para 0.1.26.
- [x] Documentar raiz de confiança keyless.
- [x] Validar restore, auditoria, testes, regressões e build antes do commit.
