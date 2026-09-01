# NFe Agendamento — arquitetura atual da Central LAN

## Objetivo

Permitir que a equipe use o NFe Agendamento pelo navegador enquanto certificado A1, cache, cooldown, fila fiscal, auditoria e comunicação com a SEFAZ permanecem em um único PC central.

## Decisão arquitetural atual

O executável Windows hospeda duas partes no mesmo processo:

- painel WinForms/bandeja para administrar a Central;
- ASP.NET Core com a interface web e os endpoints internos.

O servidor fica preparado para escutar em `0.0.0.0:17345`, permitindo uso local e LAN. A autorização de conexões remotas é controlada pelo estado persistido **Central ativa/parada** na camada de segurança. Assim, não dependemos de o operador iniciar o processo manualmente com `--lan`.

O acesso local continua disponível em:

```text
http://127.0.0.1:17345
```

Quando a Central está ativa, o painel detecta um IPv4 utilizável e exibe um endereço como:

```text
http://10.0.0.29:17345
```

`nfeagendamento.local` é apenas uma conveniência via mDNS; o IPv4 é o fallback operacional.

## Segurança

- certificado e chave privada permanecem no Windows Certificate Store;
- XML, cache e cooldown permanecem no PC central;
- cache e estado fiscal persistido usam DPAPI;
- Host e Origin são validados;
- operações POST exigem token CSRF;
- requisições possuem limite de tamanho;
- clientes remotos são rejeitados quando a Central está parada;
- `/api/bootstrap` expõe somente token CSRF, estado LAN e endereço operacional;
- firewall automático limita entrada a TCP `17345`, perfil Privado e executável atual;
- a porta não deve ser publicada na internet.

O produto atual **não possui autenticação própria por senha**. A fronteira de acesso é a rede interna da empresa, a regra restrita de firewall e o controle ativa/parada da Central. Se autenticação voltar a ser necessária no futuro, deverá ser tratada como uma nova decisão arquitetural e não como comportamento já existente.

## Fluxo fiscal

1. navegador envia a chave ao PC central;
2. backend valida a chave e consulta o cache criptografado;
3. chamadas concorrentes da mesma chave compartilham uma única operação;
4. operações únicas entram na fila fiscal central;
5. a fila admite no máximo 12 operações únicas e executa uma chamada externa por vez;
6. antes de tocar na SEFAZ, a operação verifica o cooldown persistente;
7. `138` com XML válido é armazenado no cache;
8. `137` vira retorno controlado de não localizado;
9. `656` cria cooldown persistente de uma hora;
10. falhas transitórias usam retry limitado; timeout, resposta inválida e falha final viram erros controlados.

O cancelamento de um navegador não cancela uma operação compartilhada que possa estar sendo aguardada por outro cliente.

## Fila e respostas 429

Há dois `429` distintos:

### Central ocupada

```text
status: fila_ocupada
Retry-After: 5
```

É produzido localmente quando as 12 vagas de operações únicas estão ocupadas.

### Consumo indevido SEFAZ

```text
status: consumo_indevido
cStat: 656
blockedUntilUtc: ...
```

O bloqueio é persistido por uma hora e continua válido depois de reiniciar/criar uma nova instância do serviço. Consultas que já estavam aguardando revalidam o cooldown antes de qualquer nova chamada externa.

## Operação Windows

O painel exibe:

- Central ativa/parada;
- IPv4 e porta;
- URL compartilhável;
- diagnóstico de rede;
- diagnóstico do listener;
- diagnóstico do Firewall do Windows.

A bandeja oferece abertura da Central, abertura da interface web, cópia do endereço, configuração do certificado, atualização e encerramento.

## Cliente web

O navegador:

- consulta uma NF-e por vez;
- mostra DANFE em popup;
- permite imprimir/salvar PDF;
- permite baixar XML;
- diferencia fila ocupada de bloqueio `656`;
- usa o tempo/horário retornado pelo servidor para orientar o usuário.

A consulta em lote foi removida para reduzir complexidade operacional e risco de consumo indevido.

## Auditoria

Cada operação fiscal compartilhada pode registrar:

- horário UTC;
- fingerprint curta SHA-256 da chave;
- status interno;
- `cStat`;
- indicação de cache;
- duração.

A auditoria não registra XML, chave de acesso completa, certificado, chave privada, CPF/CNPJ nem a mensagem integral da SEFAZ.

## Release e CI

O caminho oficial de publicação é apenas o workflow manual **Release Bridge**.

Antes da publicação ele executa:

- testes .NET;
- regressão Fernando Klein;
- regressão do feedback fiscal;
- regressão de prontidão de release;
- build;
- publish Windows x64 autocontido.

A regressão de prontidão impede que workflows/testes passem a depender de certificados `.pfx/.p12`, credenciais fiscais ou transporte SEFAZ real.

## Fora do escopo

- publicação na internet;
- hospedagem em nuvem;
- envio do certificado para navegadores, GitHub ou Cloudflare;
- abertura de porta no roteador;
- banco externo;
- consulta em lote;
- autenticação própria por senha no estado atual.

## Critérios de aceitação

Automatizados:

- mesma chave concorrente produz uma operação fiscal compartilhada;
- chamadas de chaves diferentes são serializadas;
- fila possui limite e retorno controlado;
- `656` persiste entre instâncias e impede novo transporte;
- bootstrap não expõe certificado/XML;
- CI não usa credenciais fiscais reais;
- build e pacote Windows passam.

Físicos, após gerar a próxima release:

- painel central apresenta Rede/Servidor/Firewall OK;
- acesso local em `127.0.0.1:17345` funciona;
- um segundo PC acessa o IPv4 exibido pela Central;
- o segundo PC consulta uma NF-e conhecida sem instalar o certificado A1;
- XML e DANFE funcionam no cliente;
- o certificado permanece somente no PC central.
