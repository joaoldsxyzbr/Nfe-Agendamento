# NFe Agendamento

Aplicativo Windows interno para consultar, visualizar e baixar NF-e. Cada PC usa a interface local e a coordenação multi-PC acontece por uma fila segura na pasta compartilhada da empresa.

## Versão

- última release publicada: **v0.1.25**;
- `main`: preparada para a **v0.1.26**.

A versão v0.1.25 acrescenta **fencing fiscal imediatamente antes do envio à SEFAZ**, atualização com **backup, health check local e rollback automático**, migração para **.NET 10 LTS** e novos gates de segurança no CI/release. A assinatura independente de pacotes continua condicionada ao provisionamento de uma chave privada em GitHub Secret; nenhuma chave privada é versionada no repositório.

A `main` preparada para v0.1.26 inclui o fallback do Portal acionado pelo site local, carregamento automático do XML pelo cache e atualização assinada com Sigstore keyless vinculada ao workflow oficial do GitHub Actions.

## Arquitetura atual

Cada PC executa sua própria cópia e abre:

```text
http://127.0.0.1:17345
```

Todos usam:

```text
P:\01-Nfe agendamento
```

Todos os PCs confiáveis podem ser candidatos a líder, desde que tenham acesso à pasta, estejam autorizados no grupo e possuam o A1 aplicável instalado/configurado localmente.

```text
PCs autorizados
   ↓
eleição por central.lock
   ↓
1 líder ativo + demais em standby
   ↓
pedidos cifrados pela pasta compartilhada
   ↓
cache fiscal compartilhado 24h → fila fiscal serial → revalidação final da liderança → SEFAZ
   ↓
XML validado/cache cifrado
   ↓
resposta cifrada ao solicitante
```

Mesmo com A1 em todos os PCs, apenas o líder com lock exclusivo e saudável inicia trabalho fiscal.

## Liderança automática e fencing fiscal

O lock exclusivo fica em:

```text
P:\01-Nfe agendamento\status\central.lock
```

Somente um processo pode mantê-lo aberto com exclusividade. O líder publica heartbeat assinado e processa a fila; os demais ficam em standby.

Se o líder sair, outro candidato tenta assumir automaticamente. Além das verificações durante o processamento, a v0.1.25 revalida a autoridade do líder **no último boundary antes de iniciar a chamada fiscal**. Se a liderança for perdida, a operação falha de forma segura e não é repetida automaticamente.

A configuração legada `ConfiguredAsCentral` existe somente para a migração inicial e não decide mais o dispatch normal.

## Migração da Central antiga

Na primeira execução desta arquitetura:

1. atualize o aplicativo;
2. abra primeiro o PC que era a Central antiga;
3. mantenha `P:\01-Nfe agendamento` acessível;
4. ele migra a identidade RSA já pareada, autorização e replay para o estado do grupo;
5. os clientes já pareados importam automaticamente seus pacotes de candidatura;
6. o antigo Central também ganha identidade de cliente;
7. depois disso qualquer PC autorizado pode assumir a fila.

A migração é idempotente e preserva a chave pública conhecida pelos clientes, evitando reapareamento geral.

## Estrutura e segurança do grupo

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

- identidade RSA compartilhada cifrada por AES-GCM;
- chave de estado do candidato protegida localmente por DPAPI;
- pacote de candidatura individual protegido pelo segredo do cliente;
- clientes autorizados e `LastSequence` compartilhados de forma cifrada;
- replay continua bloqueado após troca de líder;
- cooldown de `cStat=656` é compartilhado e cifrado;
- cache XML de 24h é compartilhado e cifrado com a chave do grupo;
- arquivos do cache usam nome derivado de SHA-256 da chave, sem XML ou chave NF-e em texto puro;
- caminhos ficam confinados à árvore dedicada e reparse points operacionais são rejeitados;
- heartbeat assinado, HMAC nos pedidos, RSA OAEP-SHA256 para a chave AES e AES-GCM nos dados compartilhados.

O certificado A1, sua chave privada e eventual senha **não são copiados para a pasta compartilhada**.

## Consulta e cache

A consulta individual usa `POST /api/nfe/lookup`.

Quando este PC é líder com lock saudável, executa o fluxo fiscal local. Caso contrário, envia o pedido pela fila para o líder atual.

O botão **Consultar NF-e** permanece acionável mesmo durante estados transitórios de bootstrap. Se o PC realmente ainda não estiver autorizado ou a fila estiver indisponível, o backend devolve a condição correspondente e a interface exibe a mensagem ao usuário.

Ordem fiscal:

1. validar a chave de 44 dígitos;
2. consultar o cache XML compartilhado e cifrado;
3. deduplicar a mesma chave;
4. entrar na fila fiscal serializada;
5. respeitar o cooldown compartilhado;
6. revalidar a liderança imediatamente antes da chamada externa;
7. consultar `NFeDistribuicaoDFe/consChNFe`;
8. validar o XML;
9. gravar o cache compartilhado;
10. devolver o resultado ao solicitante.

O cache possui retenção de **24 horas** e é legível por qualquer líder autorizado. Uma troca de líder não perde o conhecimento de XMLs já obtidos e evita nova consulta desnecessária à SEFAZ.

## Robustez fiscal e failover

A política é deliberadamente conservadora:

- HTTP `429` não é repetido automaticamente;
- timeout fiscal não é repetido automaticamente;
- `5xx`, falha de conexão e `HttpRequestException` ambígua não são repetidos automaticamente;
- perda de liderança antes do envio aborta a operação sem iniciar nova consulta fiscal;
- pedidos recuperados depois de interrupção não provocam segunda chamada fiscal;
- se o antigo líder pode já ter alcançado a SEFAZ, o sucessor devolve falha segura e exige nova ação explícita;
- `cStat=656` persiste em estado compartilhado, portanto mudar o líder não fura o cooldown;
- o cache fiscal também sobrevive ao failover.

## Certificado A1

O A1 é configurado **localmente em cada PC confiável**. Antes de contar com uma máquina como candidato, valide nela o certificado, a UF autora e uma consulta conhecida.

## Contingência pelo Portal Nacional

Quando a consulta automática recebe `cStat=656`, o aplicativo mantém o cooldown e não insiste automaticamente.

**Baixar pelo Portal** é oferecido somente no **líder atual com lock saudável**. O hCaptcha permanece manual e não é automatizado nem contornado.

Fluxo da interface:

1. o site local abre o Portal no WebView2 seguro;
2. a chave é preenchida automaticamente;
3. o usuário resolve o hCaptcha e conclui o download;
4. o XML é validado e salvo no cache compartilhado;
5. a janela do Portal fecha sem exigir confirmação adicional;
6. o site acompanha apenas o cache por `GET /api/nfe/cache/{accessKey}`;
7. quando o XML aparece, a NF-e é carregada automaticamente pela interface.

O acompanhamento do cache não chama a SEFAZ e não cria retry fiscal durante o cooldown.

Proteções do Portal:

- navegação fiscal limitada ao domínio oficial esperado;
- certificado comparado por thumbprint;
- XML limitado a 10 MiB;
- DTD/entidades externas proibidos;
- `infNFe/@Id` deve corresponder à chave consultada;
- XML de outra chave é rejeitado;
- somente uma janela de contingência fica aberta por vez no líder.

A integração real WebView2 + Portal + hCaptcha + seleção do A1 continua exigindo teste físico.

## Autorizar outro PC

O código temporário só pode ser gerado pelo líder atual.

1. no líder, abra **Configurar**;
2. clique em **Gerar código de autorização**;
3. no PC novo, informe o código em **Autorizar este PC**;
4. o líder registra o cliente e publica seu pacote de candidatura;
5. o novo PC passa a funcionar como cliente e futuro candidato a líder.

## Consulta em lote

O lote reutiliza o mesmo `POST /api/nfe/lookup` e não cria paralelismo fiscal adicional.

- até 50 chaves únicas;
- duplicatas removidas;
- uma consulta por vez por instalação;
- líder serializa o fluxo fiscal;
- cache compartilhado, deduplicação e cooldown são os mesmos da consulta individual;
- `cStat=656` interrompe o restante do lote;
- cancelar impede o início dos próximos itens.

## DANFE

O DANFE é montado localmente a partir do XML validado.

- visualização em popup próprio;
- `Ctrl + scroll` aplica zoom somente ao DANFE;
- impressão/salvar PDF usa o navegador local;
- XML fiscal original não é modificado.

## Mapeamento Fernando Klein

O mapeamento interno altera somente a apresentação interna de código/descrição quando aplicável. O XML e o `cProd` fiscal original permanecem intactos.

## Dados locais

Os dados locais ficam em:

```text
%LOCALAPPDATA%\NfeAgendamento
```

Podem incluir auditoria, seleção de certificado, pareamento, chave de candidato protegida por DPAPI, solicitações pendentes, perfil WebView2 e dados legados necessários à migração. O cache fiscal operacional fica na pasta compartilhada, cifrado com a chave do grupo.

## Segurança de rede

- HTTP somente em loopback;
- Host/Origin validados;
- operações mutáveis protegidas por CSRF;
- nenhuma porta LAN adicional;
- nenhuma regra de firewall criada;
- nenhum mDNS necessário.

## Atualização

Na bandeja use **Verificar atualização**. O atualizador exige pacote oficial por HTTPS, tamanho esperado e digest SHA-256 válido.

