# Guia operacional da Central

> O nome deste arquivo é mantido para não quebrar links antigos. A arquitetura HTTP LAN foi substituída pela fila segura em pasta compartilhada.

## Pré-requisitos

No PC Central:

- Windows;
- certificado A1 instalado no usuário que executará o app;
- acesso de leitura/escrita a `P:\01-Nfe agendamento`.

Nos PCs cliente:

- Windows;
- cópia local do aplicativo;
- acesso de leitura/escrita a `P:\01-Nfe agendamento`.

Não é necessário liberar TCP `17345` entre computadores, configurar mDNS ou usar `nfeagendamento.local`.

## Pasta usada pelo aplicativo

A raiz é fixa:

```text
P:\01-Nfe agendamento
```

Ela **precisa existir antes** de a Central ser ativada. O aplicativo não cria a raiz e não acessa a raiz `P:\`.

Quando o PC Central é ativado, somente os itens abaixo podem ser criados dentro dela:

```text
P:\01-Nfe agendamento\
├── .nfe-agendamento
├── fila\
├── pareamento\
├── processando\
├── respostas\
└── status\
```

O restante do compartilhamento corporativo fica fora do escopo do aplicativo.

## Primeira configuração

### No PC que possui o A1

1. Copie/extraia a pasta do NFe Agendamento localmente.
2. Execute `NfeAgendamento.App.exe`.
3. Abra a janela **Central NFe Agendamento**.
4. Confirme que `P:\01-Nfe agendamento` está acessível.
5. Clique em **Iniciar Central**.
6. Aguarde o painel mostrar **Central ativa** e **Processador: Ativo**.
7. Clique em **Abrir sistema**.
8. Selecione o certificado A1 e a UF autora.
9. Salve.
10. Consulte uma NF-e conhecida no próprio Central.

Ao clicar em **Iniciar Central**, o PC passa a ser marcado localmente como Central. Essa preferência persiste e o aplicativo tenta reassumir a função automaticamente nas próximas inicializações.

### Parear cada PC cliente

Com a Central ativa:

1. no sistema local da Central, localize **Conectar PCs**;
2. clique em **Gerar código de pareamento**;
3. use o código temporário nos PCs clientes dentro de aproximadamente 10 minutos;
4. em cada cliente, abra o sistema local, informe o código e clique em **Parear com a Central**;
5. aguarde a confirmação de pareamento antes da primeira consulta.

O pareamento é persistido localmente. Não é necessário repetir a cada consulta ou reinicialização. Gere outro código somente para um cliente novo ou quando for necessário refazer o vínculo.

### Nos outros PCs

1. Copie/extraia a pasta do NFe Agendamento localmente em cada computador.
2. Execute `NfeAgendamento.App.exe`.
3. Não clique em **Iniciar Central**.
4. Abra **Abrir sistema** ou `http://127.0.0.1:17345`.
5. A configuração de certificado não fica disponível.
6. Faça o pareamento inicial.
7. Faça uma consulta de teste.

Cada PC usa seu próprio servidor local. O cliente não abre o site hospedado pelo PC Central.

## Como identificar o papel do PC

O painel mostra **Papel deste PC**.

### Central configurada

Significa que o usuário clicou em **Iniciar Central** naquele computador e essa escolha foi salva localmente.

Possíveis estados:

- **Central ativa** — lock adquirido, heartbeat assinado sendo publicado e processador disponível;
- **Central aguardando pasta** — `P:\01-Nfe agendamento` não está disponível ou não pode ser usada;
- **Conflito: outra Central ativa** — outro processo já possui o lock exclusivo.

Um PC configurado como Central só executa consulta fiscal quando a Central está realmente ativa com o lock adquirido.

### Cliente

O PC não executa consulta fiscal diretamente. Ele usa a fila compartilhada e pode estar em três situações:

- **não pareado** — precisa receber um código temporário da Central;
- **Central online** — pareamento válido e heartbeat recente/autenticado;
- **Central offline** — heartbeat ausente, antigo ou inválido.

## Somente uma Central

A Central mantém aberto um lock exclusivo em:

```text
P:\01-Nfe agendamento\status\central.lock
```

O arquivo é usado como mecanismo de exclusão mútua do compartilhamento. O lock depende do handle aberto, não do texto contido no arquivo.

Se outra máquina tentar assumir ao mesmo tempo, ela não derruba a Central existente e não processa a fila.

Quando o processo que possui o lock encerra, o sistema de arquivos libera o handle.

## Heartbeat autenticado

Enquanto ativa, a Central atualiza:

```text
P:\01-Nfe agendamento\status\heartbeat.json
```

O heartbeat contém somente dados operacionais necessários, como:

