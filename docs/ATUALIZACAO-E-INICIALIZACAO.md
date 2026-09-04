# Inicialização e atualização do NFe Agendamento

## Arquitetura atual

Cada PC executa sua própria cópia do `NfeAgendamento.App.exe`. A interface fica somente em:

```text
http://127.0.0.1:17345
```

A comunicação entre máquinas usa exclusivamente:

```text
P:\01-Nfe agendamento
```

Não existe servidor HTTP exposto na LAN, mDNS nem configuração automática de firewall.

Todos os PCs confiáveis que poderão assumir a fila devem possuir acesso à pasta compartilhada e o certificado A1 aplicável instalado/configurado localmente.

A pasta compartilhada deve ser SMB normal, com locks exclusivos preservados. Não use Offline Files/Arquivos Offline ou cache desconectado para a pasta da fila.

## Inicialização normal

O executável oficial é:

```text
NfeAgendamento.App.exe
```

O argumento legado `--lan` pode existir em atalhos antigos, mas é ignorado e nunca expõe a porta 17345 para outros computadores.

Depois que o PC está autorizado no grupo, ele participa automaticamente da eleição da fila ao iniciar:

- se conseguir `status\central.lock`, vira candidato a líder;
- se outro PC já possuir o lock, fica em standby;
- se a pasta estiver indisponível, não inicia trabalho fiscal;
- se houver `status\rotation.json`, a rotação pendente precisa ser concluída antes de qualquer trabalho fiscal novo.

Não existem mais comandos operacionais **Iniciar Central** ou **Parar Central**.

## Migração da Central fixa para liderança automática

A configuração antiga `ConfiguredAsCentral` é mantida apenas para o bootstrap de compatibilidade.

Na primeira atualização para a arquitetura automática:

1. atualize os arquivos do aplicativo;
2. abra primeiro a instalação que era a Central antiga;
3. confirme acesso a `P:\01-Nfe agendamento`;
4. mantenha o aplicativo aberto até a identidade do grupo ser criada;
5. abra os PCs já pareados;
6. eles importam automaticamente o pacote de candidatura vinculado à chave pública já conhecida;
7. confirme no **Status da fila** que existe exatamente um líder e os demais estão em standby;
8. teste desligando o líder e confirmando a tomada automática por outro PC.

O bootstrap é recuperável: a chave local protegida por DPAPI é persistida antes da identidade compartilhada. Uma queda entre essas duas etapas não deve obrigar recriação manual do grupo; a próxima inicialização reutiliza a chave já preparada.

A migração preserva a identidade RSA da fila e o estado de clientes/replay. Não é necessário reaparear todos os PCs.

O antigo PC Central também ganha uma identidade de cliente durante a migração, portanto continua conseguindo consultar quando estiver em standby.

## Autorizar um PC novo

O código temporário é gerado somente no líder atual.

1. abra **Configurar** no PC líder;
2. clique em **Gerar código de autorização**;
3. no PC novo, abra o sistema local;
4. informe o código e clique em **Autorizar este PC**;
5. o novo PC recebe seu vínculo local e o pacote que o torna futuro candidato a líder;
6. a autorização só retorna sucesso depois de validar/importar o estado seguro do grupo.

O código de autorização é de uso único depois de uma autorização concluída. Se ocorrer troca de líder durante o fluxo, gere um novo código no líder atual.

O estado local fica protegido por DPAPI e não precisa ser recriado a cada inicialização.

## Revogar um PC autorizado

O líder atual pode listar os PCs autorizados na aba **Configurar** e remover um PC que deixou de ser confiável.

A revogação não é apenas uma remoção de nome. Ela executa rotação criptográfica do grupo:

1. nova chave de estado do grupo;
2. nova identidade RSA;
3. nova lista de autorizados sem o PC removido;
4. cooldown fiscal preservado;
5. novos bundles apenas para os candidatos restantes;
6. cadeia de transição RSA assinada para permitir que candidatos offline validem a nova identidade a partir do pin anterior;
7. purge do cache cifrado com a chave antiga;
8. promoção do novo estado.

Se houver queda durante essa operação, o marcador `status\rotation.json` mantém a rotação recuperável. O próximo candidato autorizado que conseguir o lock deve concluir a rotação antes de iniciar novo trabalho fiscal.

Não apague manualmente `rotation.json`, arquivos `.prepared` ou sidecars `.transitions` durante uma recuperação.

## Certificado A1

