# Guia operacional da central

## Pré-requisitos

- Windows no PC central;
- certificado A1 instalado no usuário que executará o app;
- PC central e clientes na mesma rede privada;
- porta TCP 17345 liberada apenas na rede privada;
- UDP 5353 liberado se a descoberta mDNS for usada.

## Primeira configuração

1. Extraia o pacote em uma pasta permanente.
2. Execute `NfeAgendamento.App.exe --lan`.
3. Abra `http://127.0.0.1:17345` no próprio PC.
4. Selecione o certificado A1.
5. Informe a UF autora.
6. Faça uma consulta individual de teste.
7. Teste o download do XML e a visualização do DANFE.

O modo `--lan` faz o servidor escutar nas interfaces de rede. O modo sem argumento continua restrito a `127.0.0.1`.

A consulta em lote foi removida. A central trabalha apenas com consultas individuais para manter o consumo fiscal previsível quando vários computadores usam o mesmo certificado e o mesmo CNPJ.

## Uso nos clientes

O endereço principal é:

```text
http://nfeagendamento.local:17345
```

O acesso é direto, sem senha. Se não funcionar, descubra o IPv4 do PC central com `ipconfig` e use:

```text
http://IP-DO-CENTRAL:17345
```

O cliente só precisa de navegador. Não instale o certificado A1 nos clientes e não copie a pasta `%LOCALAPPDATA%\\NfeAgendamento`.

## Operação diária

- mantenha o PC central ligado;
- mantenha o aplicativo aberto na bandeja;
- faça as consultas pelo endereço central;
- não abra cópias independentes do app em outros PCs;
- use uma chave por consulta;
- não repita consultas enquanto uma consulta estiver em andamento;
- após `cStat=656`, aguarde o cooldown indicado.

As consultas fiscais são coordenadas no PC central. Consultas simultâneas da mesma chave são deduplicadas e o acesso à SEFAZ é serializado.

## Domínio interno

O app anuncia `nfeagendamento.local` por mDNS. A descoberta pode falhar quando a rede bloqueia multicast, separa clientes por VLAN ou aplica isolamento Wi-Fi. Nesses casos, use o IP do central.

O domínio não é público e não exige compra de domínio ou configuração na internet.

## Firewall

A regra recomendada no PC central deve limitar:

- protocolo TCP, porta 17345, perfil Rede privada;
- protocolo UDP, porta 5353, perfil Rede privada, apenas quando necessário para mDNS.

Não abra a porta no roteador e não use perfil de rede pública.

## Acesso interno

O aplicativo não possui autenticação própria. Qualquer computador que consiga alcançar a porta 17345 poderá abrir a interface, por isso o acesso deve ficar restrito à rede privada da empresa.

Não publique a porta 17345 na internet e não permita o aplicativo no perfil de rede pública do Windows.

## Dados locais

O app armazena dados em:

```text
%LOCALAPPDATA%\\NfeAgendamento
```

O cache e o estado fiscal são protegidos pelo DPAPI do usuário do Windows. Trocar o usuário do Windows pode impedir o acesso ao cache antigo; isso é esperado pelo modelo de proteção.

## Diagnóstico

### Domínio não abre

1. teste o IP do central;
2. confirme que o app foi iniciado com `--lan`;
3. confirme que os PCs estão na mesma rede;
4. verifique se UDP 5353 não está bloqueado;
5. confirme que o perfil do Windows é Rede privada.

### IP também não abre

1. confirme que o app está em execução;
2. confirme a porta TCP 17345 no firewall;
3. teste a conectividade entre os PCs;
4. confirme o IP atual do PC central;
5. verifique se o PC entrou em outra rede.

### Retorno 429

É o tratamento do `cStat=656`. Aguarde o horário exibido e verifique se outro sistema consulta o mesmo CNPJ.

### Certificado não aparece

O certificado precisa estar instalado no Windows Certificate Store do usuário que executa o app, dentro da validade e com chave privada acessível.

## Atualização

Sempre encerre o app antes de substituir os arquivos da aplicação. Baixe somente releases oficiais do repositório e mantenha a pasta de dados do usuário intacta.

## Limitações atuais

- o servidor central precisa permanecer ligado;
- o domínio depende de mDNS ou do fallback por IP;
- o app não cria automaticamente regras do firewall;
- o acesso é HTTP dentro da rede interna;
- não há publicação na internet;
- a consulta fiscal continua sujeita às regras da SEFAZ;
- o DANFE completo continua em evolução.
