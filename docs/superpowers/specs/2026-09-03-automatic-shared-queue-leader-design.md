# Liderança automática da fila compartilhada — Design

## Objetivo

Eliminar a dependência de um PC Central fixo sem permitir processamento fiscal concorrente. Todos os PCs confiáveis da empresa, já com o mesmo certificado A1 instalado e acesso à pasta `P:\01-Nfe agendamento`, tornam-se candidatos a líder. Exatamente um candidato por vez processa a fila e consulta a SEFAZ.

## Princípios

- Não abrir portas de rede; toda coordenação continua pela pasta compartilhada.
- `central.lock` continua sendo a autoridade exclusiva para eleição do líder.
- Sem lock exclusivo válido, nenhuma chamada fiscal é iniciada.
- A identidade criptográfica atualmente pareada pelos clientes permanece a mesma após a migração.
- Nenhum segredo privado é gravado em claro na pasta compartilhada.
- O failover nunca repete automaticamente uma chamada fiscal ambígua.
- O certificado A1 continua local em cada PC e não é exportado nem copiado pelo aplicativo.
- Alterações são feitas direto em `main`, com TDD e CI verde antes de considerar o trabalho concluído.

## Arquitetura

### 1. Identidade única do grupo

A chave RSA atualmente mantida pelo `CentralKeyStore` no PC Central vira a identidade criptográfica do grupo. A chave pública não muda, portanto os `ClientPairingStore` já existentes continuam válidos.

Na primeira execução da versão nova, o PC que ainda estiver marcado como Central realiza a migração inicial:

1. adquire `central.lock`;
2. lê a identidade RSA atual;
3. gera `GroupStateKey` aleatória de 32 bytes;
4. grava a identidade RSA na pasta compartilhada cifrada com AES-256-GCM usando `GroupStateKey`;
5. migra a lista atual de clientes autorizados para um estado compartilhado cifrado/autenticado com `GroupStateKey`;
6. cria um pacote de candidatura para cada cliente autorizado, cifrado individualmente com o `ClientSecret` desse cliente;
7. guarda `GroupStateKey` localmente no PC migrador usando DPAPI CurrentUser.

O pacote individual contém apenas os dados mínimos para tornar o PC candidato: `GroupStateKey`, versão do formato e fingerprint da chave pública do grupo. O pacote nunca contém o certificado A1.

### 2. Adesão automática dos PCs já pareados

Cada PC cliente já possui localmente `ClientId`, `ClientSecret` e a chave pública da Central no `ClientPairingStore` protegido por DPAPI.

Ao iniciar:

1. procura seu pacote de candidatura na pasta compartilhada;
2. decifra com seu `ClientSecret`;
3. valida que a fingerprint da identidade de grupo corresponde à chave pública já pareada;
4. grava `GroupStateKey` localmente com DPAPI;
5. passa a ser candidato elegível a líder.

Não há senha nova e não há reapareamento geral.

### 3. Novos PCs

O pareamento atual continua existindo. Quando um novo cliente é autorizado, o líder atual:

1. adiciona o cliente ao estado compartilhado de autorizados;
2. publica o pacote de candidatura cifrado com o novo `ClientSecret`;
3. só então conclui a resposta de pareamento.

Assim todo PC pareado no novo modelo também se torna candidato automaticamente.

### 4. Estado compartilhado de autorização e replay

A lista de clientes autorizados deixa de depender do DPAPI de uma única máquina. O arquivo compartilhado contém `ClientId`, nome, segredo de autenticação e `LastSequence`, serializados e cifrados por AES-256-GCM com `GroupStateKey`.

Somente o processo que possui `central.lock` pode avançar `LastSequence` ou alterar a lista. Escritas são feitas em arquivo temporário seguido de rename atômico.

O estado local antigo é mantido apenas para bootstrap/migração e compatibilidade de rollback; depois da migração, processamento e pareamento usam o estado compartilhado.

### 5. Eleição e failover

Todos os PCs com `GroupStateKey` local tentam adquirir `central.lock`.

- vencedor: `Active`, publica heartbeat e processa fila;
- demais: `Standby`, usam o app como clientes e tentam assumir periodicamente;
- pasta indisponível: `ShareUnavailable`, sem chamada fiscal;
- perda do lock/SMB: processamento fiscal é interrompido e o PC volta a standby.

