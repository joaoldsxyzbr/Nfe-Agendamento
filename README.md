# NFe Agendamento

Aplicativo Windows interno para consultar, visualizar e baixar NF-e. O certificado A1 fica somente no PC definido como **Central**; os demais computadores usam uma fila segura na pasta compartilhada da empresa.

## Versão

- última release publicada: **v0.1.21**;
- `main`: candidata **v0.1.22**.

A v0.1.22 adiciona uma contingência manual pelo **Portal Nacional da NF-e** quando a consulta automática recebe `cStat=656`, sem copiar o A1 para os PCs clientes e sem automatizar captcha. A revisão desta candidata também remove retries fiscais ambíguos, endurece recuperação de consultas interrompidas, timeout da fila, vínculo do pareamento e o fluxo de release.

## Arquitetura atual

Cada PC executa sua própria cópia do aplicativo e abre a interface local em:

```text
http://127.0.0.1:17345
```

Os clientes não fazem consulta fiscal direta. O transporte entre PCs usa:

```text
P:\01-Nfe agendamento
```

Fluxo normal:

```text
PC cliente pareado
   ↓ pedido cifrado/autenticado
P:\01-Nfe agendamento
   ↓
PC Central
   ↓
cache 24h → fila fiscal → SEFAZ NFeDistribuicaoDFe/consChNFe
   ↓
XML validado + cache criptografado
   ↓ resposta cifrada
P:\01-Nfe agendamento
   ↓
PC cliente
```

O certificado A1 e sua chave privada permanecem exclusivamente no Windows Certificate Store do PC Central.

## Consulta e cache

A consulta individual usa `POST /api/nfe/lookup`.

Ordem no Central:

1. validar a chave de 44 dígitos;
2. verificar o cache XML criptografado;
3. deduplicar consultas simultâneas da mesma chave;
4. entrar na fila fiscal única/serializada;
5. respeitar eventual cooldown de `cStat=656`;
6. consultar `NFeDistribuicaoDFe` por `consChNFe`;
7. validar o XML recebido;
8. gravar o XML localizado no cache;
9. devolver o XML ao solicitante.

O cache de XML localizado possui retenção de **24 horas**. Uma nova consulta da mesma chave dentro desse período não precisa chamar a SEFAZ novamente.

## Contingência pelo Portal Nacional da NF-e

Quando o `NFeDistribuicaoDFe` retorna `cStat=656`, o aplicativo mantém o cooldown e **não insiste automaticamente** na SEFAZ.

No **PC Central**, a tela passa a oferecer:

**Consultar pela Fazenda**

Fluxo:

```text
cStat 656
   ↓
Consultar pela Fazenda
   ↓
janela WebView2 no PC Central
   ↓
Portal Nacional da NF-e
   ↓
chave preenchida automaticamente
   ↓
hCaptcha resolvido manualmente pelo usuário
   ↓
certificado A1 configurado selecionado pelo aplicativo
   ↓
download oficial do XML
   ↓
validação da chave e estrutura
   ↓
cache criptografado de 24h
```

Regras importantes:

- o hCaptcha **não é automatizado nem contornado**;
- a janela de contingência aceita navegação fiscal somente no domínio oficial `www.nfe.fazenda.gov.br`;
- o A1 usado é exatamente o certificado já configurado no NFe Agendamento, comparado por thumbprint;
- somente o download oficial de `downloadNFe.aspx` é capturado como XML de contingência;
- o arquivo é baixado primeiro para uma área temporária;
- DTD e resolução de entidades externas são proibidos no parser;
- o XML é limitado a 10 MiB;
- o `infNFe/@Id` precisa ser exatamente `NFe` + a chave consultada;
- XML de outra chave é rejeitado;
- após a importação, o temporário é removido e o XML entra no mesmo cache criptografado do fluxo normal;
- somente uma janela de contingência pode ficar aberta por vez.

A validação do XML do Portal possui testes automatizados para XML processado válido, chave divergente, raiz inválida e tentativa de DTD/entidade externa.

### Nos PCs clientes

A contingência não abre com certificado nos PCs clientes. Se um cliente receber o bloqueio 656, a interface orienta que a consulta alternativa seja concluída no **PC Central**.

Depois que o Central obtiver o XML pelo Portal, uma nova consulta da mesma chave passa a ser atendida pelo cache normal.

### WebView2 Runtime

A janela usa `Microsoft.Web.WebView2`. Em Windows 11 e na maioria das instalações atuais do Windows 10 o Runtime já está presente. Se não estiver, o aplicativo informa o pré-requisito em vez de falhar silenciosamente.

