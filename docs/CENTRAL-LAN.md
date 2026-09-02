# Guia operacional da central

## Pré-requisitos

- Windows no PC central;
- certificado A1 instalado no usuário que executará o app;
- PC central e clientes na mesma sub-rede corporativa;
- permissão para autorizar a regra TCP 17345 no Firewall do Windows;
- UDP 5353 somente se a descoberta mDNS for usada.

## Primeira configuração

1. Extraia o pacote em uma pasta permanente.
2. Execute `NfeAgendamento.App.exe`.
3. Confirme no painel Windows que o status está **Central ativa**.
4. Confira o IPv4 e o endereço de acesso mostrados na janela.
5. Confira os indicadores **Rede**, **Servidor** e **Firewall**.
6. Se o firewall indicar **Precisa configurar**, clique em **Configurar firewall** e autorize o UAC do Windows.
7. Clique em **Abrir sistema**.
8. Selecione o certificado A1.
9. Informe a UF autora.
10. Faça uma consulta individual de teste.
11. Teste o download do XML e a visualização do DANFE.

Não é necessário iniciar o aplicativo com `--lan`. A aplicação Windows mantém o servidor preparado para a LAN e a camada de segurança libera ou bloqueia clientes remotos conforme o estado da Central.

Em uma instalação nova, a Central inicia habilitada. O estado fica persistido em:

```text
%LOCALAPPDATA%\NfeAgendamento\state\central.json
```

Se esse arquivo já existir mas estiver corrompido, ilegível ou inválido, a Central adota **desabilitado** para o acesso remoto. Esse fallback é intencionalmente fail-closed; somente a ausência do arquivo em uma instalação nova assume o estado inicial habilitado.

## Painel Windows

A janela principal mostra:

- status da Central;
- IPv4 detectado;
- porta `17345`;
- URL para os demais computadores;
- status da interface de rede;
- status do listener do servidor na LAN;
- status da regra do Firewall do Windows;
- resumo do problema quando alguma etapa precisa de atenção.

A seleção de IPv4 prioriza interfaces utilizáveis com gateway, endereços privados e adaptadores Ethernet/Wi-Fi, evitando escolher primeiro interfaces de túnel, loopback ou endereços APIPA.

Ações:

- **Iniciar Central**: permite novas conexões vindas da rede;
- **Parar Central**: bloqueia conexões remotas, mas mantém o acesso local funcionando;
- **Abrir sistema**: abre `http://127.0.0.1:17345` no PC central;
- **Configurar firewall**: cria ou corrige a regra de entrada necessária para a Central.

Fechar a janela apenas minimiza a operação para a bandeja. Para encerrar o servidor, use **Sair** no menu da bandeja.

## Bandeja do Windows

O menu da bandeja mantém as ações rápidas da Central e passa a mostrar o endereço de rede atual. Quando o acesso remoto está disponível, aparece uma linha como:

```text
Acesso: http://10.0.0.29:17345
```

A ação **Copiar endereço da Central** envia esse endereço para a área de transferência para que ele possa ser passado aos outros computadores sem redigitação.

Se a Central estiver parada, o menu mostra **Acesso pela rede: desativado**. Se estiver ativa mas nenhum IPv4 utilizável tiver sido identificado, mostra **Acesso pela rede: IP não identificado** e o botão de copiar fica desativado.

## Uso nos clientes

Use preferencialmente o endereço exibido pelo painel ou copiado pelo menu da bandeja. Exemplo:

```text
http://10.0.0.29:17345
```

Nunca use `127.0.0.1` nos clientes: esse endereço sempre aponta para o próprio computador que está acessando.

Quando a descoberta mDNS funcionar, também pode ser usado:

```text
http://nfeagendamento.local:17345
```

O cliente só precisa de navegador. Não instale o certificado A1 nos clientes e não copie a pasta `%LOCALAPPDATA%\NfeAgendamento`.

A administração do certificado é deliberadamente local: clientes remotos recebem `403` ao tentar acessar as rotas de listagem, estado atual ou seleção de certificado. O painel web remoto oculta essa configuração e mantém somente o fluxo operacional de consulta, visualização e download.

