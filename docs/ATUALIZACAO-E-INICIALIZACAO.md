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

## Inicialização normal

O executável oficial é:

```text
NfeAgendamento.App.exe
```

O argumento legado `--lan` pode existir em atalhos antigos, mas é ignorado e nunca expõe a porta 17345 para outros computadores.

Depois que o PC está autorizado no grupo, ele participa automaticamente da eleição da fila ao iniciar:

- se conseguir `status\central.lock`, vira líder;
- se outro PC já possuir o lock, fica em standby;
- se a pasta estiver indisponível, não inicia trabalho fiscal.

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

A migração preserva a identidade RSA da fila e o estado de clientes/replay. Não é necessário reaparear todos os PCs.

O antigo PC Central também ganha uma identidade de cliente durante a migração, portanto continua conseguindo consultar quando estiver em standby.

## Autorizar um PC novo

O código temporário é gerado somente no líder atual.

1. abra **Configurar** no PC líder;
2. clique em **Gerar código de autorização**;
3. no PC novo, abra o sistema local;
4. informe o código e clique em **Autorizar este PC**;
5. o novo PC recebe seu vínculo local e o pacote que o torna futuro candidato a líder.

O estado local fica protegido por DPAPI e não precisa ser recriado a cada inicialização.

## Certificado A1

O certificado é configurado localmente em cada PC confiável. Ele não é copiado pela fila e não faz parte do pacote de candidatura.

Antes de depender de um PC como possível líder, confirme nele:

1. A1 instalado no usuário correto;
2. certificado selecionado no NFe Agendamento;
3. UF autora configurada;
4. consulta conhecida funcionando.

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
- HTTPS;
- tamanho dentro do limite;
- digest SHA-256 válido.

Após confirmação:

1. baixa o ZIP para área temporária;
2. valida tamanho e SHA-256;
3. rejeita caminhos absolutos, `..` e entradas que escapem do diretório temporário;
4. prepara a versão nova sem sobrescrever o processo em execução;
5. aguarda o aplicativo encerrar;
6. substitui os arquivos da pasta do app;
7. inicia o executável novamente;
8. remove temporários quando possível.

Os dados persistentes em `%LOCALAPPDATA%\NfeAgendamento` não são substituídos.

## Atualização manual segura

Quando precisar atualizar manualmente:

1. confirme que ninguém está usando a cópia compartilhada do executável;
2. encerre o app por **Sair** nos PCs envolvidos;
3. preserve uma cópia da versão atual até validar a nova;
4. substitua pelos arquivos oficiais/testados;
5. abra primeiro o antigo Central caso esta seja a primeira versão com liderança automática;
6. depois abra os demais PCs;
7. confira **Status da fila**;
8. valide uma consulta no líder e outra em standby;
9. feche o líder e valide o failover;
10. valide DANFE/download XML e, quando necessário, Portal Nacional/WebView2.

## O que fica persistente

Dependendo da instalação, `%LOCALAPPDATA%\NfeAgendamento` pode conter:

- seleção local do certificado e UF;
- cache XML cifrado;
- auditoria fiscal;
- pareamento/segredo do cliente via DPAPI;
- chave de estado do candidato via DPAPI;
- material legado usado somente na migração;
- chaves pendentes de solicitações;
- perfil local do WebView2.

Na pasta compartilhada ficam apenas os estados necessários à coordenação, protegidos criptograficamente, incluindo identidade do grupo, autorização/replay e cooldown fiscal.

## Recuperação de falha

Se uma atualização falhar antes de instalar, a versão atual permanece. Não desative SHA-256 ou outras validações para contornar o problema.

Se uma versão nova não iniciar:

1. encerre processos restantes;
2. restaure a pasta anterior do executável;
3. não apague `%LOCALAPPDATA%\NfeAgendamento`;
4. confira o CI/commit da versão antes de tentar novamente.

Se o app abrir mas não houver líder, consulte [CENTRAL-LAN.md](CENTRAL-LAN.md).

## Validação após atualização

O mínimo recomendado é:

- pasta compartilhada acessível em pelo menos dois PCs;
- A1 local configurado nos candidatos;
- exatamente um líder;
- consulta funcionando no líder;
- consulta funcionando em standby via fila;
- takeover automático após encerrar o líder;
- replay e cooldown preservados;
- nenhuma repetição automática de consulta fiscal ambígua;
- DANFE/XML funcionando;
- Portal real validado fisicamente quando fizer parte da aceitação da versão.
