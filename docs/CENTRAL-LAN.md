# Guia operacional da fila compartilhada

> O nome `CENTRAL-LAN.md` é mantido para não quebrar links antigos. A arquitetura atual não usa HTTP pela LAN nem exige um PC Central fixo.

## Arquitetura atual

Cada PC executa sua própria cópia local do NFe Agendamento em `http://127.0.0.1:17345` e todos usam:

```text
P:\01-Nfe agendamento
```

Todos os PCs confiáveis devem ter Windows, acesso de leitura/escrita à pasta e o certificado A1 aplicável instalado/configurado localmente no usuário que executa o app.

Não é necessário liberar a porta 17345 entre computadores, usar mDNS ou acessar o site hospedado por outro PC.

## Liderança automática

Não existe mais uma Central fixa. Todo PC autorizado é candidato a processar a fila.

O líder é definido pelo lock exclusivo:

```text
P:\01-Nfe agendamento\status\central.lock
```

Somente um processo consegue manter esse arquivo aberto com `FileShare.None`. O vencedor publica `heartbeat.json` e processa a fila; os demais ficam em **standby** e funcionam como clientes.

Se o líder encerrar ou perder o lock, outro candidato tenta assumir automaticamente. Antes de iniciar novo trabalho, o aplicativo revalida o handle do lock; se a validação falhar, abandona a liderança de forma conservadora.

## Estrutura da pasta

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

A raiz precisa existir previamente. O aplicativo confina suas operações a essa árvore e rejeita reparse points operacionais.

## Migração da arquitetura antiga

Na primeira atualização para a liderança automática:

1. atualize o aplicativo nos PCs;
2. abra **primeiro o PC que era a Central antiga**;
3. mantenha `P:\01-Nfe agendamento` acessível;
4. o aplicativo migra uma única vez a identidade RSA da fila e os clientes já autorizados;
5. abra os demais PCs já pareados; eles importam automaticamente seus pacotes de candidatura;
6. confirme que um PC aparece como líder e os demais como standby;
7. depois disso o antigo PC Central pode ser desligado sem tornar a fila dependente dele.

A configuração antiga `ConfiguredAsCentral` permanece somente para identificar a instalação autorizada a inicializar a migração quando ainda não existe identidade de grupo. Ela não decide mais quem consulta a SEFAZ depois que o grupo existe.

## Identidade, autorização e cache do grupo

A identidade RSA já pareada é preservada, portanto a chave pública da fila não muda durante a migração.

A chave privada da fila fica cifrada na pasta compartilhada por uma chave de estado do grupo. Cada candidato guarda essa chave localmente protegida por DPAPI. Clientes existentes recebem um pacote individual cifrado/autenticado com o segredo do próprio pareamento.

A lista de clientes autorizados e o `LastSequence` ficam em estado compartilhado cifrado. Assim, após troca de líder, o sucessor mantém a autorização e continua bloqueando replay.