O certificado e a chave privada não trafegam para os clientes. Porém, quando um usuário remoto consulta, visualiza ou baixa uma NF-e, o XML correspondente é entregue pela Central ao navegador através da rede interna HTTP. Portanto, a porta `17345` deve permanecer somente em uma LAN corporativa confiável; o aplicativo não fornece TLS para esse tráfego e não deve ser exposto à internet.

## Operação diária

- mantenha o PC central ligado;
- mantenha o aplicativo aberto ou na bandeja;
- confirme que o painel mostra **Central ativa**;
- confirme **Rede: OK**, **Servidor: OK** e **Firewall: OK** antes do primeiro uso em outro PC;
- use nos clientes o endereço informado pela janela ou copiado pela bandeja;
- não abra cópias independentes do app em outros PCs;
- use uma chave por consulta;
- após `cStat=656`, aguarde o cooldown indicado.

A consulta em lote foi removida. As consultas fiscais são coordenadas no PC central, consultas simultâneas da mesma chave são deduplicadas e o acesso à SEFAZ é serializado.

## Fila fiscal

A Central admite no máximo **12 operações fiscais únicas** ao mesmo tempo: uma em execução e até 11 aguardando. O limite evita que vários computadores acumulem um número indefinido de consultas na memória.

A deduplicação acontece antes da fila. Se dois ou mais computadores pedirem a mesma chave enquanto a primeira consulta ainda estiver em andamento, todos compartilham a mesma operação fiscal e apenas uma chamada pode chegar à SEFAZ.

Quando as 12 vagas estão ocupadas, uma chave diferente recebe:

```text
HTTP 429
status: fila_ocupada
Retry-After: 5
```

Esse retorno significa somente que a Central está ocupada. Ele não representa `cStat=656`.

No navegador, esse cenário é exibido como **Central ocupada**, usando o valor real do cabeçalho `Retry-After` para informar em quantos segundos tentar novamente.

## Proteção contra cStat=656

Quando a SEFAZ retorna `656`, a Central aplica imediatamente um cooldown de uma hora **em memória** e depois tenta persistir o mesmo estado. A ordem é intencional: uma falha de disco ou permissão não pode liberar novas consultas dentro do processo atual.

Quando a persistência funciona, o cooldown também sobrevive ao encerramento e reinício do aplicativo. Se a persistência falhar após um `656` real, a consulta ainda retorna bloqueada e as próximas consultas no mesmo processo permanecem bloqueadas; somente a durabilidade após reinício depende da gravação bem-sucedida.

Consultas que já estavam aguardando na fila também verificam o cooldown novamente depois de obter a vez de execução. Se uma consulta anterior tiver recebido `656`, as demais são bloqueadas localmente antes de qualquer nova chamada externa.

No navegador, esse retorno é apresentado separadamente da fila cheia: a mensagem informa que o bloqueio veio da **SEFAZ**, mostra o horário exato de liberação e orienta a não repetir a consulta antes desse momento.

O estado de cooldown persistido é protegido por DPAPI. Se um arquivo de cooldown existente estiver corrompido ou não puder ser validado, a Central falha de forma segura: retorna um erro controlado e não envia uma nova consulta à SEFAZ.

## Limites da comunicação fiscal

A consulta individual aplica:

- no máximo 3 tentativas para falhas transitórias de comunicação;
- são consideradas transitórias: falha de rede sem status HTTP, HTTP `408`, HTTP `429`, respostas HTTP `5xx` e timeout da chamada externa;
- erros HTTP permanentes de cliente, como `400`, `401`, `403` e `404`, não são repetidos automaticamente;
- 2 segundos antes da segunda tentativa;
- 5 segundos antes da terceira tentativa;
- timeout de 45 segundos na chamada externa;
- máximo de 10 MB para a resposta fiscal;
- máximo de 256 KB para o corpo das requisições locais.

Falha de rede após a última tentativa, timeout final ou resposta fiscal inválida são convertidos em erro controlado. Esses casos não deixam exceções de transporte escaparem como erro interno genérico da aplicação.

## Cache XML

