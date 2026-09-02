# NFe Agendamento — fila segura em pasta compartilhada

## Status

Design aprovado em conversa em 2026-09-02 para substituir a comunicação LAN direta entre os PCs.

A implementação deve ocorrer diretamente na `main` depois da revisão deste documento.

## Objetivo

Permitir que os PCs do agendamento usem o NFe Agendamento sem depender de conexão HTTP de entrada no PC central e, portanto, sem depender de regra própria no Firewall do Windows.

O certificado A1, a chave privada, o cache fiscal, o cooldown, a auditoria e toda comunicação com a SEFAZ continuam somente no PC central.

## Decisão arquitetural

Cada PC executa sua própria instância local do NFe Agendamento e abre a interface somente em:

```text
http://127.0.0.1:17345
```

A comunicação entre clientes e Central passa a usar exclusivamente a pasta corporativa já disponível:

```text
P:\01-Nfe agendamento
```

O caminho é deliberadamente fixo. Não haverá configuração para apontar o aplicativo para outra pasta, para a raiz `P:\` ou para uma pasta irmã.

Estrutura permitida:

```text
P:\01-Nfe agendamento\
├── .nfe-agendamento
├── fila\
├── processando\
├── respostas\
└── status\
```

O aplicativo não deve criar, listar, ler, alterar, mover ou apagar qualquer arquivo fora dessa raiz.

## Proteção rígida de caminho

Uma única classe de infraestrutura será responsável por construir todos os caminhos da fila.

Regras obrigatórias:

- raiz compilada no aplicativo: `P:\01-Nfe agendamento`;
- todo caminho final é normalizado com `Path.GetFullPath`;
- todo caminho final precisa permanecer dentro da raiz normalizada;
- nomes de arquivos derivados de requisição usam somente `Guid` gerado pelo aplicativo;
- sequências como `..`, caminhos absolutos recebidos externamente e nomes arbitrários são rejeitados;
- somente os diretórios conhecidos `fila`, `processando`, `respostas` e `status` podem ser usados;
- o app nunca enumera a raiz `P:\`;
- a raiz só é considerada válida quando existe o marcador `.nfe-agendamento` com versão de esquema reconhecida;
- o marcador não contém certificado, chave fiscal, credencial ou segredo reutilizável.

A inicialização da estrutura ocorre somente dentro da raiz fixa.

## Comunicação e segurança dos dados

A pasta compartilhada não deve armazenar chave de acesso ou XML em texto puro.

A Central mantém um par de chaves RSA próprio:

- chave privada: somente no PC central, persistida localmente e protegida por DPAPI;
- chave pública: publicada em `status` para os clientes.

Para cada consulta, o cliente:

1. gera um `requestId` aleatório (`Guid`);
2. gera uma chave AES aleatória exclusiva da requisição;
3. protege essa chave localmente com DPAPI enquanto a requisição estiver pendente;
4. cifra o payload da consulta com AES-GCM;
5. cifra a chave AES com a chave pública RSA da Central usando OAEP-SHA256;
6. grava somente o envelope cifrado em `fila`.

A Central:

1. reivindica o arquivo movendo-o de `fila` para `processando`;
2. recupera a chave AES com sua chave privada;
3. decifra e valida a chave NF-e;
4. passa a consulta pelo fluxo fiscal existente;
5. cifra o resultado com a mesma chave AES;
6. publica a resposta em `respostas`.

O cliente decifra a resposta localmente e remove os artefatos da requisição após o consumo.

Assim, uma pessoa que tenha acesso genérico ao compartilhamento não consegue ler diretamente a chave NF-e nem o XML apenas abrindo os arquivos da fila.

## Escrita atômica

Nenhum consumidor deve ler arquivo parcialmente gravado.

Toda publicação segue:

1. escrever arquivo temporário com nome aleatório no mesmo diretório;
2. fechar e liberar o arquivo;
3. renomear/mover para o nome definitivo.

O processador nunca consome arquivos temporários.

## Papel da Central

O estado existente de **Central ativa** passa a significar que aquela instância é a processadora da pasta compartilhada.

Somente uma Central pode ficar ativa por vez.

Ao ativar, o aplicativo mantém um lock exclusivo em arquivo dentro de `status`. O compartilhamento SMB mantém o lock enquanto o processo está vivo. Se outra máquina tentar ativar a Central enquanto o lock estiver em uso, a ativação é recusada com mensagem clara.

Se o processo central cair, o lock é liberado pelo sistema e outra inicialização pode assumir a função.

O PC central continua podendo consultar NF-e localmente mesmo se a unidade `P:` ficar temporariamente indisponível. Consultas feitas no próprio PC central usam diretamente o serviço fiscal existente.

## Papel dos clientes

Nos demais PCs:

- o servidor web escuta somente em loopback;
- `/api/nfe/lookup` valida a entrada e cria uma requisição na pasta compartilhada;
- o endpoint aguarda a resposta da Central por polling com timeout controlado;
- nenhum cliente acessa Certificate Store para executar consulta fiscal;
- nenhum cliente recebe ou armazena a chave privada do certificado A1;
- a interface informa `Central offline` quando não existir heartbeat recente;
- configuração de certificado fica indisponível fora do PC central.

A interface web continua usando o mesmo endpoint local. A diferença entre execução direta e envio pela fila fica escondida no backend.

## Heartbeat e diagnóstico

Enquanto ativa, a Central atualiza periodicamente um arquivo em `status` contendo apenas dados operacionais mínimos:

- versão do esquema da fila;
- identificador da Central;
- horário UTC do último heartbeat;
- chave pública necessária para cifrar novas requisições;
- versão do aplicativo/protocolo.

Não entram no heartbeat:

- certificado;
- chave privada;
- senha;
- XML;
- chave de acesso NF-e;
- CNPJ/CPF.

O painel Windows passa a diagnosticar:

- pasta compartilhada disponível;
- marcador válido;
- permissão de leitura/escrita dentro da pasta dedicada;
- lock da Central;
- heartbeat;
- processador da fila.

O diagnóstico de Firewall deixa de ser requisito para uso entre PCs.

## Polling

Não será usado `FileSystemWatcher` como mecanismo principal em unidade de rede.

O processador central verifica `fila` em intervalo curto e controlado. O cliente também verifica sua resposta em intervalo curto até receber resultado ou atingir timeout.

Isso evita depender de notificações de filesystem que podem se perder ou se comportar de forma diferente em compartilhamentos SMB.

## Recuperação de falhas

- arquivo inválido ou envelope que não possa ser autenticado não é enviado à SEFAZ;
- resposta AES-GCM com autenticação inválida é rejeitada;
- requisições antigas em `processando` podem ser recuperadas na inicialização da Central depois de um limite de tempo;
- arquivos temporários órfãos e respostas expiradas são limpos por política de retenção;
- falha de rede no `P:` retorna erro operacional, sem fallback para abrir porta LAN;
- timeout do cliente não cancela uma operação fiscal que a Central já começou;
- o fluxo existente de deduplicação, fila fiscal, cooldown `656` e cache continua sendo a autoridade antes de qualquer chamada à SEFAZ.

## Retenção

A pasta compartilhada é transporte, não arquivo histórico.

- respostas consumidas são removidas pelo cliente;
- envelopes concluídos não são mantidos em `concluidos`;
- arquivos expirados são removidos automaticamente;
- auditoria fiscal permanece no armazenamento local protegido da Central.

Isso reduz exposição de dados e evita acumular XML no compartilhamento.

## Mudança no modo LAN atual

A comunicação HTTP direta entre PCs deixa de ser o caminho operacional.

Após a migração:

- porta `17345` continua existindo apenas para a interface local de cada PC;
- o servidor deve bindar somente em loopback;
- `nfeagendamento.local`, IPv4 compartilhável e abertura automática de firewall deixam de ser necessários para operação multi-PC;
- o código legado pode ser removido quando os testes da nova fila cobrirem o fluxo equivalente.

Não haverá fallback silencioso para LAN. Se a pasta compartilhada estiver indisponível, o cliente mostra o problema em vez de tentar contornar políticas de rede da empresa.

## Compatibilidade do fluxo fiscal

A Central continua usando sem alteração conceitual:

- `NfeLookupService`;
- `FiscalRequestCoordinator` para deduplicação;
- `FiscalOperationGate` para serialização/limite;
- `FiscalCooldownStore` para `656`;
- `EncryptedXmlCache`;
- `FiscalAuditLog`;
- `CertificateService` e o A1 do Windows.

A fila compartilhada fica antes desse fluxo e serve apenas como transporte entre PCs.

## Testes obrigatórios

### Caminhos

- aceita somente `P:\01-Nfe agendamento` e subdiretórios conhecidos;
- rejeita `..`;
- rejeita caminho absoluto injetado;
- rejeita arquivo fora da raiz;
- não enumera nem altera `P:\`;
- marcador inválido impede operação.

### Criptografia

- envelope não contém chave NF-e em texto puro;
- envelope não contém XML em texto puro;
- adulteração de ciphertext/tag falha;
- chave AES não pode ser recuperada sem a chave privada da Central;
- resposta válida pode ser decifrada apenas pelo cliente que criou a requisição.

### Concorrência

- duas requisições recebem IDs independentes;
- somente uma Central adquire o lock;
- reivindicação por move impede processamento duplo do mesmo arquivo;
- reinicialização recupera item antigo em `processando` sem duplicar chamada fiscal já concluída quando houver resposta publicada.

### Integração

- cliente local envia pedido e recebe resposta através de diretório temporário usado como share de teste;
- Central continua usando a deduplicação e a fila fiscal existentes;
- cliente sem certificado instalado consegue concluir consulta através da Central simulada;
- PC central continua consultando localmente quando o share está offline;
- nenhum teste de CI depende do `P:` real, certificado real ou SEFAZ real.

### Rede

- configuração do servidor local não aceita bind em `0.0.0.0` no modo normal;
- uso multi-PC não depende de regra de firewall;
- bootstrap não divulga certificado, XML ou chave fiscal.

## Critérios de aceitação física

Na empresa, após a release:

1. os três PCs conseguem abrir `P:\01-Nfe agendamento`;
2. cada PC abre sua própria interface em `127.0.0.1:17345`;
3. somente o PC com certificado é ativado como Central;
4. os outros PCs exibem Central online;
5. um cliente sem certificado consulta uma NF-e conhecida;
6. XML/DANFE chegam ao cliente sem ficarem legíveis em texto puro na pasta compartilhada;
7. desligar a Central faz os clientes exibirem Central offline;
8. bloquear acesso ao `P:` produz erro de compartilhamento, sem tentativa de abrir firewall;
9. arquivos fora de `P:\01-Nfe agendamento` permanecem intocados.

## Fora do escopo

- tentar desativar, burlar ou reconfigurar políticas corporativas de firewall;
- publicar o serviço na internet;
- armazenar certificado A1 na pasta compartilhada;
- copiar a chave privada para clientes;
- usar a pasta como backup de XML;
- acessar qualquer outra área do `P:`;
- consulta em lote.
