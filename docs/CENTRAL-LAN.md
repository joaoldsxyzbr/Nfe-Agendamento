# Guia operacional da central

## Pré-requisitos

- Windows no PC central;
- certificado A1 instalado no usuário que executará o app;
- PC central e clientes na mesma rede privada;
- porta TCP 17345 liberada apenas na rede privada;
- UDP 5353 liberado se a descoberta mDNS for usada.

## Primeira configuração

1. Extraia o pacote em uma pasta permanente.
2. Execute `NfeAgendamento.App.exe`.
3. Confirme no painel Windows que o status está **Central ativa**.
4. Confira o IPv4 e o endereço de acesso mostrados na janela.
5. Clique em **Abrir sistema**.
6. Selecione o certificado A1.
7. Informe a UF autora.
8. Faça uma consulta individual de teste.
9. Teste o download do XML e a visualização do DANFE.

Não é mais necessário iniciar o aplicativo com `--lan`. A aplicação Windows controla o acesso remoto diretamente.

Em uma instalação nova, a Central inicia habilitada. O estado fica persistido em:

```text
%LOCALAPPDATA%\NfeAgendamento\state\central.json
```

## Painel Windows

A janela principal mostra:

- status da Central;
- IPv4 detectado;
- porta `17345`;
- URL para os demais computadores.

Ações:

- **Iniciar Central**: permite novas conexões vindas da rede;
- **Parar Central**: bloqueia conexões remotas, mas mantém o acesso local funcionando;
- **Abrir sistema**: abre `http://127.0.0.1:17345` no PC central.

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
- use nos clientes o endereço informado pela janela;
- não abra cópias independentes do app em outros PCs;
- use uma chave por consulta;
- após `cStat=656`, aguarde o cooldown indicado.

A consulta em lote foi removida. As consultas fiscais são coordenadas no PC central, consultas simultâneas da mesma chave são deduplicadas e o acesso à SEFAZ é serializado.

## Firewall

O Bloco 1 não cria regras do Firewall do Windows automaticamente.

A regra recomendada no PC central deve limitar:

- TCP 17345 ao perfil de rede privada;
- UDP 5353 ao perfil de rede privada apenas quando necessário para mDNS.

Não abra a porta no roteador e não permita o aplicativo em redes públicas.

A automação, verificação e diagnóstico do firewall pertencem ao próximo bloco de rede.

## Segurança

O servidor fica escutando a porta local necessária para a Central, mas a camada de segurança bloqueia clientes remotos sempre que o painel estiver em **Central parada**.

Continuam sendo aplicados:

- CSRF;
- validação de Host;
- validação de Origin;
- limite de tamanho das requisições;
- certificado A1 somente no PC central;
- cache criptografado por DPAPI.

O aplicativo não possui autenticação própria. Enquanto a Central estiver ativa, o acesso deve permanecer restrito à rede interna da empresa.

## Domínio interno

O app anuncia `nfeagendamento.local` por mDNS. A descoberta pode falhar quando a rede bloqueia multicast, separa clientes por VLAN ou aplica isolamento Wi-Fi. Nesses casos, use o IPv4 exibido no painel.

## Diagnóstico

### IP exibido no painel não abre em outro PC

1. confirme que o painel mostra **Central ativa**;
2. confirme que o outro PC está na mesma rede;
3. confirme o IP atual mostrado pelo painel;
4. teste a porta TCP 17345 no Firewall do Windows;
5. verifique se existe isolamento entre os computadores na rede.

### `nfeagendamento.local` não abre, mas o IP funciona

O servidor está acessível e o problema está na descoberta mDNS. Continue usando o IP até o bloco de rede tratar o diagnóstico de descoberta.

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
- o Bloco 1 ainda não configura o firewall automaticamente;
- o domínio depende de mDNS ou do fallback por IP;
- o acesso é HTTP dentro da rede interna;
- não há publicação na internet;
- a consulta fiscal continua sujeita às regras da SEFAZ.