Na v0.1.25 a aplicação preparada é validada antes da troca. Depois que o processo atual encerra, o instalador move a instalação existente para backup, ativa a nova versão e verifica `http://127.0.0.1:17345/api/bootstrap` por até **20 segundos**. Se a nova versão não ficar saudável, ela é encerrada, o backup é restaurado e a versão anterior é reiniciada.

A assinatura criptográfica independente prevista no hardening não é anunciada como concluída: ela depende de uma chave privada externa armazenada em GitHub Secret e essa chave nunca deve entrar no repositório.

Veja [Inicialização e atualização](docs/ATUALIZACAO-E-INICIALIZACAO.md).

## Desenvolvimento e validação

Requer SDK **.NET 10**.

```bash
dotnet restore Nfe-Agendamento.sln
dotnet list Nfe-Agendamento.sln package --vulnerable --include-transitive --format json
dotnet test Nfe-Agendamento.sln -c Release
node tests/js/product-mapping-regression.test.js
node tests/js/lookup-feedback-regression.test.js
node tests/js/portal-fallback-regression.test.js
node tests/js/pairing-lookup-regression.test.js
node tests/js/batch-lookup-regression.test.js
node tests/js/release-readiness-regression.test.js
dotnet build Nfe-Agendamento.sln -c Release
```

O CI também publica Windows x64 autocontido, compacta o ZIP e disponibiliza artifact.

## Checklist da v0.1.25

Automatizado validado para a release:

- testes .NET completos;
- fencing de liderança antes do envio fiscal;
- ausência de retry fiscal automático após perda de liderança ou falha ambígua;
- eleição de exatamente um líder e takeover seguro;
- replay e cooldown persistentes entre líderes;
- cache XML compartilhado legível após troca de líder;
- atualizador com staging, backup, health check de 20 s e rollback;
- Portal restrito ao líder ativo;
- regressões JS de produto, feedback fiscal, Portal, botão de consulta, lote e release;
- auditoria de dependências NuGet;
- build Release;
- publish Windows x64 autocontido;
- ZIP da release.

Mudanças posteriores presentes na `main`:

- fallback do Portal acionado e acompanhado pela interface web;
- leitura exclusiva do cache durante a espera, sem polling fiscal;
- carregamento automático da NF-e após o XML oficial entrar no cache;
- fechamento automático da janela do Portal após importação bem-sucedida.

Teste físico ainda necessário para aceitação operacional:

- [ ] pelo menos dois PCs reais disputam a liderança e somente um vence;
- [ ] ao fechar o líder, outro assume automaticamente;
- [ ] consulta funciona pelo standby antes e depois do failover;
- [ ] consultar uma NF-e, trocar o líder e confirmar retorno do cache sem nova ida à SEFAZ;
- [ ] perder acesso ao compartilhamento no líder e confirmar que nenhuma nova chamada fiscal começa;
- [ ] restaurar o compartilhamento e validar recuperação;
- [ ] A1 local funciona nos candidatos;
- [ ] Portal Nacional aparece somente no líder e abre no WebView2 real;
- [ ] hCaptcha permanece manual e o A1 funciona no fluxo real;
- [ ] XML oficial chega ao cache compartilhado e a interface carrega a NF-e automaticamente;
- [ ] atualização real conclui o health check;
- [ ] cenário de health check inválido restaura a versão anterior.

Não provoque `cStat=656` real apenas para testar.

## Release

A última release publicada é **v0.1.25**. A `main` está preparada para a **v0.1.26**; a publicação é feita exclusivamente pelo Release Bridge após todos os gates.

A publicação oficial usa o workflow **Release Bridge**, que restaura dependências, audita vulnerabilidades, testa, compila, publica o pacote Windows e prende a tag ao SHA efetivamente validado.

## Documentação técnica

- [Hardening pós-auditoria — design](docs/superpowers/specs/2026-09-03-audit-hardening-design.md)
- [Hardening v0.1.25 — plano](docs/superpowers/plans/2026-09-03-audit-hardening-v0.1.25.md)
- [Liderança automática — design](docs/superpowers/specs/2026-09-03-automatic-shared-queue-leader-design.md)
- [Liderança automática — plano](docs/superpowers/plans/2026-09-03-automatic-shared-queue-leader.md)
- [Guia operacional da fila](docs/CENTRAL-LAN.md)
- [Inicialização e atualização](docs/ATUALIZACAO-E-INICIALIZACAO.md)
- [Contingência pelo Portal — design](docs/superpowers/specs/2026-09-03-portal-nfe-fallback-design.md)
- [Fila segura — design](docs/superpowers/specs/2026-09-02-shared-folder-queue-design.md)
- [DANFE — design](docs/superpowers/specs/2026-08-31-danfe-fsist-design.md)
