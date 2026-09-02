# Inicialização e atualização do NFe Agendamento

Este guia descreve a inicialização de cada cópia local do aplicativo e o mecanismo de atualização pela release oficial do GitHub.

## Arquitetura atual da `main`

Cada PC executa sua própria cópia do `NfeAgendamento.App.exe` e a interface web fica restrita a:

```text
http://127.0.0.1:17345
```

A comunicação entre computadores usa exclusivamente:

```text
P:\01-Nfe agendamento
```

Não existe mais servidor HTTP exposto na LAN, mDNS nem configuração automática de Firewall do Windows.

## Inicialização normal

O executável oficial é:

```text
NfeAgendamento.App.exe
```

O argumento legado `--lan` pode existir em atalhos antigos, mas é ignorado e nunca expõe a porta `17345` para outros computadores.

O papel do PC fica salvo em:

```text
%LOCALAPPDATA%\NfeAgendamento\state\central.json
```

Há duas configurações independentes:

- **Iniciar com o Windows** decide se a cópia local do programa será aberta automaticamente;
- **Iniciar Central / Parar Central** decide se aquele PC tentará atuar como Central da fila compartilhada.

Uma instalação nova começa como **Cliente**. O papel de Central somente é ativado manualmente pelo usuário.

## Reassunção automática da Central

Quando o usuário clica em **Iniciar Central**, `ConfiguredAsCentral = true` é persistido localmente.

Nas próximas inicializações, esse PC tenta automaticamente:

1. validar `P:\01-Nfe agendamento`;
2. adquirir o lock exclusivo em `status\central.lock`;
3. publicar heartbeat;
4. iniciar o processador da fila.

Se a pasta estiver indisponível, o aplicativo não procura outro caminho nem abre uma rota de rede alternativa. Ele permanece aguardando a pasta e tenta novamente.

Se outro PC já possuir o lock, o painel mostra conflito e não inicia um segundo processador fiscal.

Ao usar **Parar Central**, `ConfiguredAsCentral = false` é salvo, o lock é liberado e a reassunção automática deixa de ocorrer.

## Iniciar com o Windows

A opção **Iniciar com o Windows** registra o executável no perfil do usuário atual em:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

O comando salvo contém somente o caminho do executável entre aspas, por exemplo:

```text
"C:\NFe Agendamento\NfeAgendamento.App.exe"
```

Nenhuma flag de LAN é necessária.

### Validação manual

1. marque **Iniciar com o Windows**;
2. encerre o aplicativo pelo item **Sair** da bandeja;
3. reinicie o Windows ou encerre a sessão;
4. entre novamente com o mesmo usuário;
5. confirme que o NFe Agendamento abriu;
6. se esse PC era Central, confirme que tentou reassumir automaticamente;
7. se era Cliente, confirme que permaneceu Cliente.

Se o executável for movido de pasta, desmarque e marque novamente **Iniciar com o Windows** para atualizar o caminho registrado.

## Verificar atualização

A ação **Verificar atualização** fica no menu da bandeja.

Ela consulta a release oficial do repositório e compara a versão publicada com a versão gravada no executável instalado.

Quando a versão local já for a mais recente, o aplicativo apenas informa que não há atualização.

Quando existir uma versão nova, a instalação automática somente é oferecida se houver um pacote oficial instalável e verificável.

## Requisitos de um pacote instalável

O atualizador exige:

- release oficial do projeto;
- versão semanticamente maior que a instalada;
- asset `Nfe-Agendamento-win-x64.zip`;
- URL HTTPS;
- tamanho dentro do limite aceito pelo aplicativo;
- digest SHA-256 oficial válido.

Se qualquer requisito falhar, o aplicativo não instala o pacote automaticamente.

## Fluxo da atualização

Depois da confirmação do usuário:

1. o ZIP oficial é baixado para uma pasta temporária;
2. o tamanho recebido é conferido;
3. o SHA-256 calculado localmente é comparado com o digest publicado;
4. cada entrada do ZIP é validada antes da extração;
5. caminhos absolutos, `..` e entradas que escapem do diretório temporário são rejeitados;
6. a nova versão é preparada sem sobrescrever a instância em execução;
7. um processo auxiliar aguarda o encerramento do NFe Agendamento;
8. os arquivos novos substituem o conteúdo da pasta do aplicativo;
9. o executável é iniciado novamente;
10. os arquivos temporários são removidos quando possível.

Os dados persistentes ficam fora da pasta do executável e não são substituídos pelo updater.

Isso inclui, conforme o papel do PC:

- configuração `ConfiguredAsCentral`;
- seleção do certificado;
- cache XML criptografado;
- cooldown fiscal;
- auditoria;
- chave RSA privada da Central protegida por DPAPI;
- chaves AES pendentes de clientes protegidas por DPAPI.

## Estado da release publicada

A última release publicada antes da nova arquitetura por pasta compartilhada é:

```text
v0.1.18
```

A `v0.1.18` ainda pertence à arquitetura LAN anterior, mas **já contém o mecanismo de atualização integrado**.

Portanto, quando a próxima release com a fila compartilhada for publicada, uma instalação `v0.1.18` poderá usar **Verificar atualização** para migrar para ela, desde que o pacote oficial e o SHA-256 sejam validados normalmente.

Instalações mais antigas que não possuam o updater integrado precisam ser atualizadas manualmente uma vez.

## Atualização manual segura

Quando uma atualização manual for necessária:

1. encerre completamente o aplicativo por **Sair**;
2. baixe somente o ZIP da release oficial;
3. preserve uma cópia da pasta atual do executável até validar a nova versão;
4. extraia a nova versão na pasta permanente do aplicativo;
5. execute `NfeAgendamento.App.exe`;
6. confirme que `http://127.0.0.1:17345` abre localmente;
7. confirme acesso a `P:\01-Nfe agendamento`;
8. no PC com A1, confirme o estado da Central/heartbeat/processador;
9. em um cliente, confirme que a Central aparece online;
10. faça uma consulta conhecida e valide XML/DANFE.

Os dados em `%LOCALAPPDATA%\NfeAgendamento` não precisam ser copiados para a pasta do programa.

## Falha na verificação ou instalação

Se houver erro de rede, ZIP inválido, digest ausente/incorreto, falta de permissão ou falha na preparação, a versão atual continua instalada.

Não desative a validação de SHA-256 para contornar uma falha de atualização.

Se a nova versão não iniciar após uma atualização manual:

1. encerre qualquer processo restante do NFe Agendamento;
2. restaure a pasta do executável anterior;
3. não apague `%LOCALAPPDATA%\NfeAgendamento` durante a recuperação;
4. confira o CI e a release correspondente antes de tentar novamente.

Se o aplicativo iniciar mas a operação multi-PC falhar, use o guia [CENTRAL-LAN.md](CENTRAL-LAN.md), cujo nome foi mantido apenas por compatibilidade de links e hoje documenta a fila compartilhada.

## Validação automatizada

O CI cobre:

- testes .NET completos;
- regressões de mapeamento Fernando Klein;
- feedback de erros fiscais;
- prontidão dos workflows de release;
- build Release;
- publish Windows autocontido;
- geração do ZIP de teste.

Os testes da fila usam diretórios temporários e não dependem do `P:` real, de certificado real nem da SEFAZ real.

A troca física dos arquivos e a validação do compartilhamento corporativo continuam fazendo parte da aceitação manual da release.