O certificado é configurado localmente em cada PC confiável. Ele não é copiado pela fila e não faz parte do pacote de candidatura.

Antes de depender de um PC como possível líder ou usar o fallback pelo Portal nele, confirme:

1. A1 instalado no usuário correto;
2. certificado selecionado no NFe Agendamento;
3. UF autora configurada;
4. acesso à pasta compartilhada e autorização do PC no grupo.

## Cache fiscal compartilhado

O cache operacional de XML fica em:

```text
P:\01-Nfe agendamento\cache
```

Os XMLs são cifrados com AES-GCM usando a chave do grupo e têm retenção de 24 horas. A chave NF-e e o XML não aparecem em texto puro no compartilhamento.

Isso permite que um PC assuma a liderança e reutilize o XML obtido pelo líder anterior, evitando uma nova consulta desnecessária à SEFAZ após failover.

Após revogação/rotação da chave do grupo, o cache antigo é purgado deliberadamente porque foi cifrado com material que o PC revogado já possuía.

## Portal Nacional

Após `cStat=656`, **Baixar pelo Portal** pode ser iniciado em qualquer PC autorizado no grupo, inclusive quando ele está em standby. O WebView2 e o certificado A1 usados são locais daquele PC; o hCaptcha continua manual.

O fallback manual do Portal não exige `central.lock`, mas o PC precisa ter o estado de grupo necessário para gravar o XML no cache compartilhado. As consultas automáticas à SEFAZ continuam exclusivas do líder e permanecem protegidas pelo fencing fiscal.

O XML oficial baixado e validado entra no mesmo cache compartilhado de 24 horas. Assim que aparece no cache, a interface local carrega a NF-e automaticamente sem polling fiscal.

## Iniciar com o Windows

A opção **Iniciar com o Windows** registra o executável no perfil atual em:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

Quando o Windows inicia, o PC autorizado entra automaticamente na eleição. Se outro líder estiver ativo, permanece em standby.

Se o executável for movido de pasta, desmarque e marque novamente a opção para atualizar o caminho.

## Atualizador integrado

A ação **Verificar atualização** fica no menu da bandeja.

O atualizador aceita somente pacote oficial verificável:

- release oficial do projeto;
- versão maior que a instalada;
- asset `Nfe-Agendamento-win-x64.zip`;
- bundle de assinatura `Nfe-Agendamento-win-x64.zip.sigstore.json`;
- HTTPS com host oficial esperado;
- tamanho dentro dos limites;
- digest SHA-256 válido;
- assinatura Sigstore keyless válida para o workflow oficial `release-bridge.yml` em `main`, issuer OIDC do GitHub Actions e transparency log.

Após confirmação:

1. baixa o ZIP para área temporária;
2. valida tamanho e SHA-256;
3. baixa o bundle Sigstore;
4. valida a assinatura, identidade do workflow, issuer OIDC e transparency log;
5. somente depois da validação criptográfica extrai o pacote;
6. rejeita caminhos que escapem do diretório temporário;
7. prepara a versão nova sem sobrescrever o processo em execução;
8. aguarda o aplicativo encerrar;
9. move a instalação atual para backup e ativa a nova;
10. inicia o executável novamente;
11. verifica `http://127.0.0.1:17345/api/bootstrap` por até 20 segundos;
12. em falha, encerra a versão nova, restaura o backup e reinicia a versão anterior.

### Hardening em andamento para v0.1.31

A `main` já expõe `appVersion` em `/api/bootstrap`. Antes da v0.1.31 ainda falta concluir a segunda metade do hardening: o script de instalação deve comparar `appVersion` com a versão exata que acabou de instalar. Um HTTP 2xx de outra versão não será suficiente para confirmar sucesso; nesse caso deverá ocorrer rollback.

Até esse gate voltar a GREEN e a release ser publicada, a última release oficial continua sendo **v0.1.30**.

Os dados persistentes em `%LOCALAPPDATA%\NfeAgendamento` não são substituídos.

### Assinatura keyless das releases

A partir da v0.1.26, a release não depende de chave privada persistente nem de GitHub Secret de assinatura. O `Release Bridge` usa Sigstore keyless com o token OIDC efêmero do GitHub Actions.

O pacote é aceito somente quando o bundle comprova:

- issuer `https://token.actions.githubusercontent.com`;
- identidade `https://github.com/joaoldsxyzbr/Nfe-Agendamento/.github/workflows/release-bridge.yml@refs/heads/main`;
- repositório oficial `https://github.com/joaoldsxyzbr/Nfe-Agendamento`;
- execução em runner hospedado pelo GitHub;
- inclusão verificável no transparency log;
- assinatura correspondente exatamente aos bytes do ZIP cujo SHA-256 também foi validado.

O workflow verifica o bundle antes de publicar a release. O updater repete essa verificação antes de extrair qualquer arquivo.

## Atualização manual segura

Quando precisar atualizar manualmente:

1. confirme que ninguém está usando a cópia compartilhada do executável;
2. encerre o app por **Sair** nos PCs envolvidos;
3. preserve uma cópia da versão atual até validar a nova;
4. use somente arquivos da release oficial testada;
5. abra primeiro o antigo Central caso esta seja a primeira versão com liderança automática;
6. depois abra os demais PCs;
7. confira **Status da fila**;
8. valide uma consulta no líder e outra em standby;
9. feche o líder e valide o failover;
10. confirme que uma NF-e já consultada continua vindo do cache depois da troca de líder;
11. valide DANFE/download XML e, quando necessário, Portal Nacional/WebView2 em um PC autorizado, inclusive em standby.

Se a atualização incluir revogação/rotação de confiança, atualize os PCs confiáveis antes de testar a remoção de um candidato antigo.

## O que fica persistente

Dependendo da instalação, `%LOCALAPPDATA%\NfeAgendamento` pode conter:

- seleção local do certificado e UF;
- auditoria fiscal;
- pareamento/segredo do cliente via DPAPI;
- chave de estado do candidato via DPAPI;
- material legado usado somente na migração;
- chaves pendentes de solicitações;
- perfil local do WebView2;
- cache local legado de versões anteriores, que não é o cache operacional da arquitetura automática.

Na pasta compartilhada ficam os estados necessários à coordenação protegidos criptograficamente, incluindo identidade do grupo, autorização/replay, cooldown fiscal, cache XML compartilhado, marcador de rotação e sidecars de transição de identidade quando aplicáveis.

## Recuperação de falha

Se uma atualização falhar antes de instalar, a versão atual permanece. Não desative SHA-256, Sigstore ou outras validações para contornar o problema.

Se uma versão nova não iniciar, o atualizador tenta automaticamente restaurar o backup. Se ainda houver intervenção manual:

1. encerre processos restantes;
2. restaure a pasta anterior do executável;
3. não apague `%LOCALAPPDATA%\NfeAgendamento`;
4. confira o CI/commit da versão antes de tentar novamente.

Se o app abrir mas não houver líder, consulte [CENTRAL-LAN.md](CENTRAL-LAN.md).

## Cancelamento fiscal deliberadamente conservador

Cancelar uma ação de interface impede trabalho ainda não iniciado e, em lote, impede os próximos itens. Depois que uma operação fiscal entra na região em que a SEFAZ pode ter recebido a solicitação, o app não força cancelamento seguido de retry automático. Essa escolha é deliberada para evitar duplicidade em resultado ambíguo.

## Modelo de ameaça local

A API em `127.0.0.1` não fica exposta à LAN, mas loopback não isola processos ou usuários do mesmo Windows. O app assume PCs corporativos confiáveis. Outro processo malicioso no mesmo computador deve ser tratado como comprometimento local, não como cliente remoto bloqueado pela política de rede.

## Validação após atualização

O mínimo recomendado é:

- pasta compartilhada acessível em pelo menos dois PCs e sem Offline Files;
- A1 local configurado nos candidatos;
- exatamente um líder;
- consulta funcionando no líder;
- consulta funcionando em standby via fila;
- takeover automático após encerrar o líder;
- XML em cache reutilizado pelo novo líder sem nova consulta fiscal;
- replay e cooldown preservados;
- nenhuma repetição automática de consulta fiscal ambígua;
- DANFE/XML funcionando;
- código de pareamento usado uma única vez;
- revogação de PC rotacionando chave/identidade e mantendo os demais operacionais;
- candidato offline durante rotação conseguindo validar a cadeia de transições ao retornar;
- Portal validado fisicamente em pelo menos um PC autorizado que esteja em standby;
- atualização oficial contendo ZIP + `.sigstore.json` e passando pelo health check/rollback;
- para a v0.1.31 e posteriores, health check confirmando também a versão exata iniciada.

O roteiro completo está em [Validação física multi-PC](TESTE-MULTI-PC.md).
