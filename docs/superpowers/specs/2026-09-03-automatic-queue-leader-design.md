# Liderança automática da fila compartilhada

## Objetivo

Eliminar a dependência de um PC Central fixo sem permitir processamento fiscal concorrente. Todos os PCs confiáveis da empresa podem ser candidatos; exatamente um deles processa a fila por vez.

## Princípios

- A pasta compartilhada continua sendo o barramento da fila.
- `status/central.lock`, mantido aberto com `FileShare.None`, continua sendo a autoridade de exclusão mútua.
- Sem lock exclusivo válido, nenhum PC pode chamar a SEFAZ pela fila.
- Todos os candidatos possuem o mesmo certificado A1 instalado no Windows.
- A identidade criptográfica da fila continua separada do certificado A1.
- O A1 é usado somente para desbloquear a identidade/estado compartilhado da fila em um PC candidato.
- Uma troca de líder nunca repete automaticamente uma consulta fiscal ambígua.
- Não automatizar nem contornar hCaptcha no fallback do Portal Nacional.

## Arquitetura

Cada instância do app opera simultaneamente como cliente da fila e candidata a líder. Quando a pasta compartilhada está disponível e o certificado exigido pelo grupo existe localmente, a instância tenta adquirir `central.lock`. Quem obtém o lock torna-se `Active`; as demais permanecem `Standby` e continuam usando a fila normalmente.

O serviço de processamento só executa pareamento, manutenção e `SharedQueueProcessor.ProcessOneAsync` enquanto o runtime possui o lease exclusivo.

## Identidade do grupo

A fila precisa manter a mesma chave pública já fixada nos clientes mesmo quando outro PC assume. Para preservar os pareamentos existentes, o primeiro bootstrap da nova arquitetura usa a chave RSA atual de `CentralKeyStore` da Central existente como identidade do grupo.

Criar `SharedQueueClusterIdentityStore` com um envelope compartilhado em `status/cluster-identity.json` contendo:

- versão do formato;
- thumbprint do A1 que protege o grupo;
- UF autora usada pelo app;
- chave de estado aleatória de 256 bits, embrulhada com a chave pública RSA do A1 usando OAEP-SHA256;
- chave privada PKCS#8 da identidade da fila, cifrada por AES-256-GCM com a chave de estado;
- nonce/tag e metadados autenticados.

O envelope nunca contém a chave privada da fila nem a chave de estado em claro. Um candidato só consegue abrir o bundle se possuir a chave privada do mesmo A1 no Windows.

Se o provedor do certificado não suportar RSA/OAEP-SHA256, o PC não se torna líder e exibe erro explícito; ele continua podendo usar a fila como cliente.

## Bootstrap e migração

Na primeira execução após a atualização:

1. A antiga Central configurada tenta o `central.lock` pelo fluxo existente.
2. Se `cluster-identity.json` ainda não existe, ela lê o A1 atualmente selecionado.
3. Exporta a chave privada RSA atual de `CentralKeyStore` apenas em memória, gera a chave de estado, cria o bundle e grava atomicamente na pasta compartilhada.
4. Migra a lista local `authorized-clients.bin`, incluindo `LastSequence`, para o estado compartilhado cifrado.
5. Publica heartbeat usando a mesma identidade RSA já conhecida pelos clientes.
6. Marca localmente a migração como concluída e passa ao modo candidato automático.

O bootstrap é idempotente: se o bundle já existe, nenhum PC cria outra identidade.

## Estado compartilhado de clientes

`AuthorizedClientStore` deixa de depender de DPAPI local durante o modo de grupo. O arquivo `status/authorized-clients.dat` é cifrado por AES-256-GCM com a chave de estado do cluster e escrito atomicamente.

Somente o líder com `central.lock` pode autenticar/avançar `LastSequence`, autorizar novos clientes ou migrar o estado legado. Dessa forma, o replay protection continua global após failover.

A leitura/escrita deve rejeitar versão inválida, autenticação GCM inválida, tamanho excessivo, reparse points e arquivos fora dos caminhos previstos.

## Cooldown fiscal global

O cooldown de `cStat=656` não pode ficar apenas na máquina que era líder. O estado de bloqueio passa a ser persistido na pasta compartilhada, de forma atômica, para que um novo líder respeite imediatamente o mesmo `BlockedUntilUtc`.

O conteúdo não precisa conter XML ou certificado. Apenas o mínimo necessário para manter a janela de proteção fiscal entre líderes.