O lock continua sendo um `FileStream` com `FileShare.None`, aproveitando a exclusividade do SMB/Windows já implementada.

### 6. Processamento local versus remoto

`LookupDispatchService` deixa de perguntar se o PC foi manualmente configurado como Central.

- se `SharedQueueCentralService.IsActive`: consulta pelo `NfeLookupService` local;
- caso contrário: envia pela `SharedQueueClient` ao líder anunciado no heartbeat.

Portanto a mesma máquina pode alternar automaticamente entre cliente e líder sem reiniciar ou trocar configuração.

### 7. Heartbeat e identidade

O heartbeat continua assinado com a mesma identidade RSA do grupo e continua anunciando a mesma chave pública. `CentralId` passa a significar apenas o nome da máquina que atualmente detém o lock.

Clientes já pareados validam o heartbeat normalmente e aceitam a troca de máquina porque a chave pública permanece idêntica.

### 8. Certificado e Portal

Como todos os PCs confiáveis possuem A1 instalado:

- configuração/seleção do certificado deixa de ser limitada ao PC marcado como Central;
- fallback manual pelo Portal pode ser aberto localmente em qualquer PC que tenha certificado configurado;
- chamadas automáticas de distribuição SEFAZ continuam exclusivas do líder.

O certificado não participa da distribuição da identidade do grupo e nunca é exportado.

### 9. Interface

A UI deixa de apresentar “Iniciar Central / Parar Central” como operação normal. O diagnóstico passa a comunicar:

- `Este PC está processando a fila` quando líder;
- `Fila processada por <PC>` quando standby com líder online;
- `Aguardando outro PC assumir` quando elegível mas sem heartbeat válido;
- `Pasta compartilhada indisponível` quando aplicável.

O antigo `ConfiguredAsCentral` permanece somente durante a migração da primeira versão e não decide mais o caminho fiscal depois que o grupo estiver inicializado.

## Segurança e falhas

- `GroupStateKey` local: DPAPI CurrentUser.
- identidade RSA compartilhada: AES-256-GCM com `GroupStateKey`.
- estado de autorizados/replay: AES-256-GCM com `GroupStateKey`.
- pacote de candidatura: AES-256-GCM com chave derivada do `ClientSecret` e contexto específico.
- todos os envelopes incluem versão e AAD para impedir troca entre tipos de arquivo.
- arquivos compartilhados têm limite de tamanho, validação de caminho e proteção contra reparse point seguindo `SharedQueueFileIO`.
- candidato com pacote adulterado não vira líder.
- identidade pública diferente da já pareada bloqueia a importação.
- recuperação de `processando` mantém a regra atual: consulta potencialmente enviada antes da queda não é repetida automaticamente.

## Migração

1. Atualizar primeiro o executável compartilhado.
2. Iniciar o PC que hoje está marcado como Central pelo menos uma vez.
3. Ele cria o estado de grupo e pacotes para os clientes já autorizados.
4. Cada cliente, ao abrir a versão nova, importa automaticamente seu pacote.
5. Depois de todos importarem, desligar o antigo Central deve fazer outro PC assumir sem intervenção.

A migração é idempotente. Se o estado de grupo já existir, nenhum PC gera uma nova identidade.

## Testes obrigatórios

- somente um candidato mantém `central.lock`;
- cliente existente importa pacote sem reaparear;
- pacote de outro `ClientId` não é aceito;
- pacote adulterado não é aceito;
- fingerprint de chave pública divergente não é aceita;
- estado compartilhado de autorizados persiste `LastSequence` entre líderes;
- dois líderes em sequência reconhecem o mesmo cliente e bloqueiam replay;
- standby envia consulta para o líder;
- após liberar o lock, outro candidato assume e assina heartbeat com a mesma chave pública;
- pedido recuperado após queda não causa segunda chamada fiscal;
- ausência/perda da pasta impede chamada fiscal;
- seleção de certificado e Portal não dependem mais de `ConfiguredAsCentral`.

## Fora de escopo

- alta disponibilidade quando a própria pasta SMB está offline;
- sincronização/instalação automática do certificado A1;
- múltiplas empresas/certificados distintos na mesma fila;
- processamento fiscal simultâneo por múltiplos líderes;
- remoção imediata dos arquivos de compatibilidade antigos antes de uma versão posterior de limpeza.