O diretório `cache\` guarda os XMLs localizados por até 24 horas. O conteúdo é cifrado com AES-GCM usando a chave do grupo e os nomes dos arquivos são derivados por SHA-256 da chave NF-e. Portanto outro líder autorizado consegue reutilizar o mesmo XML depois de um failover sem fazer nova consulta desnecessária à SEFAZ.

Nenhum PFX, chave privada do A1 ou senha de certificado é copiado para a pasta compartilhada.

## Autorizar um PC novo

O código temporário só pode ser gerado no PC que estiver como líder naquele momento.

1. no líder atual, abra **Configurar**;
2. clique em **Gerar código de autorização**;
3. no PC novo, informe o código em **Autorizar este PC**;
4. o líder registra o cliente no estado compartilhado e publica seu pacote de candidatura;
5. o novo PC passa a funcionar como cliente e também pode assumir a liderança no futuro.

## Consulta normal

Quando o usuário consulta uma NF-e:

- se este PC é o líder e o lock continua saudável, ele executa o fluxo fiscal;
- caso contrário, envia o pedido cifrado pela pasta para o líder atual;
- o líder consulta primeiro o cache compartilhado de 24h antes de considerar uma chamada à SEFAZ.

Mesmo com A1 instalado em todos os PCs, as consultas fiscais **não** rodam em paralelo entre máquinas. A fila mantém um único líder e a serialização fiscal existente.

## Cooldown e failover

O cooldown de `cStat=656` fica em estado compartilhado cifrado. Trocar de líder não zera o bloqueio fiscal.

Se um líder cair depois de uma solicitação ter sido autenticada e existir possibilidade de a chamada já ter alcançado a SEFAZ, o sucessor **não repete automaticamente a consulta**. A recuperação devolve falha segura e exige nova ação explícita do usuário.

O cache também é compartilhado: uma NF-e já obtida por um líder pode ser entregue pelo sucessor sem nova ida à SEFAZ enquanto estiver dentro das 24 horas.

## Certificado A1 e Portal Nacional

O A1 é uma configuração local de cada PC confiável e não depende de papel fixo de Central.

O fallback pelo Portal Nacional só pode ser iniciado no **líder atual com lock saudável**. Após `cStat=656`, o site local exibe **Baixar pelo Portal**; o backend continua rejeitando a operação se o lock não estiver válido. O hCaptcha permanece manual.

Depois que o Portal baixa o XML oficial, o aplicativo valida o arquivo, grava no cache compartilhado e fecha a janela do WebView2. O site acompanha apenas o cache local por um endpoint que **não consulta a SEFAZ**; assim que o XML aparece, a mesma NF-e é carregada automaticamente na interface sem exigir nova ação do usuário.

## Estados exibidos

Na bandeja use **Status da fila**.

- **Líder automático**: este PC possui o lock e processa a fila;
- **Candidato em espera / Standby**: outro PC possui o lock;
- **Aguardando pasta**: a pasta ou o lock não pôde ser validado;
- **Não autorizado**: o PC ainda precisa ser autorizado por um líder.

Não existem mais botões **Iniciar Central** ou **Parar Central** na operação normal.

## Se a unidade P: cair

O comportamento é fail-closed:

- nenhum candidato novo assume sem acesso à pasta;
- quem perder a validação do lock deixa de iniciar novo trabalho;
- clientes informam indisponibilidade da fila;
- o app não abre portas LAN, não altera firewall e não procura outra pasta automaticamente.

## Consulta em lote

O lote usa a mesma fila da consulta individual:

- até 50 chaves únicas;
- uma consulta por vez por instalação;
- líder serializa o acesso fiscal;
- cache compartilhado, deduplicação e cooldown continuam ativos;
- `cStat=656` interrompe o restante do lote;
- cancelar impede o início dos próximos itens.

## Iniciar com o Windows

A opção **Iniciar com o Windows** inicia a cópia local. Se o PC estiver autorizado, ele participa automaticamente da eleição; se outro líder já existir, permanece em standby.

O argumento legado `--lan` não habilita exposição HTTP na rede.

## Diagnóstico rápido

Se não houver líder, confira nesta ordem:

1. `P:\01-Nfe agendamento` abre no Explorador;
2. `.nfe-agendamento` existe;
3. o PC está autorizado;
4. `status\heartbeat.json` está sendo atualizado por algum candidato;
5. o A1 está configurado localmente no PC que eventualmente assumir.

Não desative o Firewall do Windows para testar esse fluxo.

## Teste físico recomendado

Após uma release que altere esta arquitetura:

1. confirmar acesso à pasta em pelo menos dois PCs;
2. confirmar A1 configurado nos PCs;
3. iniciar dois aplicativos e verificar exatamente um líder;
4. consultar uma NF-e conhecida pelo líder e pelo standby;
5. fechar o líder e confirmar que o standby assume automaticamente;
6. consultar novamente após o failover;
7. consultar uma NF-e, trocar o líder e confirmar retorno pelo mesmo cache sem nova consulta fiscal;
8. reabrir o antigo líder e confirmar que ele fica em standby se outro já possui o lock;
9. validar DANFE e download XML;
10. executar lote pequeno;
11. confirmar que replay e cooldown permanecem compartilhados;
12. validar que o Portal aparece somente no líder, funciona com WebView2/A1 real e que o site carrega automaticamente o XML após o download;
13. confirmar que arquivos fora da árvore dedicada permaneceram intocados.

Não provoque um `cStat=656` real apenas para testar.

## Firewall

A interface HTTP continua restrita a:

```text
http://127.0.0.1:17345
```

A comunicação entre PCs ocorre pela pasta compartilhada, não por servidor HTTP remoto.
