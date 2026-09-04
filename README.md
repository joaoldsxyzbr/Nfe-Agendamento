# NFe Agendamento

Aplicativo Windows interno para consultar, visualizar e baixar NF-e. Cada PC executa sua própria interface local e a coordenação multi-PC acontece por uma fila segura em pasta compartilhada.

## Versão

- última release publicada: **v0.1.30**;
- `main`: **v0.1.30**.

A v0.1.30 endurece o pareamento da arquitetura de grupo automático. O líder usa obrigatoriamente a identidade criptográfica compartilhada do grupo, o pareamento só retorna sucesso depois da importação segura do estado do grupo, estados locais incompletos são descartados, pedidos não são consumidos quando o líder perdeu o estado seguro e envios duplicados são serializados no backend e bloqueados na interface.

## Arquitetura atual

Cada PC executa sua própria cópia e abre:

```text
http://127.0.0.1:17345
```

Todos usam a pasta compartilhada:

```text
P:\01-Nfe agendamento
```

Os PCs confiáveis podem ser candidatos a líder desde que tenham acesso de leitura/gravação à pasta, estejam autorizados no grupo e possuam o certificado A1 aplicável instalado/configurado localmente.

```text
PCs autorizados
   ↓
eleição por central.lock
   ↓
1 líder ativo + demais em standby
   ↓
pedidos cifrados pela pasta compartilhada
   ↓
cache fiscal 24h → fila fiscal serial → revalidação da liderança → SEFAZ
   ↓
XML validado/cache cifrado
   ↓
resposta cifrada ao solicitante
```

Mesmo com A1 nos PCs confiáveis, somente o líder com lock exclusivo e saudável inicia trabalho fiscal automático.

## Liderança automática e identidade do grupo

O lock exclusivo fica em:

```text
P:\01-Nfe agendamento\status\central.lock
```

Somente um processo pode mantê-lo aberto com exclusividade. O líder publica heartbeat assinado e processa a fila; os demais permanecem em standby.

A chave RSA usada pelo líder não pertence ao PC que venceu a eleição. Ela vem de `group-identity.bin`, protegida pela chave de estado do grupo. Assim, troca de líder não altera a identidade pública confiada pelos clientes.

Antes de cada chamada fiscal, a autoridade do líder é revalidada no último boundary possível. Se a liderança foi perdida, a operação falha de forma segura e uma nova chamada fiscal não é iniciada automaticamente.

A configuração legada `ConfiguredAsCentral` existe apenas para compatibilidade/migração e não controla o dispatch normal.

## Pareamento robusto

O código temporário de autorização só pode ser gerado pelo líder atual.

Fluxo normal:

1. no líder, abra **Configurar**;
2. clique em **Gerar código de autorização**;
3. no novo PC, informe o código em **Autorizar este PC**;
4. o cliente publica um pedido cifrado na pasta compartilhada;
5. o líder valida o pedido usando o código ativo;
6. o líder registra o cliente e publica seu pacote de candidatura;
7. a resposta fixa a mesma identidade pública compartilhada do grupo;
8. o cliente importa e valida o estado seguro do grupo;
9. somente depois dessa validação a API responde sucesso;
10. o PC passa a operar como cliente e futuro candidato a líder.

Proteções da v0.1.30:

- o `CentralKeyStore` de produção usa `CandidateStateStore + SharedGroupIdentityStore`;
- um líder sem estado seguro do grupo não consome nem apaga o pedido de pareamento;
- falhas transitórias depois da leitura do pedido restauram o arquivo para nova tentativa;
- o cliente tenta recuperar um pareamento parcial anterior antes de iniciar outro;
- se a validação final do grupo falhar, o estado local parcial é apagado;
- solicitações simultâneas no mesmo PC são serializadas por um coordenador único;
- cliques ou `Enter` repetidos são bloqueados na interface;
- `clientPaired` só fica verdadeiro quando `CandidateStateStore` está realmente pronto;
- um `409` representa falha real de pareamento, não sucesso parcial mascarado.

O código é temporário e ligado ao líder que o gerou. Se ocorrer troca de líder durante a autorização, gere um novo código no líder atual.

## Migração da arquitetura anterior

Na primeira execução desta arquitetura:

1. atualize todos os PCs;
2. abra primeiro o PC que possuía o estado legado da Central;
3. mantenha `P:\01-Nfe agendamento` acessível;
4. identidade, autorização e replay são migrados para o estado do grupo;
5. clientes já pareados tentam importar seus pacotes de candidatura;
6. estados locais legados que não validarem são tratados como não autorizados;
7. depois disso qualquer PC autorizado e saudável pode assumir a liderança.

A migração é idempotente e preserva a identidade do grupo.

