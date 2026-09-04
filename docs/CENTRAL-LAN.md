# Guia operacional da fila compartilhada

> O nome `CENTRAL-LAN.md` é mantido para não quebrar links antigos. A arquitetura atual não usa HTTP pela LAN nem exige um PC Central fixo.

## Arquitetura atual

Cada PC executa sua própria cópia local do NFe Agendamento em `http://127.0.0.1:17345` e todos usam:

```text
P:\01-Nfe agendamento
```

Todos os PCs confiáveis devem ter Windows, acesso de leitura/escrita à pasta e o certificado A1 aplicável instalado/configurado localmente no usuário que executa o app.

Não é necessário liberar a porta 17345 entre computadores, usar mDNS ou acessar o site hospedado por outro PC.

## Requisito do compartilhamento SMB

A pasta deve estar em um compartilhamento SMB normal que preserve locks exclusivos de arquivo. A eleição depende de `FileShare.None` sobre `status\central.lock`.

Não habilite **Offline Files/Arquivos Offline**, cache local de cliente ou outro mecanismo que possa apresentar uma cópia desconectada do compartilhamento como se fosse o estado atual. Se a unidade `P:` ficar indisponível, o comportamento esperado é fail-closed.

Permissões do compartilhamento e NTFS devem limitar leitura/gravação somente aos usuários/PCs confiáveis que participam do grupo.

## Liderança automática

Não existe mais uma Central fixa. Todo PC autorizado é candidato a processar a fila.

O líder é definido pelo lock exclusivo:

```text
P:\01-Nfe agendamento\status\central.lock
```

Somente um processo consegue manter esse arquivo aberto com `FileShare.None`. O vencedor publica `heartbeat.json` e processa a fila; os demais ficam em **standby** e funcionam como clientes.

Se o líder encerrar ou perder o lock, outro candidato tenta assumir automaticamente. Antes de iniciar novo trabalho, o aplicativo revalida o handle do lock; se a validação falhar, abandona a liderança de forma conservadora.

Se existir uma rotação de confiança pendente, o candidato que obtiver o lock precisa concluir a recuperação da rotação antes de ser considerado apto a iniciar trabalho fiscal. Enquanto isso não ocorre, a fila fica fail-closed.

## Estrutura da pasta

```text
P:\01-Nfe agendamento\
├── .nfe-agendamento
├── cache\
├── candidatos\
│   ├── <clientId>.candidate
│   └── <clientId>.transitions
├── fila\
├── pareamento\
├── processando\
├── respostas\
└── status\
    ├── central.lock
    ├── heartbeat.json
    ├── group-identity.bin
    ├── authorized-clients.bin
    ├── fiscal-cooldown.bin
    └── rotation.json
```

Durante uma rotação também podem existir artefatos `.prepared` associados ao identificador da rotação. Eles são parte do mecanismo de recuperação e não devem ser apagados manualmente enquanto `rotation.json` existir.

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

O bootstrap foi endurecido para ser recuperável: a chave de estado do grupo é persistida localmente antes da identidade compartilhada. Se o processo cair entre essas gravações, a próxima inicialização reutiliza a mesma chave em vez de abandonar a identidade em estado parcial.

A configuração antiga `ConfiguredAsCentral` permanece somente para identificar a instalação autorizada a inicializar a migração quando ainda não existe identidade de grupo. Ela não decide mais quem consulta a SEFAZ depois que o grupo existe.

## Identidade, autorização e cache do grupo

A identidade RSA já pareada é preservada durante a migração inicial.

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
5. o novo PC valida/importa o estado seguro do grupo;
6. somente depois disso a autorização retorna sucesso.

O código é de **uso único**: depois de uma autorização concluída ele é consumido. Falhas anteriores à conclusão segura não devem consumir o código indevidamente.

Se houver troca de líder durante o pareamento, gere um novo código no líder atual.

## Gerenciar e revogar PCs autorizados

