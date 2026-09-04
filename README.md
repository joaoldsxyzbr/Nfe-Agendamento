# NFe Agendamento

Aplicativo Windows interno para consultar, visualizar e baixar NF-e. Cada PC executa sua própria interface local e a coordenação multi-PC acontece por uma fila segura em pasta compartilhada.

## Versão

- última release publicada: **v0.1.31**;
- `main`: **v0.1.31**.

A v0.1.31 conclui o hardening pós-auditoria iniciado sobre a v0.1.30: bootstrap recuperável, pareamento one-shot, revogação com rotação criptográfica recuperável, cadeia RSA assinada para candidatos offline, gerenciamento de PCs autorizados, Actions fixadas por SHA, health check de atualização vinculado à versão realmente iniciada e tratamento estreito do ciclo de vida do WebView2.

O checklist técnico está em [Hardening pós-auditoria — plano](docs/superpowers/plans/2026-09-04-post-audit-hardening.md). A validação que depende de máquinas reais permanece separada em [Teste multi-PC](docs/TESTE-MULTI-PC.md).

## Arquitetura atual

Cada PC executa sua própria cópia e abre somente:

```text
http://127.0.0.1:17345
```

Todos usam a pasta compartilhada:

```text
P:\01-Nfe agendamento
```

Não existe servidor HTTP exposto na LAN, mDNS nem regra automática de firewall. A comunicação entre PCs acontece pela pasta compartilhada.

Os PCs confiáveis podem ser candidatos a líder quando:

- possuem acesso de leitura/gravação à pasta compartilhada;
- estão autorizados no grupo;
- possuem o certificado A1 aplicável instalado e configurado localmente.

A pasta deve usar SMB normal, preservando locks exclusivos. **Não use Offline Files/Arquivos Offline ou cache desconectado** para a pasta da fila.

```text
PCs autorizados
   ↓
eleição por central.lock
   ↓
1 líder ativo + demais em standby
   ↓
pedidos cifrados pela pasta compartilhada
   ↓
cache fiscal 24h → fila fiscal serial → fencing → SEFAZ
   ↓
XML validado/cache cifrado
   ↓
resposta cifrada ao solicitante
```

Mesmo com A1 em vários PCs, somente o líder com lock exclusivo e saudável inicia trabalho fiscal automático.

## Liderança automática

O lock exclusivo fica em:

```text
P:\01-Nfe agendamento\status\central.lock
```

Somente um processo pode mantê-lo aberto com exclusividade. O líder publica heartbeat assinado e processa a fila; os demais permanecem em standby.

A identidade RSA usada pelo líder vem de `group-identity.bin`, protegida pela chave de estado do grupo. Trocar de líder normalmente não muda a identidade pública confiada pelos clientes.

Antes de cada chamada fiscal, a autoridade do líder é revalidada no último boundary possível. Se a liderança foi perdida, a operação falha fechado e uma nova chamada fiscal não é iniciada automaticamente.

Se existir `status\rotation.json`, nenhum candidato inicia novo trabalho fiscal até concluir a recuperação da rotação pendente.

A configuração legada `ConfiguredAsCentral` existe apenas para compatibilidade/migração e não controla a operação normal.

## Pareamento robusto

O código temporário de autorização só pode ser gerado pelo líder atual.

Fluxo:

1. no líder, abra **Configurar**;
2. clique em **Gerar código de autorização**;
3. no novo PC, informe o código em **Autorizar este PC**;
4. o cliente publica um pedido cifrado na pasta compartilhada;
5. o líder valida o código e registra o cliente;
6. publica o pacote de candidatura;
7. o cliente importa e valida o estado seguro do grupo;
8. somente depois disso a API responde sucesso.

Proteções:

- o líder usa obrigatoriamente a identidade criptográfica compartilhada do grupo;
- estados locais incompletos são recuperados ou descartados com segurança;
- solicitações simultâneas no mesmo PC são serializadas;
- clique/`Enter` duplicado é bloqueado também na interface;
- o código é consumido somente após a autorização concluída;
- o mesmo código não autoriza um segundo PC;
- troca legítima de identidade só é aceita por cadeia RSA assinada a partir do pin já confiado.

Se ocorrer troca de líder durante o fluxo, gere um novo código no líder atual.

## Revogação e rotação de confiança

O líder atual pode listar PCs autorizados e revogar um PC pela aba **Configurar**. A listagem não expõe o segredo criptográfico dos clientes e o líder atual não pode se autorrevogar pela interface.

A revogação executa uma rotação real de confiança:

1. nova chave de estado do grupo;
2. nova identidade RSA;
3. nova lista de autorizados sem o PC removido;
4. cooldown fiscal preservado;
5. novos bundles somente para os PCs restantes;
6. cadeia de transições RSA assinada para candidatos offline;
7. purge do cache cifrado com a chave antiga;
8. promoção recuperável do novo estado.

Um candidato offline pode validar transições A→B→C desde que a cadeia seja criptograficamente ligada ao pin anterior. Uma identidade arbitrária sem essa prova é rejeitada.

Se houver queda durante a promoção, `rotation.json` e os artefatos preparados permitem ao próximo candidato autorizado concluir a operação antes de qualquer trabalho fiscal.

## Bootstrap e migração

O bootstrap é recuperável. A chave local protegida por DPAPI é persistida antes da identidade compartilhada. Uma interrupção entre essas etapas reutiliza a chave preparada na próxima inicialização, evitando criar estado cifrado com uma chave perdida.

A migração do estado legado não depende mais de reflection sobre campos privados.

Na migração da arquitetura anterior:

1. atualize todos os PCs;
2. abra primeiro o PC que possuía o estado legado da Central;
3. mantenha `P:\01-Nfe agendamento` acessível;
4. deixe identidade, autorização e replay serem migrados;
5. abra os demais PCs já autorizados;
6. confirme exatamente um líder e os demais em standby.

## Estrutura compartilhada

```text
P:\01-Nfe agendamento\
├── .nfe-agendamento
├── cache\
├── candidatos\
│   ├── <clientId>.candidate
│   └── <clientId>.transitions
├── fila\
├── pareamento\
├── processando\
├── respostas\
└── status\
    ├── central.lock
    ├── heartbeat.json
    ├── group-identity.bin
    ├── authorized-clients.bin
    ├── fiscal-cooldown.bin
    └── rotation.json
```

Durante uma rotação podem existir artefatos `.prepared`. Não os apague manualmente enquanto `rotation.json` existir.

Proteções principais:

- RSA OAEP-SHA256 para encapsulamento de chave;
- AES-GCM para dados compartilhados sensíveis;
- HMAC nos pedidos;
- DPAPI para material local;
- RSA-PSS/SHA-256 em heartbeat e transições de identidade;
- replay bloqueado após troca de líder;
- cooldown fiscal compartilhado;
- cache XML compartilhado e cifrado com retenção de 24 horas;
- nomes de cache derivados de SHA-256;
- confinamento de caminhos e rejeição de reparse points operacionais;
- certificado A1, chave privada e senha nunca são copiados para a pasta compartilhada.

## Consulta e cache

A consulta individual usa:

```text
POST /api/nfe/lookup
```

Fluxo:

1. validar a chave de 44 dígitos;
2. consultar o cache compartilhado;
3. deduplicar a mesma chave;
4. entrar na fila fiscal serializada;
5. respeitar o cooldown compartilhado;
6. revalidar a liderança imediatamente antes da chamada externa;
7. consultar a distribuição de DF-e;
8. validar o XML retornado;
9. gravar o cache cifrado;
10. devolver o resultado ao solicitante.

O cache tem retenção de 24 horas e sobrevive à troca normal de líder. Após revogação/rotação da chave do grupo, o cache antigo é purgado deliberadamente.

## Robustez fiscal e failover

A política é conservadora:

- HTTP `429` não recebe retry automático;
- timeout fiscal não recebe retry automático;
- `5xx`, falha de conexão ou `HttpRequestException` ambígua não geram retry automático;
- perda de liderança antes do envio aborta sem iniciar nova consulta;
- pedido recuperado após interrupção não provoca uma segunda chamada fiscal automática;
- se o líder anterior pode já ter alcançado a SEFAZ, o sucessor devolve falha segura e exige nova ação explícita;
- `cStat=656` persiste entre líderes e durante rotação;
- cache fiscal sobrevive ao failover normal.

Cancelar a interface ou um lote impede trabalho ainda não iniciado e os próximos itens. Uma operação fiscal que já pode ter alcançado a SEFAZ não é forçada a cancelar e repetir automaticamente.

## Certificado A1

O A1 é configurado localmente em cada PC confiável. Antes de considerar um PC candidato a líder, valide nele:

- certificado correto no `CurrentUser\My`;
- chave privada acessível ao usuário do app;
- UF autora configurada;
- acesso à pasta compartilhada;
- uma consulta conhecida.

PFX, chave privada e senha nunca devem entrar no repositório ou na pasta compartilhada.

## Contingência pelo Portal Nacional

Quando a consulta automática recebe `cStat=656`, o aplicativo mantém o cooldown e não insiste automaticamente.