- versão do protocolo;
- identificador do PC Central;
- horário UTC;
- chave pública RSA;
- versão do aplicativo;
- assinatura digital.

Não contém certificado A1, chave privada, XML, chave de acesso NF-e, CPF/CNPJ ou segredo do cliente.

Durante o pareamento, o cliente fixa localmente a chave pública da Central. Depois disso, ele não confia em uma chave diferente apenas porque apareceu no compartilhamento: compara a chave com a que foi pareada e valida a assinatura do heartbeat.

Clientes consideram a Central indisponível quando o heartbeat fica antigo ou não é autenticado corretamente.

## Segurança do pareamento

O diretório `pareamento` funciona somente como transporte temporário para autorizar um PC cliente.

1. a Central gera um código aleatório temporário;
2. o código deriva uma chave de pareamento;
3. o cliente envia sua identidade em um envelope AES-GCM;
4. a Central só abre o pedido enquanto o código estiver ativo;
5. a Central cria um segredo exclusivo para o cliente e devolve a identidade/chave pública da Central em resposta cifrada;
6. o cliente guarda segredo e chave pública localmente com DPAPI;
7. a Central guarda a lista de clientes autorizados localmente com DPAPI.

Arquivos inválidos na pasta de pareamento são descartados e não conseguem bloquear clientes legítimos.

## Segurança da fila

O compartilhamento não recebe a NF-e em texto puro.

Para uma consulta:

1. cliente reserva uma sequência monotônica da sua identidade pareada;
2. gera um GUID e uma chave AES de 256 bits;
3. chave NF-e é cifrada com AES-GCM;
4. chave AES é cifrada com a chave pública RSA pareada da Central usando OAEP-SHA256;
5. o envelope recebe HMAC com o segredo exclusivo do cliente;
6. envelope cifrado e autenticado é publicado em `fila`;
7. Central move o arquivo para `processando` para reivindicá-lo;
8. Central valida cliente, HMAC, sequência, tamanho, horário e payload;
9. somente então executa o fluxo fiscal;
10. resposta é cifrada com a chave AES da solicitação;
11. cliente decifra e remove a resposta consumida.

A sequência precisa ser maior que a última aceita pela Central, bloqueando replay/repetição de um envelope antigo.

A chave AES pendente fica protegida localmente por DPAPI no cliente. A chave RSA privada da Central fica protegida localmente por DPAPI no Central.

## Consulta em lote

O lote usa a mesma consulta individual e a mesma fila segura; não existe um serviço fiscal separado para lote.

### Como usar

1. abra o sistema local no Central ou em um cliente já pareado;
2. localize **Consulta em lote**;
3. cole uma chave de 44 dígitos por linha;
4. confira o contador de válidas, duplicadas e inválidas;
5. clique em **Iniciar lote**;
6. acompanhe cada linha como **Aguardando**, **Consultando**, **Concluída** ou erro correspondente;
7. nas linhas concluídas, use **Ver DANFE** ou **Baixar XML**.

São aceitas no máximo **50 chaves únicas por lote**. Duplicatas no texto são removidas antes da execução e entradas inválidas não são enviadas ao backend.

### Concorrência

Cada instalação executa apenas **uma NF-e do lote por vez**. O lote não usa chamadas fiscais paralelas.

Se PC 2 e PC 3 iniciarem lotes ao mesmo tempo, ambos enviam pedidos individuais pela fila já existente. O PC Central continua aplicando a serialização fiscal, deduplicação e cache normalmente.

### Fila ocupada

Quando a Central responder `fila_ocupada`, o lote respeita `Retry-After` e repete somente aquela NF-e, com quantidade limitada de tentativas. Se continuar ocupada, a linha recebe erro e o lote segue.

### `cStat=656`

Quando a SEFAZ indicar consumo indevido (`cStat=656`):

- o item atual é marcado como bloqueado;
- o restante do lote é interrompido;
- itens ainda não iniciados são marcados como não processados por cooldown;
- nenhuma repetição automática é feita para tentar furar o bloqueio.

Não provoque `656` real para testar o lote.

### Cancelamento

**Cancelar lote** interrompe a requisição local atual quando possível e impede o início dos próximos itens. Um pedido que a Central já tenha reivindicado segue as regras normais da fila segura.

Os XMLs retornados pelo lote ficam somente em memória enquanto a página estiver aberta. O lote não cria histórico persistente, não usa `localStorage`/IndexedDB e não cria uma área adicional no `P:`.

## O que acontece se `P:` cair

### Cliente

A consulta falha de forma controlada com indicação de pasta/Central indisponível.

O aplicativo **não** abre porta HTTP na LAN, cria regra de firewall, tenta mDNS, procura outra pasta no `P:` ou tenta contornar política corporativa.