Na aba **Configurar**, o líder atual pode listar os PCs autorizados e remover um PC que deixou de ser confiável.

A listagem expõe somente identificador, nome, sequência e indicador do PC atual. O segredo criptográfico do cliente nunca é enviado ao navegador.

Ao revogar um PC:

1. o líder prepara uma nova chave de estado do grupo;
2. gera uma nova identidade RSA do grupo;
3. prepara a nova lista de autorizados sem o PC removido;
4. preserva o cooldown fiscal;
5. publica novos bundles apenas para os PCs restantes;
6. publica para cada candidato restante uma cadeia de transição RSA assinada pela identidade anterior;
7. promove o novo estado e purga o cache cifrado com a chave antiga;
8. os PCs restantes importam a nova chave/identidade quando voltarem a operar.

A cadeia de transições permite que um PC autorizado que ficou offline atravesse mais de uma rotação válida, por exemplo A→B→C, sem aceitar uma identidade arbitrária. Uma nova RSA só é aceita se a cadeia puder ser verificada a partir do pin já confiado localmente.

A revogação é recuperável. Se houver queda após a publicação do marcador, um líder autorizado tenta concluir a rotação pendente antes de permitir novo trabalho fiscal.

Revogar um PC é uma operação de segurança e invalida o cache compartilhado antigo porque esse cache foi cifrado com a chave anterior do grupo.

## Consulta normal

Quando o usuário consulta uma NF-e:

- se este PC é o líder e o lock continua saudável, ele executa o fluxo fiscal;
- caso contrário, envia o pedido cifrado pela pasta para o líder atual;
- o líder consulta primeiro o cache compartilhado de 24h antes de considerar uma chamada à SEFAZ.

Mesmo com A1 instalado em todos os PCs, as consultas fiscais automáticas **não** rodam em paralelo entre máquinas. A fila mantém um único líder e a serialização fiscal existente.

## Cooldown e failover

O cooldown de `cStat=656` fica em estado compartilhado cifrado. Trocar de líder não zera o bloqueio fiscal. A rotação de confiança também preserva esse cooldown.

Se um líder cair depois de uma solicitação ter sido autenticada e existir possibilidade de a chamada já ter alcançado a SEFAZ, o sucessor **não repete automaticamente a consulta**. A recuperação devolve falha segura e exige nova ação explícita do usuário.

O cache também é compartilhado: uma NF-e já obtida por um líder pode ser entregue pelo sucessor sem nova ida à SEFAZ enquanto estiver dentro das 24 horas. Após uma revogação/rotação de chave, o cache anterior é purgado deliberadamente.

## Certificado A1 e Portal Nacional

O A1 é uma configuração local de cada PC confiável e não depende de papel fixo de Central.

Após `cStat=656`, **qualquer PC autorizado no grupo** pode iniciar **Baixar pelo Portal**, inclusive quando estiver em standby. O WebView2 e o certificado A1 usados são os daquele próprio PC. O backend exige que a instalação possua o estado de grupo necessário para gravar o XML no cache compartilhado; o hCaptcha permanece manual.

Essa exceção vale somente para o fallback manual do Portal. As chamadas automáticas à SEFAZ continuam exclusivas do líder com lock saudável.

Depois que o Portal baixa o XML oficial, o aplicativo valida o arquivo, grava no cache compartilhado e fecha a janela do WebView2. O site acompanha apenas o cache local por um endpoint que **não consulta a SEFAZ**; assim que o XML aparece, a mesma NF-e é carregada automaticamente na interface sem exigir nova ação do usuário.

## Estados exibidos

Na bandeja use **Status da fila**.

- **Líder automático**: este PC possui o lock e processa a fila;
- **Candidato em espera / Standby**: outro PC possui o lock;
- **Aguardando pasta**: a pasta ou o lock não pôde ser validado;
- **Não autorizado**: o PC ainda precisa ser autorizado por um líder.

