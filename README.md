# NFe Agendamento

Aplicativo Windows interno para consultar, visualizar e baixar NF-e usando o certificado A1 instalado somente no PC definido como **Central**.

## Versão publicada

**v0.1.18**

> A `main` já contém a nova arquitetura por pasta compartilhada com pareamento seguro. A `v0.1.18` ainda pertence à arquitetura LAN anterior, mas já contém o atualizador integrado. O pacote atual da `main` é o candidato de teste para a próxima release (`v0.1.19`).

## Arquitetura atual da `main`

Cada computador executa sua própria cópia do aplicativo e abre a interface somente em:

```text
http://127.0.0.1:17345
```

A comunicação entre os PCs não usa mais uma porta HTTP aberta no PC Central. Ela passa exclusivamente pela pasta corporativa:

```text
P:\01-Nfe agendamento
```

Fluxo:

```text
PC cliente pareado
   ↓ pedido criptografado + autenticado
P:\01-Nfe agendamento
   ↓
PC Central
   ↓
Certificado A1 + fila fiscal + cache + SEFAZ
   ↓ resposta criptografada
P:\01-Nfe agendamento
   ↓
PC cliente
```

O certificado A1 e sua chave privada continuam exclusivamente no Windows Certificate Store do PC Central. O compartilhamento nunca recebe o certificado nem a chave privada fiscal.

## Pasta compartilhada

A raiz operacional é fixa:

```text
P:\01-Nfe agendamento
```

A Central cria somente esta estrutura **dentro da pasta que já deve existir**:

```text
P:\01-Nfe agendamento\
├── .nfe-agendamento
├── fila\
├── pareamento\
├── processando\
├── respostas\
└── status\
```

Regras de segurança:

- o aplicativo não cria `P:\01-Nfe agendamento` se ela não existir;
- o aplicativo não enumera nem modifica a raiz `P:\`;
- o aplicativo não acessa pastas irmãs;
- nomes de arquivos de requisição são GUIDs gerados pelo próprio aplicativo;
- caminhos são normalizados e validados para permanecer dentro da raiz dedicada;
- junctions/symlinks/reparse points na árvore operacional são rejeitados;
- arquivos com nomes inválidos são descartados e não conseguem travar a fila;
- chave NF-e e XML não são gravados em texto puro no compartilhamento;
- arquivos da fila são temporários e não funcionam como backup de XML.

## Como a Central é definida

A Central é escolhida manualmente no painel Windows.

No PC que possui o certificado A1:

1. execute `NfeAgendamento.App.exe`;
2. clique em **Iniciar Central**;
3. o aplicativo salva localmente que esse PC foi configurado como Central;
4. ele tenta adquirir o lock exclusivo em `P:\01-Nfe agendamento\status`;
5. se conseguir, passa a publicar heartbeat assinado e processar a fila.

A preferência fica armazenada somente nesse PC. Depois de reiniciar o aplicativo ou o Windows, ele tenta reassumir a Central automaticamente.

Ao usar **Parar Central**, a preferência é removida, o heartbeat ativo é retirado e o PC não tenta reassumir nas próximas inicializações até **Iniciar Central** ser usado novamente.

O papel de Central **não é escolhido automaticamente pela presença do certificado**.

### Somente uma Central por vez

A Central mantém um lock exclusivo no compartilhamento. Se outro PC configurado como Central tentar assumir enquanto o lock estiver ativo, o painel mostra conflito e não inicia um segundo processador fiscal.

Se o processo central encerrar, o lock é liberado pelo sistema de arquivos e uma nova inicialização configurada como Central pode assumir.

Um PC apenas configurado como Central, mas sem o lock ativo, **não executa consulta fiscal diretamente**.

## Pareamento seguro dos PCs clientes

Antes da primeira consulta em cada PC cliente, faça o pareamento uma vez.

No PC Central ativo:

1. abra o sistema local;
2. na área **Conectar PCs**, clique em **Gerar código de pareamento**;
3. copie o código temporário exibido. Ele expira em aproximadamente 10 minutos.

No PC cliente:

1. execute a cópia local do app e abra `http://127.0.0.1:17345`;
2. informe o código exibido na Central;
3. clique em **Parear com a Central**;
4. aguarde a confirmação **PC pareado com a Central com sucesso**.