## Estrutura compartilhada

```text
P:\01-Nfe agendamento\
├── .nfe-agendamento
├── cache\
├── candidatos\
├── fila\
├── pareamento\
├── processando\
├── respostas\
└── status\
    ├── central.lock
    ├── heartbeat.json
    ├── group-identity.bin
    ├── authorized-clients.bin
    └── fiscal-cooldown.bin
```

Proteções principais:

- RSA OAEP-SHA256 para encapsulamento de chave;
- AES-GCM para dados compartilhados sensíveis;
- HMAC nos pedidos;
- DPAPI para material protegido localmente;
- heartbeat assinado;
- replay bloqueado após troca de líder;
- `cStat=656` persistido de forma compartilhada;
- cache XML compartilhado e cifrado com retenção de 24 horas;
- nomes de cache derivados de SHA-256, sem chave NF-e em texto puro no nome;
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

O cache tem retenção de 24 horas e sobrevive à troca de líder.

## Robustez fiscal e failover

A política é conservadora por projeto:

- HTTP `429` não é repetido automaticamente;
- timeout fiscal não é repetido automaticamente;
- `5xx`, falha de conexão ou `HttpRequestException` ambígua não geram retry automático;
- perda de liderança antes do envio aborta sem iniciar nova consulta;
- pedidos recuperados depois de interrupção não provocam uma segunda chamada fiscal automática;
- se o líder anterior pode já ter alcançado a SEFAZ, o sucessor devolve falha segura e exige nova ação explícita;
- `cStat=656` persiste entre líderes;
- cache fiscal sobrevive ao failover.

## Certificado A1

O certificado A1 é configurado localmente em cada PC confiável. Antes de considerar um PC candidato a líder, valide nele:

- certificado correto;
- UF autora configurada;
- acesso à pasta compartilhada;
- uma consulta conhecida.

O PFX, sua chave privada e eventual senha nunca devem entrar no repositório ou na pasta compartilhada.

## Contingência pelo Portal Nacional

Quando a consulta automática recebe `cStat=656`, o cooldown é mantido e o aplicativo não insiste automaticamente.

**Baixar pelo Portal** pode ser usado em qualquer PC autorizado no grupo que tenha o certificado A1 configurado localmente e o WebView2 disponível. O PC não precisa ser o líder da fila. O hCaptcha permanece manual e não é automatizado ou contornado.

Fluxo:

1. o site local abre o Portal em WebView2 no próprio PC;
2. a chave é preenchida automaticamente;
3. o usuário resolve o hCaptcha e conclui o download com o A1 local;
4. o XML é validado;
5. o XML válido entra no cache compartilhado;
6. a janela do Portal fecha após importação bem-sucedida;
7. a interface acompanha somente o cache;
8. a NF-e é carregada automaticamente quando o XML aparece.

O acompanhamento usa:

```text
GET /api/nfe/cache/{accessKey}
```

Esse polling não chama a SEFAZ.

Proteções do Portal:

- somente PCs autorizados com estado de grupo disponível podem iniciar o fallback;
- navegação restrita ao domínio oficial esperado;
- certificado comparado por thumbprint;
- XML limitado a 10 MiB;
- DTD e entidades externas proibidos;
- `infNFe/@Id` deve corresponder à chave solicitada;
- XML de outra chave é rejeitado;
- apenas uma janela de contingência pode ficar aberta por vez em cada PC.

## Consulta em lote

O lote reutiliza o mesmo endpoint e a mesma fila fiscal da consulta individual.

- até 50 chaves únicas;
- duplicatas removidas;
- uma consulta por vez por instalação;
- líder serializa a parte fiscal;
- cache, deduplicação e cooldown são compartilhados;
- `cStat=656` interrompe o restante do lote;
- cancelar impede o início dos próximos itens.

## DANFE

O DANFE é produzido localmente a partir do XML validado.

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

## Segurança de rede

- HTTP somente em loopback;
- Host e Origin validados;
- operações mutáveis protegidas por CSRF;
- nenhuma porta LAN adicional;
- nenhuma regra de firewall criada;
- nenhum mDNS necessário.

## Atualização

Na bandeja use **Verificar atualização**.

O atualizador exige:

- origem oficial por HTTPS;
- tamanho esperado;
- SHA-256 válido;
- bundle Sigstore válido;
- certificado Sigstore emitido pelo OIDC do GitHub Actions;
- identidade exatamente vinculada ao workflow oficial `release-bridge.yml@refs/heads/main`;
- transparency log e verificações exigidas pela biblioteca Sigstore.

Depois do download, a nova versão é preparada antes da troca. O instalador preserva backup da instalação atual, ativa a nova versão e verifica `http://127.0.0.1:17345/api/bootstrap` por até 20 segundos. Falha no health check encerra a nova versão, restaura o backup e reinicia a anterior.