O XML obtido com sucesso pode ser mantido no cache local por até 24 horas. O conteúdo é protegido por DPAPI e fica no perfil do usuário do Windows do PC central.

Uma entrada de cache que não puder ser descriptografada, desserializada ou validada contra a chave solicitada é considerada inválida, apagada e tratada como **cache miss**. A primeira consulta não falha apenas porque um arquivo antigo do cache está corrompido; o fluxo segue normalmente para a fila fiscal e, se permitido, para a SEFAZ.

Erros reais de acesso ao sistema de arquivos não são mascarados como cache miss. A autocorreção é limitada ao conteúdo de cache inválido.

## Auditoria fiscal

Cada operação fiscal compartilhada gera um registro operacional em:

```text
%LOCALAPPDATA%\NfeAgendamento\logs\fiscal-audit.jsonl
```

O arquivo atual gira ao atingir aproximadamente 2 MB e mantém somente um backup:

```text
fiscal-audit.jsonl.1
```

Cada linha contém apenas:

- horário UTC;
- fingerprint SHA-256 de 12 caracteres da chave;
- status interno da operação;
- `cStat`, quando disponível;
- indicação de cache;
- duração em milissegundos.

A auditoria não contém XML, chave de acesso completa, certificado, chave privada, CPF/CNPJ nem mensagem integral da SEFAZ. Se o log não puder ser gravado, a operação fiscal continua normalmente.

## Firewall

O painel verifica se existe a regra estável da Central. O botão **Configurar firewall** solicita elevação pelo UAC e recria a regra com estas restrições:

- direção de entrada;
- protocolo TCP;
- porta local `17345`;
- perfis **Domínio** e **Privado**;
- origem limitada a `LocalSubnet`;
- sem liberação no perfil Público;
- sem vínculo ao caminho do `NfeAgendamento.App.exe`.

A regra não depende mais da pasta do executável. Atualizar o aplicativo ou mover a instalação não invalida o firewall apenas por mudança de caminho.

Em computadores administrados por política corporativa, o Windows pode impedir alterações locais. Nessa situação, a regra deve ser aplicada pelo administrador da rede.

O UDP `5353` continua opcional e serve apenas para `nfeagendamento.local`. O acesso por IPv4 não depende dele.

Não abra a porta no roteador e não permita o aplicativo em redes públicas.

## Segurança

O servidor fica ouvindo em `0.0.0.0:17345` para poder atender a rede, mas a camada de segurança bloqueia clientes remotos sempre que o painel estiver em **Central parada**.

Continuam sendo aplicados:

- CSRF em operações mutáveis;
- validação de Host;
- Host remoto na porta `17345` aceito somente para um IPv4 realmente atribuído ao PC central ou para o nome interno explicitamente permitido `nfeagendamento.local`;
- validação de Origin consistente com o Host permitido;
- limite de tamanho das requisições;
- administração de certificado exclusiva de conexão loopback no PC central;
- certificado A1 e chave privada somente no PC central;
- arquivo de estado da Central existente e inválido desabilita acesso remoto;
- cache e cooldown criptografados por DPAPI;
- cache inválido descartado antes de reutilização;
- fila fiscal serializada, limitada e deduplicada;
- bloqueio imediato em memória após `cStat=656`, com persistência quando possível;
- auditoria sem dados fiscais completos;
- regra de firewall limitada à porta `17345`, perfis Domínio/Privado e origem `LocalSubnet`, sem dependência do caminho do executável.

O aplicativo não possui autenticação própria. Enquanto a Central estiver ativa, o acesso deve permanecer restrito à rede interna da empresa.

## Domínio interno

O app anuncia `nfeagendamento.local` por mDNS. O endereço anunciado é obtido pela **mesma seleção de IPv4 usada pelo painel da Central**, evitando que o nome interno aponte para uma interface diferente daquela apresentada ao operador.

A descoberta pode falhar quando a rede bloqueia multicast, separa clientes por VLAN ou aplica isolamento Wi-Fi. Nesses casos, use o IPv4 exibido no painel.

## Release e rastreabilidade

O fluxo oficial de publicação é o **Release Bridge** manual. O workflow trabalha com o SHA imutável associado ao disparo (`github.sha`): o mesmo commit é obtido, testado, compilado, empacotado e usado como destino da tag/release.