Depois disso, o cliente guarda localmente sua identidade, segredo e chave pública fixada da Central usando DPAPI. Não é necessário informar o código novamente em cada consulta ou reinicialização.

Se o pareamento local for perdido/corrompido ou a identidade da Central for substituída, gere outro código na Central e repita o pareamento.

## Uso nos outros PCs

Você continua distribuindo a pasta do aplicativo normalmente para cada computador.

Em cada cliente:

1. execute sua cópia local de `NfeAgendamento.App.exe`;
2. abra `http://127.0.0.1:17345`;
3. não configure nem instale o A1 por causa do NFe Agendamento;
4. faça o pareamento inicial descrito acima;
5. depois faça a consulta normalmente.

O cliente valida o heartbeat assinado da Central, cifra e autentica a solicitação e usa `P:\01-Nfe agendamento` como transporte. O backend local mantém o mesmo endpoint usado pela interface web, então o fluxo de consulta continua simples para o usuário.

A área de configuração do certificado fica indisponível nos PCs cliente.

## Criptografia e autenticação da fila

Cada consulta usa uma chave AES de 256 bits exclusiva.

Pedido:

1. cliente reserva uma sequência monotônica de sua identidade pareada;
2. gera `requestId` e chave AES aleatórios;
3. payload é cifrado com AES-GCM;
4. chave AES é cifrada com a chave pública RSA da Central usando OAEP-SHA256;
5. o envelope recebe autenticação HMAC com o segredo exclusivo daquele cliente;
6. somente o envelope cifrado/autenticado é escrito em `fila`.

A Central aceita o pedido somente se:

- o cliente estiver previamente autorizado pelo pareamento;
- o HMAC estiver correto;
- a sequência for maior que a última aceita, bloqueando replay/repetição;
- o envelope, tamanho, horário e chave NF-e forem válidos.

Resposta:

1. a Central recupera a chave AES com sua chave RSA privada local;
2. executa o fluxo fiscal existente;
3. cifra o resultado com AES-GCM;
4. cliente recupera a resposta e remove os artefatos consumidos.

A chave AES pendente do cliente e a chave RSA privada da Central ficam somente no armazenamento local do Windows e são protegidas por DPAPI.

### Heartbeat autenticado

O heartbeat contém a identidade da Central, horário, versão, chave pública e assinatura digital. O cliente pareado não passa a confiar em uma chave pública nova apenas porque ela apareceu no compartilhamento: ele compara com a chave da Central fixada durante o pareamento e valida a assinatura.

## Escrita atômica e recuperação

- publicação usa arquivo temporário no mesmo diretório e renomeação final;
- o processador ignora arquivos temporários;
- a Central reivindica um pedido movendo-o de `fila` para `processando`;
- pedidos adulterados ou com autenticação inválida nunca chegam à SEFAZ;
- arquivo inválido mais antigo não bloqueia pedidos legítimos da fila nem do pareamento;
- itens antigos em `processando` podem ser recuperados;
- se a resposta já existir, a recuperação não repete a chamada fiscal;
- respostas e temporários antigos são removidos por retenção;
- pedido que ainda está em `fila` é removido pelo próprio cliente se a consulta for abandonada ou exceder o timeout.

## Robustez fiscal

O transporte pela pasta fica **antes** do fluxo fiscal existente. A Central continua usando:

- deduplicação de consultas simultâneas da mesma chave;
- fila fiscal única e serializada;
- limite de operações únicas admitidas;
- cache XML local criptografado;
- cooldown persistente para `cStat=656`;
- retry limitado para falhas transitórias;
- auditoria local sem XML, chave completa, certificado ou CPF/CNPJ.

A consulta em lote permanece removida.

## Firewall e rede

A nova arquitetura multi-PC **não depende mais de conexão HTTP de entrada na porta 17345**.

O servidor web de cada instalação escuta somente em loopback:

```text
http://127.0.0.1:17345
```

Portanto:

- não é necessário criar regra de entrada para o NFe Agendamento no Firewall do Windows;
- não é necessário usar IP do PC Central;
- `nfeagendamento.local` não é usado;
- mDNS não é usado;
- `--lan` não habilita exposição de rede;
- o aplicativo não tenta contornar políticas de firewall da empresa;
- se `P:` estiver indisponível, o cliente mostra erro operacional em vez de abrir outra rota de rede.

## Painel Windows

A janela **Central NFe Agendamento** mostra:

- Papel deste PC;
- Pasta compartilhada;
- estado da Central/lock;
- heartbeat;
- processador.

Ações principais:

- **Iniciar Central**;
- **Parar Central**;
- **Abrir sistema**.

A bandeja mantém:

- **Abrir Central**;
- **Abrir sistema**;
- **Configurar certificado** — habilitado apenas no PC configurado como Central;
- **Verificar atualização**;
- **Iniciar com o Windows**;
- **Sair**.

Fechar a janela mantém o aplicativo na bandeja. Para encerrar completamente, use **Sair**.

## Configuração do certificado

Somente o PC configurado como Central pode acessar os endpoints de administração do certificado.

No Central:

1. abra o sistema;
2. escolha o A1 válido instalado no Windows;
3. informe a UF autora;
4. salve;
5. faça uma consulta conhecida para validar.

O certificado não é copiado para o GitHub, para o compartilhamento nem para os clientes.

## Atualização pelo próprio aplicativo

No menu da bandeja, use **Verificar atualização**.

O atualizador valida a release oficial, o pacote Windows e SHA-256 antes de substituir arquivos.

A **v0.1.18 já contém esse updater**. Assim, quando a próxima release com a arquitetura por pasta compartilhada for publicada, uma instalação v0.1.18 poderá migrar usando **Verificar atualização**. Instalações anteriores que não possuam o updater precisam ser atualizadas manualmente uma vez.

Na primeira execução de um cliente após migrar da arquitetura LAN anterior para a fila compartilhada, é necessário realizar o pareamento inicial com a Central.

Detalhes: [Inicialização e atualização](docs/ATUALIZACAO-E-INICIALIZACAO.md).

## Dados locais

Dados protegidos ficam em:

```text
%LOCALAPPDATA%\NfeAgendamento
```

Incluem, conforme o papel do PC:

- configuração local `ConfiguredAsCentral`;
- cache XML criptografado por DPAPI;
- cooldown fiscal;
- auditoria fiscal;
- chave RSA privada da Central protegida por DPAPI;
- chaves AES pendentes dos clientes protegidas por DPAPI;
- identidade, segredo e chave pública fixada da Central no cliente, protegidos por DPAPI;
- lista de clientes autorizados no PC Central, protegida por DPAPI.

O compartilhamento é somente transporte efêmero.

## Segurança

- A1 e chave privada fiscal somente no Central;
- HTTP somente em loopback;
- requisições locais validam Host e Origin;
- operações mutáveis exigem CSRF;
- tamanho de requisição e arquivos da fila é limitado;
- clientes não administram certificados;
- pareamento temporário autoriza explicitamente cada PC cliente;
- heartbeat é assinado e a chave pública da Central fica fixada no cliente pareado;
- HMAC autentica a identidade de cada cliente;
- sequência monotônica bloqueia replay de solicitações;
- AES-GCM autentica e cifra pedidos e respostas;
- RSA OAEP-SHA256 protege a chave de sessão enviada à Central;
- junctions/reparse points são rejeitados;
- somente uma Central mantém o lock do compartilhamento;
- uma Central sem lock ativo não executa consulta fiscal;
- nenhuma rotina operacional acessa outras áreas de `P:`;
- nenhum fallback tenta abrir firewall, mDNS ou servidor LAN.