**Baixar pelo Portal** pode ser usado em qualquer PC autorizado com A1 local e WebView2 disponível, inclusive em standby. O hCaptcha permanece manual e não é automatizado nem contornado.

Fluxo:

1. o site local abre o Portal oficial em WebView2;
2. a chave é preenchida automaticamente;
3. o usuário resolve o hCaptcha;
4. o certificado A1 local é usado quando solicitado pelo Portal;
5. o XML baixado é validado contra a chave solicitada;
6. o XML válido entra no cache compartilhado;
7. a interface acompanha apenas o cache;
8. a NF-e é carregada automaticamente quando o XML aparece.

O acompanhamento usa:

```text
GET /api/nfe/cache/{accessKey}
```

Esse polling não chama a SEFAZ.

Proteções do Portal:

- somente PC autorizado com estado real do grupo pode iniciar o fallback;
- navegação restrita ao host oficial esperado;
- certificado comparado por thumbprint;
- XML limitado a 10 MiB;
- DTD e entidades externas proibidos;
- `infNFe/@Id` deve corresponder à chave solicitada;
- XML de outra chave é rejeitado;
- somente uma janela de contingência pode ficar aberta por PC;
- callbacks tardios durante fechamento do WebView2 são tratados apenas para falhas conhecidas de ciclo de vida;
- falha COM genérica, erro de XML, certificado ou I/O não é silenciosamente ocultado.

## Consulta em lote

O lote reutiliza o mesmo endpoint e a mesma fila fiscal da consulta individual:

- até 50 chaves únicas;
- duplicatas removidas;
- uma consulta por vez por instalação;
- líder serializa a parte fiscal;
- cache, deduplicação e cooldown são compartilhados;
- `cStat=656` interrompe o restante do lote;
- cancelar impede o início dos próximos itens.

## DANFE

O DANFE é produzido localmente a partir do XML validado:

- visualização em popup próprio;
- `Ctrl + scroll` aplica zoom somente ao DANFE;
- impressão/salvar PDF usa o navegador local;
- XML fiscal original não é modificado.

## Mapeamento Fernando Klein

O mapeamento interno altera somente a apresentação interna de código/descrição quando aplicável. O XML e o `cProd` fiscal original permanecem intactos.

## Dados locais

Dados locais ficam em:

```text
%LOCALAPPDATA%\NfeAgendamento
```

Podem incluir auditoria, seleção de certificado, pareamento, chave de candidato protegida por DPAPI, solicitações pendentes, perfil WebView2 e dados de migração.

O cache fiscal operacional fica na pasta compartilhada e cifrado com a chave do grupo.

## Segurança de rede e modelo de ameaça local

- HTTP somente em loopback;
- Host e Origin validados;
- operações mutáveis protegidas por CSRF;
- nenhuma porta LAN adicional;
- nenhuma regra de firewall criada;
- nenhum mDNS necessário.

Loopback protege contra exposição direta à LAN, mas não isola processos ou usuários do mesmo Windows. O projeto assume PCs corporativos confiáveis; malware ou outro processo local malicioso deve ser tratado como comprometimento local.

## Atualização

Na bandeja use **Verificar atualização**.

O atualizador exige:

- origem oficial por HTTPS;
- tamanho esperado;
- SHA-256 válido;
- bundle Sigstore válido;
- certificado Sigstore emitido pelo OIDC do GitHub Actions;
- identidade exatamente vinculada ao workflow oficial `release-bridge.yml@refs/heads/main`;
- transparency log e verificações da biblioteca Sigstore.

A partir da v0.1.31, o health check é vinculado à versão preparada. Depois do swap, o instalador exige simultaneamente:

- HTTP 2xx em `http://127.0.0.1:17345/api/bootstrap`;
- JSON válido com objeto na raiz;
- `appVersion` como string escalar;
- igualdade ordinal exata entre `appVersion` e a versão preparada.

Resposta malformada, campo ausente, tipo incorreto, outra versão respondendo na porta ou ausência de resposta em até 20 segundos provocam rollback para a instalação anterior.

Não existe chave privada permanente de assinatura de release. O fluxo oficial usa **Sigstore keyless**.

Veja [Inicialização e atualização](docs/ATUALIZACAO-E-INICIALIZACAO.md).

## Desenvolvimento e validação

Requer SDK **.NET 10**.

A fonte única dos gates comuns é:

```powershell
./scripts/verify.ps1 -Restore
```

O script executa:

- restore quando solicitado;
- auditoria NuGet, incluindo dependências transitivas;
- testes .NET em Release;
- regressões JavaScript;
- build Release.

A cobertura automatizada inclui, entre outros:

- eleição de um único líder e takeover;
- fencing imediatamente antes da SEFAZ;
- tratamento conservador de resultado fiscal ambíguo;
- cache compartilhado;
- bootstrap recuperável;
- migração sem reflection;
- pareamento one-shot;
- staging e promoção recuperável da rotação;
- revogação e cadeia RSA assinada para candidatos offline;
- bloqueio de trabalho fiscal durante rotação pendente;
- gerenciamento de PCs sem exposição de segredo;
- health check de atualização com versão exata;
- Actions fixadas por SHA;
- tratamento estreito do ciclo de vida do WebView2.

## GitHub Actions

O projeto mantém três workflows operacionais:

```text
.github/workflows/ci.yml
.github/workflows/codeql.yml
.github/workflows/release-bridge.yml
```

Todas as Actions externas usadas por esses workflows estão fixadas por commit SHA; comentários mantêm a versão humana legível. O Dependabot monitora NuGet e GitHub Actions.

### CI

Executa em push para `main` e em pull request. Usa `./scripts/verify.ps1 -Restore`, publica um pacote de teste Windows e mantém retenção curta do artifact.

### CodeQL

Analisa C# em push/PR para `main` e semanalmente.

### Release Bridge

É o único caminho oficial de publicação. A solicitação fica em:

```text
.github/release-request.json
```

Versão atual:

```json
{
  "version": "0.1.31"
}
```

O Release Bridge:

1. exige que `<Version>` do projeto e request sejam iguais;
2. rejeita tag existente e versão não crescente;
3. executa `./scripts/verify.ps1 -Restore`;
4. publica Windows x64 autocontido;
5. assina o ZIP com Sigstore keyless;
6. verifica a assinatura antes de publicar;
7. cria a tag/release apontando exatamente para o SHA testado;
8. gera release notes das mudanças reais.

`workflow_dispatch` permanece disponível como fallback e passa pelos mesmos gates.

## Checklist automatizado

Antes da release oficial, os gates cobrem:

- testes .NET;
- regressões JS de produto, feedback fiscal, Portal, bootstrap, lote e release;
- auditoria de dependências NuGet transitivas;
- build Release;
- publish Windows x64 autocontido;
- versão coerente entre request, projeto e README;
- tag nova e semanticamente superior;
- vínculo ao SHA imutável;
- assinatura e verificação Sigstore;
- ausência de credenciais fiscais reais nos workflows;
- ausência de certificado A1 empacotado no repositório.

## Validação física ainda necessária

A implementação automatizada está fechada, mas a aceitação operacional completa exige máquinas reais. O roteiro está em [docs/TESTE-MULTI-PC.md](docs/TESTE-MULTI-PC.md) e cobre:

- eleição simultânea e failover em 2–3 PCs;
- consulta pelo standby e deduplicação entre máquinas;
- perda e recuperação do SMB sem Offline Files;
- A1 real em cada candidato;
- pareamento one-shot e revogação real;
- candidato offline durante rotação;
- recuperação de rotação interrompida;
- Portal/WebView2/A1/hCaptcha reais;
- atualização real, health check e rollback.

Esses itens permanecem marcados como manuais porque não podem ser comprovados honestamente por CI. Não provoque `cStat=656` real apenas para teste.

## Release atual

A release **v0.1.31** reúne o hardening pós-auditoria e mantém a arquitetura simples: interface local em loopback, coordenação pela pasta compartilhada e exatamente um líder fiscal por vez.

## Documentação técnica

- [Guia operacional da fila](docs/CENTRAL-LAN.md)
- [Inicialização e atualização](docs/ATUALIZACAO-E-INICIALIZACAO.md)
- [Teste multi-PC](docs/TESTE-MULTI-PC.md)
- [Hardening pós-auditoria v0.1.30 — design](docs/superpowers/specs/2026-09-04-post-audit-hardening-design.md)
- [Hardening pós-auditoria v0.1.30 — plano](docs/superpowers/plans/2026-09-04-post-audit-hardening.md)
- [Liderança automática — design](docs/superpowers/specs/2026-09-03-automatic-shared-queue-leader-design.md)
- [Contingência pelo Portal — design](docs/superpowers/specs/2026-09-03-portal-nfe-fallback-design.md)
- [Sigstore keyless — design](docs/superpowers/specs/2026-09-04-keyless-release-signing-design.md)
- [Simplificação operacional do GitHub — design](docs/superpowers/specs/2026-09-04-github-operations-simplification-design.md)
- [Simplificação operacional do GitHub — plano](docs/superpowers/plans/2026-09-04-github-operations-simplification.md)