Não existe chave privada permanente de assinatura de release.

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

A v0.1.30 adiciona regressões específicas para:

- composição real do `CentralKeyStore` com identidade compartilhada;
- round-trip de pareamento e importação da mesma identidade de grupo;
- líder sem estado seguro não consumir pedido;
- descarte de estado local quando a importação final falha;
- colapso de solicitações locais duplicadas em uma única autorização;
- composição do endpoint com `SharedQueuePairingCoordinator`;
- bloqueio de duplicidade no JavaScript.

## GitHub Actions

O projeto mantém somente três workflows operacionais:

```text
.github/workflows/ci.yml
.github/workflows/codeql.yml
.github/workflows/release-bridge.yml
```

### CI

Executa em push para `main` e em pull request. Possui permissão somente de leitura do conteúdo, timeout explícito e artifact de build com retenção curta.

### CodeQL

Analisa C# em push/PR para `main` e também semanalmente.

### Release Bridge

É o único caminho oficial de publicação.

Futuras releases são solicitadas alterando:

```text
.github/release-request.json
```

Exemplo:

```json
{
  "version": "0.1.30"
}
```

Para uma release automática:

1. `<Version>` em `src/NfeAgendamento.App/NfeAgendamento.App.csproj` deve conter a nova versão;
2. `.github/release-request.json` deve conter exatamente a mesma versão;
3. a alteração do request na `main` dispara o Release Bridge;
4. o workflow valida formato, versão do projeto, tags existentes e monotonicidade;
5. executa `scripts/verify.ps1 -Restore`;
6. publica o Windows x64;
7. assina com Sigstore keyless;
8. verifica a assinatura antes da publicação;
9. cria tag/release apontando exatamente para o SHA testado;
10. gera release notes a partir das mudanças reais.

`workflow_dispatch` continua disponível como fallback operacional e passa pelos mesmos gates.

## Checklist automatizado

Antes de uma release oficial, os gates cobrem:

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

## Teste físico ainda necessário

Antes de considerar uma implantação operacional totalmente aceita:

- [ ] autorizar um PC novo usando código gerado no líder atual;
- [ ] repetir o teste em um líder diferente do PC que criou originalmente o grupo;
- [ ] confirmar que um segundo clique/Enter não cria autorização duplicada;
- [ ] pelo menos dois PCs reais disputam a liderança e somente um vence;
- [ ] ao fechar o líder, outro assume automaticamente;
- [ ] consulta funciona pelo standby antes e depois do failover;
- [ ] consultar uma NF-e, trocar o líder e confirmar retorno do cache sem nova ida à SEFAZ;
- [ ] perder acesso ao compartilhamento no líder e confirmar que nenhuma nova chamada fiscal começa;
- [ ] restaurar o compartilhamento e validar recuperação;
- [ ] A1 local funciona nos candidatos;
- [ ] Portal Nacional abre em WebView2 real em um PC autorizado mesmo quando ele está em standby;
- [ ] hCaptcha permanece manual e o A1 local funciona no fluxo real;
- [ ] XML oficial chega ao cache compartilhado e a interface carrega a NF-e automaticamente;
- [ ] atualização real conclui o health check;
- [ ] cenário de health check inválido restaura a versão anterior.

Não provoque `cStat=656` real apenas para testar.

## Release atual

A última release publicada é **v0.1.30**.

Ela corrige a causa estrutural do `409` observado no pareamento: líderes automáticos agora usam a identidade criptográfica compartilhada do grupo. O fluxo também ficou transacional do ponto de vista local, recuperável diante de falhas transitórias e protegido contra solicitações duplicadas.

## Documentação técnica

- [Guia operacional da fila](docs/CENTRAL-LAN.md)
- [Inicialização e atualização](docs/ATUALIZACAO-E-INICIALIZACAO.md)
- [Teste multi-PC](docs/TESTE-MULTI-PC.md)
- [Hardening pós-auditoria — design](docs/superpowers/specs/2026-09-03-audit-hardening-design.md)
- [Liderança automática — design](docs/superpowers/specs/2026-09-03-automatic-shared-queue-leader-design.md)
- [Contingência pelo Portal — design](docs/superpowers/specs/2026-09-03-portal-nfe-fallback-design.md)
- [Sigstore keyless — design](docs/superpowers/specs/2026-09-04-keyless-release-signing-design.md)
- [Simplificação operacional do GitHub — design](docs/superpowers/specs/2026-09-04-github-operations-simplification-design.md)
- [Simplificação operacional do GitHub — plano](docs/superpowers/plans/2026-09-04-github-operations-simplification.md)
