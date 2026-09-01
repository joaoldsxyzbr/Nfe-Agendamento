# NFe Agendamento

Aplicativo Windows interno para consultar, visualizar e baixar NF-e usando o certificado A1 instalado somente no PC central.

## Versão publicada

**v0.1.16**

A `main` já contém os **Blocos 1 a 5** da evolução da Central Windows. Essas mudanças ainda não estão em uma nova release pública.

## Arquitetura atual

```text
PCs da equipe
    ↓ navegador
http://IP-DO-PC-CENTRAL:17345
    ↓
Central NFe Agendamento
    ↓
Certificado A1 + cache + fila + SEFAZ
```

O certificado A1 e a chave privada permanecem no Windows Certificate Store do PC central. XMLs, cache, cooldown e auditoria também permanecem locais; nada disso é enviado para GitHub, nuvem ou navegadores clientes.

## Blocos implementados

### Bloco 1 — Central Windows

- janela **Central NFe Agendamento**;
- status ativa/parada;
- IPv4, porta `17345` e URL de acesso;
- ações **Iniciar Central**, **Parar Central** e **Abrir sistema**;
- estado persistido em `%LOCALAPPDATA%\NfeAgendamento\state\central.json`;
- operação em bandeja.

### Bloco 2 — Rede e Firewall

- seleção do IPv4 utilizável da rede;
- diagnóstico de interface, listener e firewall;
- servidor preparado para atender a LAN em `17345`;
- configuração via UAC de regra de entrada TCP restrita ao perfil **Privado**, porta `17345` e executável atual;
- `nfeagendamento.local` continua opcional via mDNS; o IPv4 é o fallback confiável.

### Bloco 3 — Robustez fiscal

- fila fiscal única e serializada;
- limite de 12 operações únicas admitidas;
- deduplicação de consultas simultâneas da mesma chave;
- fila cheia retorna HTTP `429`, status `fila_ocupada` e `Retry-After: 5`;
- `cStat=656` cria cooldown persistente de uma hora;
- consultas já aguardando revalidam o cooldown antes de tocar na SEFAZ;
- até 3 tentativas somente para falhas transitórias;
- timeout e respostas inválidas viram erros controlados;
- estado fiscal corrompido falha fechado, sem nova consulta à SEFAZ;
- auditoria local sem XML, chave completa, certificado ou CPF/CNPJ.

### Bloco 4 — Operação pelos clientes

- bandeja mostra `Acesso: http://IP:17345`;
- ação **Copiar endereço da Central**;
- navegador diferencia **Central ocupada** de bloqueio **SEFAZ / cStat=656**;
- mensagens usam o `Retry-After` e o horário real de desbloqueio.

### Bloco 5 — Prontidão de release

- CI e Release Bridge executam testes .NET e todas as regressões JS;
- existe um único caminho oficial de publicação: **Release Bridge** manual;
- workflow legado de release por tag foi removido;
- regressão impede dependência de certificado `.pfx/.p12`, credencial fiscal ou transporte SEFAZ real nos testes/workflows;
- teste comprova que o cooldown `656` continua válido em uma nova instância do serviço e bloqueia o transporte;
- teste comprova que `/api/bootstrap` expõe somente dados operacionais (`csrfToken`, `lanMode`, `accessUrl`), sem XML ou dados de certificado.

A última verificação automatizada do Bloco 5 passou com **90/90 testes .NET**, regressão Fernando Klein, regressão do feedback fiscal, regressão de prontidão de release, build e pacote Windows.

> A aceitação física da LAN continua obrigatória após gerar a próxima release: instalar no PC central e abrir o endereço exibido pelo painel a partir de um segundo computador. O CI não consegue substituir esse teste da rede real da empresa.

## Uso no PC central

1. Execute `NfeAgendamento.App.exe`.
2. Confirme **Central ativa**.
3. Confira **Rede**, **Servidor** e **Firewall**.
4. Se necessário, use **Configurar firewall** e autorize o UAC.
5. Abra o sistema local.
6. Selecione o certificado A1 válido e a UF autora.
7. Faça uma consulta individual de teste.

Fechar a janela mantém o aplicativo na bandeja. Para encerrar completamente, use **Sair**.

## Uso nos outros PCs

