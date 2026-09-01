# Inicialização e atualização da Central

Este guia cobre o comportamento da Central Windows no início da sessão do usuário e o mecanismo de atualização pelo próprio aplicativo.

## Inicialização normal

O executável oficial é:

```text
NfeAgendamento.App.exe
```

Não é necessário passar `--lan` ou qualquer outra flag para habilitar a rede.

O servidor é iniciado preparado para a porta `17345`. A autorização de clientes remotos depende do estado persistido **Central ativa/parada**, controlado pela janela da Central.

O estado é salvo em:

```text
%LOCALAPPDATA%\NfeAgendamento\state\central.json
```

Isso separa duas responsabilidades:

- **Iniciar com o Windows** decide se o programa será aberto automaticamente;
- **Central ativa/parada** decide se computadores da rede podem acessar o servidor.

## Iniciar com o Windows

A opção **Iniciar com o Windows** do menu da bandeja registra o executável no `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` do usuário atual.

O comando salvo contém apenas o caminho do executável entre aspas, por exemplo:

```text
"C:\NFe Agendamento\NfeAgendamento.App.exe"
```

Não é registrado `--lan`.

### Validação manual

1. marque **Iniciar com o Windows**;
2. encerre o NFe Agendamento pelo item **Sair** da bandeja;
3. termine a sessão do Windows ou reinicie o PC central;
4. entre novamente com o mesmo usuário;
5. confirme que o ícone da Central apareceu na bandeja;
6. abra a janela e confirme que o estado ativa/parada foi preservado;
7. confirme Rede, Servidor e Firewall.

Se o executável for movido de pasta depois dessa configuração, desmarque e marque novamente **Iniciar com o Windows** para atualizar o caminho registrado.

## Verificar atualização

A ação **Verificar atualização** fica no menu da bandeja.

A consulta usa a release oficial do repositório GitHub do NFe Agendamento.

Quando a versão local já for a mais recente, o aplicativo apenas informa que não há atualização.

Quando existir versão nova, a instalação automática só é oferecida se houver um pacote oficial instalável e verificável.

## Requisitos de um pacote instalável

O atualizador exige:

- release oficial do projeto;
- versão semanticamente maior que a instalada;
- asset `Nfe-Agendamento-win-x64.zip`;
- URL HTTPS;
- tamanho dentro do limite aceito pelo aplicativo;
- digest SHA-256 oficial com 64 caracteres hexadecimais.

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
10. os arquivos temporários de atualização são removidos quando possível.

O certificado A1, cache, cooldown, configuração da Central e auditoria ficam em áreas persistentes do Windows e não fazem parte da substituição do pacote da aplicação.

## Por que o app precisa fechar

O Windows mantém arquivos do executável e DLLs em uso enquanto o processo está aberto. Por isso a Central não tenta substituir seus próprios arquivos em execução.

O aplicativo prepara a atualização, inicia o processo auxiliar e então encerra normalmente. Só depois o pacote é aplicado.

## Falha na verificação

Se houver erro de rede, ZIP inválido, digest ausente/incorreto, falta de permissão ou qualquer falha na preparação, a versão atual continua instalada.

O usuário recebe uma mensagem e pode tentar novamente depois.

Não desative a validação de SHA-256 para contornar uma falha de atualização.

## Primeira release com o updater

A `v0.1.16` foi publicada antes da implementação do novo mecanismo de atualização. Portanto ela não consegue instalar automaticamente a primeira release do Bloco 6.

A primeira versão que contiver o updater precisa ser instalada manualmente uma vez, substituindo o pacote antigo pela release oficial.

A partir dessa versão, o fluxo **Verificar atualização** pode ser validado de ponta a ponta quando uma versão posterior for publicada.

## Atualização manual segura

Quando for necessário atualizar manualmente:

1. encerre completamente o aplicativo por **Sair**;
2. baixe somente o ZIP da release oficial;
3. preserve uma cópia da pasta atual do executável até validar a nova versão;
4. extraia a nova versão na pasta permanente do aplicativo;
5. execute `NfeAgendamento.App.exe`;
6. confirme Central, Rede, Servidor e Firewall;
7. faça uma consulta de teste e valide XML/DANFE.

Os dados em `%LOCALAPPDATA%\NfeAgendamento` não precisam ser copiados para a pasta do programa.

## Falha depois da atualização

Se a nova versão não iniciar:

1. encerre qualquer processo restante do NFe Agendamento;
2. restaure a pasta do executável anterior;
3. não apague `%LOCALAPPDATA%\NfeAgendamento` durante a recuperação;
4. registre a versão que falhou e verifique o CI/release correspondente antes de tentar novamente.

Se o aplicativo iniciar mas o acesso dos clientes falhar, use o guia [CENTRAL-LAN.md](CENTRAL-LAN.md). Mudança de pasta do executável também pode exigir recriar a regra do Firewall porque ela é vinculada ao caminho atual do programa.

## Validação automatizada

O CI cobre:

- comparação de versões e descoberta do pacote oficial;
- exigência de SHA-256;
- rejeição de pacote inválido;
- preparação segura da extração;
- proteção contra path traversal;
- integração da ação da bandeja com preparação e reinício;
- geração do pacote Windows autocontido.

A troca real dos arquivos em um PC de produção continua fazendo parte da aceitação manual da release, porque o runner do GitHub Actions não representa a instalação e as políticas do PC central da empresa.