## Pasta compartilhada

A Central utiliza somente a árvore dedicada abaixo de `P:\01-Nfe agendamento`:

```text
P:\01-Nfe agendamento\
├── .nfe-agendamento
├── fila\
├── pareamento\
├── processando\
├── respostas\
└── status\
```

Proteções principais:

- a raiz `P:\01-Nfe agendamento` precisa existir previamente;
- o aplicativo não enumera nem altera outras áreas de `P:`;
- arquivos operacionais usam nomes controlados/GUID;
- caminhos são normalizados e confinados à raiz dedicada;
- junctions, symlinks e outros reparse points operacionais são rejeitados;
- chave NF-e e XML não ficam em texto puro na fila;
- arquivos da fila são temporários e não são backup de XML;
- publicação e recuperação usam escrita/movimentação atômica;
- pedidos inválidos/adulterados não chegam à camada fiscal;
- se a Central cair depois de aceitar uma solicitação e antes de publicar a resposta, a recuperação **não executa uma segunda chamada fiscal**; o cliente recebe uma falha segura e deve fazer uma nova consulta.

Os clientes aguardam até **3 minutos** pela resposta da Central. Esse prazo cobre espera normal da fila mais uma chamada fiscal sem fazer o cliente abandonar cedo demais o segredo usado para abrir a resposta cifrada.

## Central e pareamento

No PC que contém o A1:

1. execute `NfeAgendamento.App.exe`;
2. clique em **Iniciar Central**;
3. configure o certificado A1 e a UF autora;
4. mantenha acesso a `P:\01-Nfe agendamento`.

A Central mantém lock exclusivo e heartbeat assinado. Outra instalação não assume o papel enquanto o lock estiver ativo.

Para autorizar um cliente:

1. no Central, gere um código de pareamento;
2. no cliente, informe esse código na área **Conectar à Central**;
3. após o pareamento, a identidade e as chaves locais ficam protegidas por DPAPI.

O cliente valida a identidade/assinatura da Central e usa AES-GCM, HMAC e RSA OAEP-SHA256 no protocolo da fila. A resposta de pareamento precisa corresponder ao **mesmo `requestId`** da solicitação criada pelo cliente; uma resposta válida de outra solicitação é rejeitada.

## Consulta em lote

O lote reutiliza o mesmo `POST /api/nfe/lookup` e não cria paralelismo fiscal adicional.

- até 50 chaves únicas por lote;
- duplicatas são removidas;
- cada instalação envia uma consulta por vez;
- a Central continua serializando tudo;
- cache, deduplicação e cooldown são os mesmos da consulta individual;
- `cStat=656` interrompe o restante do lote;
- cancelar impede o início dos próximos itens.

## DANFE

O DANFE é montado localmente a partir do XML validado.

- visualização em popup próprio;
- `Ctrl + scroll` aplica zoom somente ao DANFE;
- impressão / salvar PDF usa o navegador local;
- XML fiscal original não é alterado para gerar o DANFE.

## Mapeamento Fernando Klein

O mapeamento interno de produtos é aplicado somente ao fornecedor configurado. Ele altera apenas a apresentação interna de código/descrição quando há correspondência conhecida; o XML e o `cProd` fiscal original permanecem intactos.

## Robustez fiscal

O Central mantém:

- cache criptografado de 24h;
- deduplicação da mesma chave;
- fila única e serializada;
- limite de operações admitidas;
- cooldown persistente de 656;
- HTTP `429 Too Many Requests` **não é repetido automaticamente**;
- timeout do transporte fiscal **não é repetido automaticamente**;
- falha HTTP `5xx`, falha de conexão ou outra `HttpRequestException` após iniciar a tentativa fiscal **não é repetida automaticamente**, pois o resultado pode ser ambíguo;
- recuperação de solicitação interrompida não dispara uma segunda consulta fiscal;
- auditoria local sem XML, chave completa ou certificado.

A política atual é deliberadamente conservadora: depois que uma tentativa fiscal começa, uma falha de transporte não gera retry automático. O usuário pode tentar novamente de forma explícita depois de avaliar a situação.

A contingência do Portal é uma rota manual separada e **não remove nem reduz essas proteções**.

## Dados locais

Os dados locais ficam em:

```text
%LOCALAPPDATA%\NfeAgendamento
```

Podem incluir:

- papel local Central/cliente;
- cache XML protegido por DPAPI;
- estado de cooldown fiscal;
- auditoria fiscal;
- seleção do certificado;
- identidade e material criptográfico da fila;
- perfil do WebView2 usado pela contingência.