## Mapeamento Fernando Klein

O mapeamento interno é aplicado somente quando o CPF/CNPJ do emitente corresponde ao fornecedor configurado. O XML e o `cProd` fiscal original nunca são alterados. Descrições desconhecidas não recebem código inventado.

A regressão automatizada cobre os produtos cadastrados, aliases, normalização, isolamento por emitente e item desconhecido.

## Desenvolvimento e validação

```bash
dotnet restore Nfe-Agendamento.sln
dotnet test Nfe-Agendamento.sln -c Release
node tests/js/product-mapping-regression.test.js
node tests/js/lookup-feedback-regression.test.js
node tests/js/release-readiness-regression.test.js
dotnet build Nfe-Agendamento.sln -c Release
```

O CI também publica um pacote Windows autocontido de teste como artifact.

Nenhum teste deve depender do `P:` real, de certificado real ou da SEFAZ real. Os testes da fila usam diretórios temporários.

## Criar uma release

Existe um único fluxo oficial:

1. abra **Actions**;
2. escolha **Release Bridge**;
3. clique em **Run workflow**;
4. informe uma versão maior que a última publicada;
5. execute.

A última versão publicada é `v0.1.18`; portanto, a próxima release deve usar uma versão superior, como `v0.1.19`.

O workflow executa testes, regressões, build, publish Windows x64 autocontido e cria a tag/release no SHA validado.

## Checklist físico da próxima release

- [ ] todos os PCs conseguem acessar `P:\01-Nfe agendamento`;
- [ ] cada PC executa sua própria cópia local do aplicativo;
- [ ] cada PC abre `http://127.0.0.1:17345`;
- [ ] somente o PC com A1 é marcado manualmente com **Iniciar Central**;
- [ ] o painel do Central mostra pasta, heartbeat e processador operacionais;
- [ ] Central gera um código de pareamento;
- [ ] PC 2 é pareado com sucesso;
- [ ] PC 3 é pareado com sucesso;
- [ ] os clientes mostram Central online;
- [ ] cliente sem A1 consulta uma NF-e conhecida;
- [ ] XML e DANFE funcionam no cliente;
- [ ] arquivos em `fila`/`pareamento`/`respostas` não expõem chave NF-e ou XML em texto puro;
- [ ] reiniciar o Central faz ele tentar reassumir automaticamente;
- [ ] **Parar Central** remove o heartbeat e impede reassunção automática na inicialização seguinte;
- [ ] desligar o Central faz os clientes mostrarem Central offline;
- [ ] arquivos fora de `P:\01-Nfe agendamento` permanecem intocados;
- [ ] nenhuma configuração de Firewall do Windows é solicitada pelo aplicativo.

Não provoque um `656` real apenas para testar cooldown; esse comportamento possui cobertura automatizada.

## Documentação técnica

- [Guia operacional da Central](docs/CENTRAL-LAN.md)
- [Inicialização e atualização](docs/ATUALIZACAO-E-INICIALIZACAO.md)
- [Design atual — fila segura em pasta compartilhada](docs/superpowers/specs/2026-09-02-shared-folder-queue-design.md)
- [Plano de implementação da fila compartilhada](docs/superpowers/plans/2026-09-02-shared-folder-queue.md)
- [Design do navegador local](docs/superpowers/specs/2026-08-31-local-browser-nfe-design.md)
- [Design do DANFE](docs/superpowers/specs/2026-08-31-danfe-fsist-design.md)

A arquitetura LAN anterior foi substituída e permanece apenas em documentos históricos marcados como superseded.
