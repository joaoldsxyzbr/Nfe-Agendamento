# NFe Agendamento

Aplicativo Windows interno para consultar, visualizar e baixar NF-e. Cada PC usa a interface local e a coordenação multi-PC acontece por uma fila segura na pasta compartilhada da empresa.

## Versão

- última release publicada: **v0.1.21**;
- `main`: candidata **v0.1.22**.

A candidata v0.1.22 reúne o fallback manual pelo Portal Nacional para `cStat=656`, proteção contra retries fiscais ambíguos, recuperação conservadora da fila, **liderança automática** e cache fiscal compartilhado entre líderes.

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
cache fiscal compartilhado 24h → fila fiscal serial → SEFAZ
   ↓
XML validado/cache cifrado
   ↓
resposta cifrada ao solicitante
```

Mesmo com A1 em todos os PCs, apenas o líder com lock exclusivo e saudável inicia trabalho fiscal.

## Liderança automática

O lock exclusivo fica em:

```text
P:\01-Nfe agendamento\status\central.lock
```

Somente um processo pode mantê-lo aberto com exclusividade. O líder publica heartbeat assinado e processa a fila; os demais ficam em standby.

Se o líder sair, outro candidato tenta assumir automaticamente. Antes de iniciar novo trabalho, o runtime revalida o handle do lock. Se a validação falhar, o PC deixa de iniciar trabalho fiscal até readquirir a liderança.

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

Ordem fiscal:

1. validar a chave de 44 dígitos;
2. consultar o cache XML compartilhado e cifrado;
3. deduplicar a mesma chave;
4. entrar na fila fiscal serializada;
5. respeitar o cooldown compartilhado;
6. consultar `NFeDistribuicaoDFe/consChNFe`;
7. validar o XML;
8. gravar o cache compartilhado;
9. devolver o resultado ao solicitante.

O cache possui retenção de **24 horas** e é legível por qualquer líder autorizado. Assim, uma troca de líder não perde o conhecimento de XMLs já obtidos e evita nova consulta desnecessária à SEFAZ.

## Robustez fiscal e failover

A política é deliberadamente conservadora:

- HTTP `429` não é repetido automaticamente;
- timeout fiscal não é repetido automaticamente;
- `5xx`, falha de conexão e `HttpRequestException` ambígua não são repetidos automaticamente;
- pedidos recuperados depois de interrupção não provocam segunda chamada fiscal;
- se o antigo líder pode já ter alcançado a SEFAZ, o sucessor devolve falha segura e exige nova ação explícita;
- `cStat=656` persiste em estado compartilhado, portanto mudar o líder não fura o cooldown;
- o cache fiscal também sobrevive ao failover, reduzindo consultas repetidas entre máquinas.

## Certificado A1

O A1 é configurado **localmente em cada PC confiável**. A tela de certificado não depende mais de papel fixo de Central.

Antes de contar com uma máquina como candidato, valide nela o certificado, a UF autora e uma consulta conhecida.

## Contingência pelo Portal Nacional

Quando a consulta automática recebe `cStat=656`, o aplicativo mantém o cooldown e não insiste automaticamente.

**Consultar pela Fazenda** é oferecido somente no **líder atual com lock saudável**. Isso impede que um PC em standby importe XML para um estado isolado ou opere fora da autoridade da fila.

```text
líder atual
  ↓
Portal Nacional via WebView2
  ↓
chave preenchida
  ↓
hCaptcha manual
  ↓
A1 local configurado
  ↓
download oficial do XML
  ↓
validação segura
  ↓
cache compartilhado cifrado de 24h
```

Regras:

- hCaptcha não é automatizado nem contornado;
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

Podem incluir auditoria, seleção de certificado, pareamento, chave de candidato protegida por DPAPI, solicitações pendentes, perfil WebView2 e dados legados necessários à migração. O cache fiscal operacional da arquitetura automática fica na pasta compartilhada, cifrado com a chave do grupo.

## Segurança de rede

- HTTP somente em loopback;
- Host/Origin validados;
- operações mutáveis protegidas por CSRF;
- nenhuma porta LAN adicional;
- nenhuma regra de firewall criada;
- nenhum mDNS necessário.

## Atualização

Na bandeja use **Verificar atualização**. O atualizador exige pacote oficial, HTTPS e digest SHA-256 válido antes de instalar.

Para a primeira atualização com liderança automática, abra primeiro o antigo PC Central uma vez para concluir o bootstrap do grupo. Depois abra os demais PCs.

Veja [Inicialização e atualização](docs/ATUALIZACAO-E-INICIALIZACAO.md).

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

O CI também publica Windows x64 autocontido, compacta o ZIP e disponibiliza artifact.

## Checklist da candidata v0.1.22

Automatizado esperado antes de considerar a `main` pronta:

- testes .NET completos;
- eleição de exatamente um líder;
- takeover preservando a mesma chave pública;
- replay persistente entre líderes;
- cooldown fiscal compartilhado;
- cache XML compartilhado legível após troca de líder;
- Portal restrito ao líder ativo no front-end e backend;
- recuperação sem segunda chamada fiscal;
- regressões JS de produto, feedback fiscal, Portal, lote e release;
- build Release;
- publish Windows x64 autocontido;
- ZIP/artifact.

Teste físico ainda necessário antes de promover a versão:

- [ ] pelo menos dois PCs reais disputam a liderança e somente um vence;
- [ ] ao fechar o líder, outro assume automaticamente;
- [ ] consulta funciona pelo standby antes e depois do failover;
- [ ] consultar uma NF-e, trocar o líder e confirmar que a mesma NF-e volta do cache sem nova ida à SEFAZ;
- [ ] A1 local funciona nos candidatos;
- [ ] Portal Nacional aparece somente no líder e abre no WebView2 real;
- [ ] chave é preenchida no Portal atual;
- [ ] hCaptcha permanece manual;
- [ ] A1 é oferecido/selecionado no fluxo real;
- [ ] XML oficial chega ao cache compartilhado;
- [ ] nova consulta retorna do cache.

Não provoque `cStat=656` real apenas para testar.

## Release

A última release publicada continua **v0.1.21**. Não publique v0.1.22 até concluir a validação desejada.

Fluxo oficial quando for aprovado:

1. Actions → **Release Bridge**;
2. referência `main`;
3. informar `v0.1.22`;
4. o workflow testa/builda/publica e prende a tag ao SHA aprovado.

## Documentação técnica

- [Liderança automática — design](docs/superpowers/specs/2026-09-03-automatic-shared-queue-leader-design.md)
- [Liderança automática — plano](docs/superpowers/plans/2026-09-03-automatic-shared-queue-leader.md)
- [Guia operacional da fila](docs/CENTRAL-LAN.md)
- [Inicialização e atualização](docs/ATUALIZACAO-E-INICIALIZACAO.md)
- [Contingência pelo Portal — design](docs/superpowers/specs/2026-09-03-portal-nfe-fallback-design.md)
- [Fila segura — design](docs/superpowers/specs/2026-09-02-shared-folder-queue-design.md)
- [DANFE — design](docs/superpowers/specs/2026-08-31-danfe-fsist-design.md)