Uma rotação pendente também impede o início de novo trabalho fiscal até que a recuperação seja concluída.

Não existem mais botões **Iniciar Central** ou **Parar Central** na operação normal.

## Se a unidade P: cair

O comportamento é fail-closed:

- nenhum candidato novo assume sem acesso à pasta;
- quem perder a validação do lock deixa de iniciar novo trabalho;
- clientes informam indisponibilidade da fila;
- o app não abre portas LAN, não altera firewall e não procura outra pasta automaticamente.

Não use Offline Files para mascarar a indisponibilidade da unidade compartilhada.

## Consulta em lote

O lote usa a mesma fila da consulta individual:

- até 50 chaves únicas;
- uma consulta por vez por instalação;
- líder serializa o acesso fiscal;
- cache compartilhado, deduplicação e cooldown continuam ativos;
- `cStat=656` interrompe o restante do lote;
- cancelar impede o início dos próximos itens.

O cancelamento é conservador: ele impede próximos itens e trabalho ainda não iniciado. Uma operação fiscal que já passou do ponto em que a SEFAZ pode ter sido alcançada não é simplesmente cancelada/repetida, para evitar duplicidade ambígua.

## Iniciar com o Windows

A opção **Iniciar com o Windows** inicia a cópia local. Se o PC estiver autorizado, ele participa automaticamente da eleição; se outro líder já existir, permanece em standby.

O argumento legado `--lan` não habilita exposição HTTP na rede.

## Diagnóstico rápido

Se não houver líder, confira nesta ordem:

1. `P:\01-Nfe agendamento` abre no Explorador;
2. `.nfe-agendamento` existe;
3. o PC está autorizado;
4. se `status\rotation.json` existir, deixe um candidato autorizado concluir a recuperação;
5. `status\heartbeat.json` está sendo atualizado por algum candidato;
6. o A1 está configurado localmente no PC que eventualmente assumir.

Não desative o Firewall do Windows para testar esse fluxo.

## Teste físico recomendado

Após uma release que altere esta arquitetura:

1. confirmar acesso à pasta em pelo menos dois PCs;
2. confirmar A1 configurado nos PCs;
3. confirmar que Offline Files/cache desconectado não está habilitado para a pasta da fila;
4. iniciar dois aplicativos e verificar exatamente um líder;
5. consultar uma NF-e conhecida pelo líder e pelo standby;
6. fechar o líder e confirmar que o standby assume automaticamente;
7. consultar novamente após o failover;
8. consultar uma NF-e, trocar o líder e confirmar retorno pelo mesmo cache sem nova consulta fiscal;
9. reabrir o antigo líder e confirmar que ele fica em standby se outro já possui o lock;
10. validar DANFE e download XML;
11. executar lote pequeno;
12. confirmar que replay e cooldown permanecem compartilhados;
13. autorizar um PC com um código e confirmar que o mesmo código não autoriza um segundo PC;
14. revogar um PC e confirmar que ele deixa de conseguir assumir/usar o novo estado do grupo;
15. deixar um candidato offline durante uma rotação, religá-lo e confirmar importação segura pela cadeia de transições;
16. validar que o Portal pode ser aberto em um standby autorizado, funciona com WebView2/A1 local e que o site carrega automaticamente o XML após o download;
17. confirmar que arquivos fora da árvore dedicada permaneceram intocados.

Não provoque um `cStat=656` real apenas para testar.

## Firewall e modelo de ameaça local

A interface HTTP continua restrita a:

```text
http://127.0.0.1:17345
```

A comunicação entre PCs ocorre pela pasta compartilhada, não por servidor HTTP remoto.

Loopback reduz a superfície de rede, mas não é uma fronteira de segurança entre processos ou usuários Windows do mesmo computador. O projeto assume PCs corporativos confiáveis; malware ou outro usuário local com capacidade de executar processos não deve ser tratado como isolado apenas porque a API está em `127.0.0.1`.
