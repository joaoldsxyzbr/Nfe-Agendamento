# Guia operacional da central

## Pré-requisitos

- Windows no PC central;
- certificado A1 instalado no usuário que executará o app;
- PC central e clientes na mesma rede privada;
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

## Uso nos clientes

Use preferencialmente o endereço exibido pelo painel. Exemplo:

```text
http://10.0.0.29:17345
```

Nunca use `127.0.0.1` nos clientes: esse endereço sempre aponta para o próprio computador que está acessando.

Quando a descoberta mDNS funcionar, também pode ser usado:

```text
http://nfeagendamento.local:17345
```

O cliente só precisa de navegador. Não instale o certificado A1 nos clientes e não copie a pasta `%LOCALAPPDATA%\NfeAgendamento`.

## Operação diária

- mantenha o PC central ligado;
- mantenha o aplicativo aberto ou na bandeja;
- confirme que o painel mostra **Central ativa**;
- confirme **Rede: OK**, **Servidor: OK** e **Firewall: OK** antes do primeiro uso em outro PC;
- use nos clientes o endereço informado pela janela;
- não abra cópias independentes do app em outros PCs;
- use uma chave por consulta;
- após `cStat=656`, aguarde o cooldown indicado.

A consulta em lote foi removida. As consultas fiscais são coordenadas no PC central, consultas simultâneas da mesma chave são deduplicadas e o acesso à SEFAZ é serializado.

## Firewall

O painel verifica se existe uma regra compatível com o executável atual. O botão **Configurar firewall** solicita elevação pelo UAC e recria a regra da Central com estas restrições:

- direção de entrada;
- protocolo TCP;
- porta local `17345`;
- perfil **Privado**;
- vinculada ao caminho atual do `NfeAgendamento.App.exe`;
- sem liberação no perfil Público.

Se o executável for movido para outra pasta, o painel pode pedir a configuração novamente porque a regra fica vinculada ao caminho do programa.

Em computadores administrados por política corporativa, o Windows pode impedir alterações locais. Nessa situação, a regra deve ser aplicada pelo administrador da rede.

O UDP `5353` continua opcional e serve apenas para `nfeagendamento.local`. O acesso por IPv4 não depende dele.

Não abra a porta no roteador e não permita o aplicativo em redes públicas.

## Segurança

O servidor fica ouvindo em `0.0.0.0:17345` para poder atender a rede, mas a camada de segurança bloqueia clientes remotos sempre que o painel estiver em **Central parada**.

Continuam sendo aplicados:

- CSRF;
- validação de Host;
- validação de Origin;
- limite de tamanho das requisições;
- certificado A1 somente no PC central;
- cache criptografado por DPAPI;
- regra de firewall limitada ao perfil Privado e ao executável atual.

O aplicativo não possui autenticação própria. Enquanto a Central estiver ativa, o acesso deve permanecer restrito à rede interna da empresa.

## Domínio interno

O app anuncia `nfeagendamento.local` por mDNS. A descoberta pode falhar quando a rede bloqueia multicast, separa clientes por VLAN ou aplica isolamento Wi-Fi. Nesses casos, use o IPv4 exibido no painel.

## Diagnóstico

### Rede não está OK

O app não encontrou um IPv4 utilizável para a Central. Verifique se Ethernet ou Wi-Fi estão conectados e se o PC recebeu um endereço válido da rede.

### Servidor não está OK

A porta `17345` não foi encontrada ouvindo em uma interface de rede. Feche outras instâncias do NFe Agendamento e reinicie o aplicativo. Se a porta já estiver ocupada por outro programa, o app informa o conflito ao iniciar.

### Firewall mostra Precisa configurar

Clique em **Configurar firewall** e confirme o UAC. Se continuar igual, a máquina provavelmente possui política corporativa de firewall e a liberação precisa ser feita pelo administrador.

### IP exibido no painel não abre em outro PC mesmo com tudo OK

1. confirme que o outro PC está na mesma rede ou VLAN com comunicação permitida;
2. execute no cliente `Test-NetConnection IP-DO-CENTRAL -Port 17345`;
3. se o teste falhar apesar dos três indicadores OK, investigue isolamento entre clientes, ACL de rede ou política corporativa fora do computador central.

### `nfeagendamento.local` não abre, mas o IP funciona

O servidor está acessível e o problema está somente na descoberta mDNS. Continue usando o IPv4 mostrado no painel.

### Retorno 429

É o tratamento do `cStat=656`. Aguarde o horário exibido e verifique se outro sistema consulta o mesmo CNPJ.

### Certificado não aparece

O certificado precisa estar instalado no Windows Certificate Store do usuário que executa o app, dentro da validade e com chave privada acessível.

## Dados locais

O app armazena dados em:

```text
%LOCALAPPDATA%\NfeAgendamento
```

O cache e o estado fiscal são protegidos pelo DPAPI do usuário do Windows. Trocar o usuário do Windows pode impedir o acesso ao cache antigo; isso é esperado pelo modelo de proteção.

## Limitações atuais

- o PC central precisa permanecer ligado;
- a configuração automática do firewall depende de autorização do Windows e pode ser bloqueada por política corporativa;
- o domínio depende de mDNS ou do fallback por IP;
- o acesso é HTTP dentro da rede interna;
- não há publicação na internet;
- o diagnóstico local não detecta isolamento de VLAN/ACL existente fora do PC central;
- a consulta fiscal continua sujeita às regras da SEFAZ.