Use o endereço mostrado no painel ou copiado pela bandeja, por exemplo:

```text
http://10.0.0.29:17345
```

Não use `127.0.0.1` em outro computador: esse endereço sempre aponta para o próprio PC que está acessando.

Se a rede suportar mDNS, também pode funcionar:

```text
http://nfeagendamento.local:17345
```

Os computadores clientes precisam apenas de navegador. Não copie nem instale o certificado A1 neles.

## Consulta fiscal

A consulta em lote foi removida. Todas as consultas são individuais e coordenadas pela Central.

Quando a fila atingir 12 operações únicas, uma nova chave recebe `429 fila_ocupada`. Isso significa apenas que a Central está ocupada.

Quando a SEFAZ retornar `cStat=656`, a Central grava um bloqueio de uma hora. Nesse caso, não force novas tentativas; aguarde o horário informado na interface.

## Cache e dados locais

Os dados ficam em:

```text
%LOCALAPPDATA%\NfeAgendamento
```

Principais itens:

- cache XML criptografado por DPAPI;
- estado/cooldown fiscal criptografado por DPAPI;
- configuração da Central;
- auditoria em `logs\fiscal-audit.jsonl` com rotação aproximada de 2 MB e um backup `.1`.

A auditoria guarda somente horário UTC, fingerprint curta da chave, status, `cStat`, indicação de cache e duração.

## Segurança

- certificado e chave privada ficam somente no PC central;
- Host e Origin são validados;
- operações POST exigem CSRF;
- requisições possuem limite de tamanho;
- conexões remotas são rejeitadas quando a Central está parada;
- firewall automático é restrito a rede Privada, TCP `17345` e executável atual;
- a porta não deve ser publicada na internet;
- o aplicativo não possui autenticação própria: a segurança de acesso depende da rede interna e do estado ativa/parada da Central.

## Mapeamento Fernando Klein

O mapeamento interno é aplicado somente quando o CPF/CNPJ do **emitente** corresponde ao fornecedor configurado. O XML e o `cProd` fiscal original nunca são alterados. Descrições desconhecidas não recebem código inventado.

A regressão automatizada cobre os 17 produtos cadastrados, aliases, normalização, isolamento por emitente e um item desconhecido.

## Desenvolvimento e validação

```bash
dotnet restore Nfe-Agendamento.sln
dotnet test Nfe-Agendamento.sln -c Release
node tests/js/product-mapping-regression.test.js
node tests/js/lookup-feedback-regression.test.js
node tests/js/release-readiness-regression.test.js
dotnet build Nfe-Agendamento.sln -c Release
```

O CI também publica um pacote Windows autocontido de teste e disponibiliza o ZIP como artifact.

## Criar uma release

Existe um único fluxo oficial:

1. abra **Actions**;
2. escolha **Release Bridge**;
3. clique em **Run workflow**;
4. informe uma versão maior que a última publicada;
5. execute.

Antes de publicar, o workflow valida a versão, executa testes e todas as regressões, compila e gera o pacote Windows x64 autocontido.

## Checklist de aceitação da próxima release

- [ ] instalar/extrair a nova versão no PC central;
- [ ] painel mostra **Central ativa**;
- [ ] **Rede: OK**;
- [ ] **Servidor: OK**;
- [ ] **Firewall: OK**;
- [ ] acesso local em `http://127.0.0.1:17345` funciona;
- [ ] segundo PC abre o endereço `http://IP-DO-CENTRAL:17345`;
- [ ] certificado continua somente no PC central;
- [ ] consulta de uma NF-e conhecida funciona;
- [ ] download XML e DANFE funcionam no cliente.

Não provoque um `656` real apenas para testar o cooldown; a persistência e o bloqueio após reinício já possuem cobertura automatizada.

## Documentação técnica

- [Guia operacional da Central](docs/CENTRAL-LAN.md)
- [Arquitetura atual da Central LAN](docs/superpowers/specs/2026-09-01-central-lan-architecture-design.md)
- [Plano e estado de verificação](docs/superpowers/plans/2026-09-01-central-lan-architecture.md)
- [Design do navegador local](docs/superpowers/specs/2026-08-31-local-browser-nfe-design.md)
- [Design do DANFE](docs/superpowers/specs/2026-08-31-danfe-fsist-design.md)