Assim, se a `main` receber outro commit enquanto uma publicação estiver em andamento, esse avanço não altera silenciosamente o conteúdo daquela release. A versão publicada corresponde ao código efetivamente validado no próprio workflow.

## Diagnóstico

### Rede não está OK

O app não encontrou um IPv4 utilizável para a Central. Verifique se Ethernet ou Wi-Fi estão conectados e se o PC recebeu um endereço válido da rede.

### Servidor não está OK

A porta `17345` não foi encontrada ouvindo em uma interface de rede. Feche outras instâncias do NFe Agendamento e reinicie o aplicativo. Se a porta já estiver ocupada por outro programa, o app informa o conflito ao iniciar.

### Firewall mostra Precisa configurar

Clique em **Configurar firewall** e confirme o UAC. A regra é recriada para a porta `17345` e deixa de depender da pasta da versão instalada. Se continuar igual, a máquina provavelmente possui política corporativa de firewall e a liberação precisa ser feita pelo administrador.

### IP exibido no painel não abre em outro PC mesmo com tudo OK

1. confirme que o outro PC está na mesma sub-rede da Central;
2. execute no cliente `Test-NetConnection IP-DO-CENTRAL -Port 17345`;
3. se o teste falhar apesar dos três indicadores OK, investigue isolamento entre clientes, ACL de rede ou política corporativa fora do computador central.

### `nfeagendamento.local` não abre, mas o IP funciona

O servidor está acessível e o problema está somente na descoberta mDNS. Continue usando o IPv4 mostrado no painel.

### HTTP 429 com `fila_ocupada`

A tela informa que a **Central está ocupada** e mostra o tempo indicado em `Retry-After`. Aguarde esse intervalo e tente novamente. Não é necessário aguardar uma hora e esse retorno não veio da SEFAZ.

### HTTP 429 com `consumo_indevido`

É o tratamento do `cStat=656`. A tela identifica a **SEFAZ**, mostra o horário de liberação e orienta a não repetir a consulta antes dele. Verifique também se outro sistema consulta o mesmo CNPJ.

### Estado fiscal local inválido

A Central não conseguiu validar o arquivo persistido de cooldown. Por segurança, ela não envia uma nova consulta à SEFAZ. Encerre o app e investigue o estado em `%LOCALAPPDATA%\NfeAgendamento\state` antes de continuar a operação.

### Cache XML corrompido

A entrada inválida é descartada automaticamente e a consulta segue como se não houvesse cache. Não é necessário apagar manualmente todo o diretório por causa de uma única entrada corrompida.

### Certificado não aparece

O certificado precisa estar instalado no Windows Certificate Store do usuário que executa o app, dentro da validade e com chave privada acessível. Essa configuração deve ser feita no próprio PC central; a interface remota não possui permissão para administrar certificados.

## Dados locais

O app armazena dados em:

```text
%LOCALAPPDATA%\NfeAgendamento
```

O cache e o estado fiscal são protegidos pelo DPAPI do usuário do Windows. Trocar o usuário do Windows pode impedir o acesso ao cache antigo; nesse caso a entrada de cache que não puder ser validada é descartada, enquanto um estado fiscal persistido inválido continua seguindo a política fail-closed.

A auditoria operacional fica em `logs\fiscal-audit.jsonl` e não contém XML nem identificadores fiscais completos.

## Limitações atuais

- o PC central precisa permanecer ligado;
- a configuração automática do firewall depende de autorização do Windows e pode ser bloqueada por política corporativa;
- os clientes precisam estar na mesma sub-rede permitida pela regra `LocalSubnet`;
- o domínio depende de mDNS ou do fallback por IP;
- o acesso é HTTP dentro da rede interna e o conteúdo solicitado da NF-e trafega sem TLS fornecido pelo aplicativo;
- não há publicação na internet e a porta `17345` não deve ser exposta externamente;
- o diagnóstico local não detecta isolamento de VLAN/ACL existente fora do PC central;
- a consulta fiscal continua sujeita às regras da SEFAZ.