### PC Central

Se o PC ainda está configurado como Central mas o compartilhamento fica indisponível, o painel mostra que está aguardando a pasta. Enquanto não adquirir o lock e ficar ativo, **não executa consulta fiscal direta**, mesmo no próprio PC.

## Parar a Central

Use **Parar Central** quando quiser remover o papel deste PC.

Isso:

- salva `ConfiguredAsCentral = false` localmente;
- libera o lock;
- remove o heartbeat publicado por essa instância;
- encerra o processamento da fila;
- impede reassunção automática na próxima inicialização.

Para voltar a ser Central, use **Iniciar Central** novamente.

## Iniciar com o Windows

A opção **Iniciar com o Windows** inicia a cópia local do aplicativo no usuário atual.

Se o PC tiver sido configurado como Central, o serviço tenta reassumir o lock automaticamente. Se for Cliente, continua Cliente.

O argumento legado `--lan` não habilita exposição de rede.

## Diagnóstico rápido

### Cliente mostra que não está pareado

1. confirme que `P:\01-Nfe agendamento` está acessível;
2. confirme que a Central está ativa;
3. gere um novo código em **Conectar PCs** no Central;
4. informe o código no cliente e faça o pareamento novamente.

### Cliente mostra “Central offline”

Confira, nesta ordem:

1. se `P:\01-Nfe agendamento` abre no Explorador;
2. se existe `.nfe-agendamento` dentro da pasta;
3. se o PC cliente está pareado;
4. se o PC Central está ligado e com o aplicativo aberto;
5. se o painel do Central mostra **Central ativa**;
6. se `status\heartbeat.json` está sendo atualizado.

Não desative o Firewall do Windows para testar esse fluxo.

### Central mostra “aguardando pasta”

Confirme se a unidade `P:` está mapeada no mesmo usuário do Windows que executa o NFe Agendamento e se a pasta dedicada existe.

### Central mostra conflito

Existe outro processo com o lock. Identifique qual PC está atuando como Central antes de interromper qualquer coisa.

## Arquivos antigos e retenção

A pasta é transporte, não arquivo permanente.

- respostas consumidas são apagadas pelo cliente;
- arquivos temporários são ignorados durante publicação;
- temporários antigos são limpos;
- respostas expiradas são limpas;
- arquivos inválidos não bloqueiam a fila nem o pareamento;
- item antigo em `processando` pode ser recuperado;
- se já existe resposta daquele pedido, a recuperação não repete a chamada fiscal.

## Firewall

A operação multi-PC atual não exige regra de entrada criada pelo NFe Agendamento.

Cada aplicativo aceita HTTP somente em loopback:

```text
http://127.0.0.1:17345
```

Uma conexão de outro computador à porta 17345 deve ser rejeitada pelo próprio aplicativo/servidor e não faz parte da arquitetura suportada.

## Teste físico recomendado após cada release relevante

1. abrir `P:\01-Nfe agendamento` nos três PCs;
2. executar uma cópia local do app em cada PC;
3. ativar Central somente no PC com A1;
4. confirmar heartbeat/processador no painel;
5. configurar/validar o certificado e consultar NF-e conhecida no Central;
6. gerar um código de pareamento no Central;
7. parear PC 2 e PC 3;
8. consultar NF-e conhecida em um cliente sem A1;
9. validar DANFE e download XML no cliente;
10. executar lote com 3 chaves no Central;
11. executar lote com 3 chaves em um cliente sem A1;
12. testar uma chave duplicada e confirmar execução única;
13. iniciar lotes em dois PCs e confirmar que a Central continua serializando as consultas;
14. testar **Cancelar lote** sem provocar bloqueio real da SEFAZ;
15. validar DANFE e download XML de uma linha concluída do lote;
16. verificar que os envelopes no compartilhamento não mostram XML/chave NF-e em texto puro;
17. reiniciar o PC Central e confirmar reassunção automática;
18. usar **Parar Central**, confirmar remoção do heartbeat, reiniciar e confirmar que ele não reassume;
19. confirmar que arquivos fora de `P:\01-Nfe agendamento` ficaram intocados.

## Documentos relacionados

- `README.md`
- `docs/ATUALIZACAO-E-INICIALIZACAO.md`
- `docs/superpowers/specs/2026-09-02-shared-folder-queue-design.md`
- `docs/superpowers/plans/2026-09-02-shared-folder-queue.md`
- `docs/superpowers/specs/2026-09-02-batch-lookup-design.md`
- `docs/superpowers/plans/2026-09-02-batch-lookup.md`

A documentação de LAN anterior é histórica e não deve ser usada para operação da arquitetura atual.