## Segurança

- certificado A1 somente no Central;
- HTTP somente em loopback;
- Host/Origin validados;
- operações mutáveis protegidas por CSRF;
- clientes não administram certificado;
- fila cifrada e autenticada;
- replay bloqueado por sequência monotônica;
- heartbeat assinado;
- resposta de pareamento vinculada à solicitação exata;
- somente uma Central ativa;
- XML do Portal validado antes do cache;
- captcha permanece humano;
- nenhuma rotina abre porta LAN, altera firewall ou tenta usar mDNS.

## Atualização

Na bandeja do Windows, use **Verificar atualização**. O atualizador valida a release e o pacote antes da instalação.

A atualização da v0.1.22 deve ser publicada pelo fluxo **Release Bridge**, com versão superior à v0.1.21. O workflow manual recusa execução quando a referência selecionada não é `main` e mantém a tag presa ao SHA exato que foi testado e empacotado.

## Desenvolvimento e validação

```bash
dotnet restore Nfe-Agendamento.sln
dotnet test Nfe-Agendamento.sln -c Release
node tests/js/product-mapping-regression.test.js
node tests/js/lookup-feedback-regression.test.js
node tests/js/portal-fallback-regression.test.js
node tests/js/batch-lookup-regression.test.js
node tests/js/release-readiness-regression.test.js
dotnet build Nfe-Agendamento.sln -c Release
```

O CI também executa o publish Windows x64 autocontido e gera um ZIP como artifact.

O CI cobre a política de não repetir falhas fiscais ambíguas, a recuperação da fila sem uma segunda chamada fiscal, as regras estáticas da contingência e a validação segura do XML. A integração externa **WebView2 + Portal real + hCaptcha + seleção do A1 + download oficial** continua exigindo teste físico no PC Central.

## Checklist da v0.1.22

### Automatizado

- [x] restore;
- [x] testes .NET de cache, fila fiscal, cooldown, deduplicação e certificado;
- [x] falha de rede/HTTP ambígua não é repetida automaticamente;
- [x] recuperação de consulta interrompida não executa segunda chamada fiscal;
- [x] validação do XML do Portal rejeita chave divergente, raiz inválida e DTD/entidade externa;
- [x] vínculo da resposta de pareamento ao `requestId`;
- [x] regressões JS de produto, feedback fiscal, contingência, lote e prontidão de release;
- [x] build Release;
- [x] publish Windows x64 autocontido;
- [x] geração do ZIP/artifact.

### Teste físico no PC Central

O que permanece físico é somente a integração com serviços/recursos externos que o CI não consegue reproduzir com fidelidade:

- [ ] a janela WebView2 abre o Portal Nacional real;
- [ ] a chave é preenchida automaticamente na página atual do Portal;
- [ ] o hCaptcha continua manual e funcional;
- [ ] o A1 configurado é oferecido/selecionado no fluxo real do Portal;
- [ ] o download oficial chega ao aplicativo e entra no cache;
- [ ] depois do download real, nova consulta da mesma chave retorna pelo cache.

Não provoque um `656` real apenas para testar. Se não houver bloqueio ativo, o fluxo do Portal pode ser validado posteriormente com uma chave conhecida por ação operacional controlada.

## Release

Fluxo oficial:

1. abra **Actions**;
2. escolha **Release Bridge**;
3. mantenha a referência em **main**;
4. clique em **Run workflow**;
5. informe `v0.1.22`;
6. execute somente depois da validação desejada para a release.

O workflow valida referência e versão, executa testes/build/publish e cria a release no SHA aprovado.

## Documentação técnica

- [Design da contingência pelo Portal NF-e](docs/superpowers/specs/2026-09-03-portal-nfe-fallback-design.md)
- [Plano da contingência pelo Portal NF-e](docs/superpowers/plans/2026-09-03-portal-nfe-fallback.md)
- [Guia operacional da Central](docs/CENTRAL-LAN.md)
- [Inicialização e atualização](docs/ATUALIZACAO-E-INICIALIZACAO.md)
- [Design da fila segura](docs/superpowers/specs/2026-09-02-shared-folder-queue-design.md)
- [Plano da fila compartilhada](docs/superpowers/plans/2026-09-02-shared-folder-queue.md)
- [Design do navegador local](docs/superpowers/specs/2026-08-31-local-browser-nfe-design.md)
- [Design do DANFE](docs/superpowers/specs/2026-08-31-danfe-fsist-design.md)