## Cache e deduplicação

O cache XML continua local por PC nesta etapa. A deduplicação em memória continua válida porque existe um único líder por vez. Após failover, uma nova consulta explicitamente solicitada pode consultar a SEFAZ novamente caso o novo líder não tenha o XML em cache.

Não mover XMLs para a pasta compartilhada neste projeto: isso aumentaria superfície de dados sensíveis sem ser necessário para eliminar a Central fixa.

## Failover

- Líder saudável: mantém `central.lock` e heartbeat.
- Encerramento normal: dispose do lease e outro candidato pode assumir.
- Crash/processo encerrado: o Windows/SMB libera o handle; outro candidato assume na próxima tentativa.
- Share indisponível ou lock incerto: o runtime entra em `ShareUnavailable/Standby` e não chama SEFAZ.
- Pedido encontrado em `processando` após queda: mantém a regra existente de recuperação segura; se o sequence já foi consumido, publica falha segura e não repete SEFAZ.
- `central.lock` é a autoridade; heartbeat é observabilidade, não autorização para processamento.

## Certificado

`cluster-identity.json` define o thumbprint do A1 esperado pelo grupo. Cada candidato procura esse certificado no `CurrentUser/My`. Não é necessário selecionar manualmente o certificado em todos os PCs depois que o grupo foi inicializado, desde que o mesmo certificado esteja instalado com chave privada.

A configuração local de certificado continua disponível para bootstrap, renovação e Portal. A UI deixa de restringir a administração de certificado ao antigo PC Central.

Renovação de A1 fica fora desta mudança automática: deve existir uma ação administrativa explícita futura para reembrulhar a chave de estado com o novo certificado antes de remover o antigo.

## Portal Nacional

O fallback manual do Portal pode ser aberto no PC onde o usuário está trabalhando, desde que o A1 exigido esteja instalado. Ele não depende do PC que atualmente lidera a fila.

O fluxo continua manual, com WebView2, domínio oficial, seleção exata do certificado e hCaptcha humano. O XML baixado continua no cache local daquele PC.

## Interface

Remover da experiência principal a ideia de `Iniciar Central`/`Parar Central`.

Estados apresentados:

- `Este PC está processando a fila` quando possui o lease;
- `Fila processada por <nome>` quando outro heartbeat válido está ativo;
- `Aguardando líder` quando não há líder saudável;
- `Pasta compartilhada indisponível` quando aplicável;
- `Certificado do grupo ausente neste PC` quando a máquina não pode assumir.

O app continua funcional como cliente mesmo quando não é elegível para liderança.

## Compatibilidade

- Preservar protocolo de request/response e a chave pública da fila durante migração para não exigir novo pareamento.
- Preservar `RequestId`, sequence/HMAC, AES-GCM, RSA-OAEP-SHA256 e assinatura RSA-PSS já existentes.
- Não alterar formato de XML fiscal ou DANFE.
- Não criar serviço externo, banco adicional ou dependência paga.

## Testes obrigatórios

- apenas um candidato adquire `central.lock`;
- segundo candidato assume após dispose/crash simulado do primeiro;
- bootstrap preserva a chave pública da Central existente;
- bundle não contém PKCS#8 nem chave de estado em claro;
- candidato sem A1 correto não consegue abrir identidade e não processa fila;
- dois candidatos com o mesmo A1 abrem a mesma identidade;
- estado compartilhado preserva `LastSequence` após troca de líder;
- replay continua bloqueado após failover;
- recuperação de pedido interrompido continua sem segunda chamada fiscal;
- cooldown `656` persiste e é respeitado pelo novo líder;
- cliente continua usando a fila enquanto outro PC é líder;
- Portal/certificado deixam de depender de `IsConfiguredAsCentral`;
- suite .NET, regressões JS, Release build e publish Windows permanecem verdes.

## Critérios de aceite

Com pelo menos dois PCs confiáveis, ambos com o mesmo A1 instalado e acesso à pasta compartilhada:

1. nenhum PC precisa ser designado permanentemente como Central;
2. exatamente um processa a fila;
3. ao encerrar o líder, outro assume automaticamente sem reapareamento;
4. nenhuma consulta ambígua é repetida automaticamente durante a troca;
5. o cooldown fiscal permanece global;
6. clientes existentes continuam válidos;
7. se nenhum candidato puder provar posse do A1 correto ou do lock exclusivo, nenhuma chamada SEFAZ é realizada.
