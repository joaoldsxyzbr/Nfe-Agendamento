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
10. Consulte uma NF-e conhecida.

Ao clicar em **Iniciar Central**, o PC passa a ser marcado localmente como Central. Essa preferência persiste e o aplicativo tenta reassumir a função automaticamente nas próximas inicializações.

### Nos outros PCs

1. Copie/extraia a pasta do NFe Agendamento localmente em cada computador.
2. Execute `NfeAgendamento.App.exe`.
3. Não clique em **Iniciar Central**.
4. Abra **Abrir sistema** ou `http://127.0.0.1:17345`.
5. A configuração de certificado não fica disponível.
6. Faça uma consulta de teste.

Cada PC usa seu próprio servidor local. O cliente não abre o site hospedado pelo PC Central.

## Como identificar o papel do PC

O painel mostra **Papel deste PC**.

### Central configurada

Significa que o usuário clicou em **Iniciar Central** naquele computador e essa escolha foi salva localmente.

Possíveis estados:

- **Central ativa** — lock adquirido, heartbeat sendo publicado e processador disponível;
- **Central aguardando pasta** — `P:\01-Nfe agendamento` não está disponível ou não pode ser usada;
- **Conflito: outra Central ativa** — outro processo já possui o lock exclusivo.

### Cliente

O PC não executa consulta fiscal diretamente. Ele usa a fila compartilhada e mostra:

- **Central online** quando encontra heartbeat recente;
- **Central offline** quando não encontra heartbeat válido.

## Somente uma Central

A Central mantém aberto um lock exclusivo em:

```text
P:\01-Nfe agendamento\status\central.lock
```

O arquivo é usado como mecanismo de exclusão mútua do compartilhamento. O lock depende do handle aberto, não do texto contido no arquivo.

Se outra máquina tentar assumir ao mesmo tempo, ela não derruba a Central existente e não processa a fila.

Quando o processo que possui o lock encerra, o sistema de arquivos libera o handle.

## Heartbeat

Enquanto ativa, a Central atualiza:

```text
P:\01-Nfe agendamento\status\heartbeat.json
```

O heartbeat contém somente dados operacionais necessários, como:

- versão do protocolo;
- identificador do PC Central;
- horário UTC;
- chave pública RSA usada para proteger novas consultas;
- versão do aplicativo.

Não contém:

- certificado A1;
- chave privada;
- XML;
- chave de acesso NF-e;
- CPF/CNPJ;
- senha.

Clientes consideram a Central indisponível quando o heartbeat fica antigo.

## Segurança da fila

O compartilhamento não recebe a NF-e em texto puro.

Para uma consulta:

1. cliente gera um GUID e uma chave AES de 256 bits;
2. chave NF-e é cifrada com AES-GCM;
3. chave AES é cifrada com a chave pública RSA da Central usando OAEP-SHA256;
4. envelope cifrado é publicado em `fila`;
5. Central move o arquivo para `processando` para reivindicá-lo;
6. Central decifra, valida e executa o fluxo fiscal;
7. resposta é cifrada com a chave AES da solicitação;
8. cliente decifra e remove a resposta consumida.

A chave AES pendente fica protegida localmente por DPAPI no cliente. A chave RSA privada da Central fica protegida localmente por DPAPI no Central.

## O que acontece se `P:` cair

### Cliente

A consulta falha de forma controlada com indicação de pasta/Central indisponível.

O aplicativo **não**:

- abre porta HTTP na LAN;
- cria regra de firewall;
- tenta mDNS;
- procura outra pasta no `P:`;
- tenta contornar política corporativa.

### PC Central

Se o PC ainda está configurado como Central mas o compartilhamento fica indisponível, o painel mostra que está aguardando a pasta. Consultas feitas no próprio PC Central podem continuar usando diretamente o fluxo fiscal local enquanto esse PC permanece configurado como Central.

## Parar a Central

Use **Parar Central** quando quiser remover o papel deste PC.

Isso:

- salva `ConfiguredAsCentral = false` localmente;
- libera o lock;
- encerra o processamento da fila;
- impede reassunção automática na próxima inicialização.

Para voltar a ser Central, use **Iniciar Central** novamente.

## Iniciar com o Windows

A opção **Iniciar com o Windows** inicia a cópia local do aplicativo no usuário atual.

Se o PC tiver sido configurado como Central, o serviço tenta reassumir o lock automaticamente. Se for Cliente, continua Cliente.

O argumento legado `--lan` não habilita exposição de rede.

## Diagnóstico rápido

### Cliente mostra “Central offline”

Confira, nesta ordem:

1. se `P:\01-Nfe agendamento` abre no Explorador;
2. se existe `.nfe-agendamento` dentro da pasta;
3. se o PC Central está ligado e com o aplicativo aberto;
4. se o painel do Central mostra **Central ativa**;
5. se `status\heartbeat.json` está sendo atualizado.

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
5. consultar NF-e conhecida no Central;
6. consultar NF-e conhecida em um cliente sem A1;
7. validar DANFE e download XML no cliente;
8. verificar que os envelopes no compartilhamento não mostram XML/chave NF-e em texto puro;
9. reiniciar o PC Central e confirmar reassunção automática;
10. usar **Parar Central**, reiniciar e confirmar que ele não reassume;
11. confirmar que arquivos fora de `P:\01-Nfe agendamento` ficaram intocados.

## Documentos relacionados

- `docs/superpowers/specs/2026-09-02-shared-folder-queue-design.md`
- `docs/superpowers/plans/2026-09-02-shared-folder-queue.md`

A documentação de LAN anterior é histórica e não deve ser usada para operação da arquitetura atual.
