# NFe Agendamento — Central interna pela rede

## Objetivo

Permitir que a equipe use o NFe Agendamento pelo navegador, mantendo o certificado A1, os XMLs e as consultas fiscais em um único PC central.

## Decisão arquitetural

O aplicativo Windows continuará hospedando a interface web e o backend, mas o host será configurável para escutar na rede interna. O certificado A1 permanece no Windows Certificate Store do PC central; nenhum certificado, chave privada ou XML será enviado aos navegadores.

Os demais computadores acessarão o endereço HTTP interno do PC central. O backend centralizará cache, cooldown, fila e deduplicação, de modo que apenas uma operação fiscal por vez seja enviada à SEFAZ.

## Segurança

- O modo padrão continuará restrito a `127.0.0.1`.
- A exposição na LAN será uma opção explícita de configuração do PC central.
- O acesso pela LAN exigirá autenticação local antes de consultar ou baixar documentos.
- Endpoints de saúde e arquivos estáticos não permitirão operações fiscais sem sessão válida.
- O certificado A1 nunca será exportado, enviado ao cliente ou armazenado pelo app fora do repositório de certificados do Windows.
- O app exibirá o endereço de acesso e o estado do servidor na bandeja.

## Fluxo fiscal

1. O navegador envia uma solicitação autenticada ao PC central.
2. O backend valida a chave e verifica o cache criptografado.
3. Solicitações iguais em andamento são compartilhadas ou aguardam o resultado existente.
4. Uma fila fiscal única serializa as consultas à SEFAZ.
5. Respostas `138` são armazenadas no cache; `656` cria cooldown persistente de uma hora.
6. O navegador recebe XML, DANFE ou ZIP sem contato direto com a SEFAZ.

## Escopo da primeira implementação

- Configuração segura de escuta local ou LAN.
- Sessão local com senha numérica configurada no PC central.
- Middleware de autenticação para operações da equipe.
- Fila/deduplicação para consultas únicas e em lote.
- Respostas claras para `429`, incluindo horário de desbloqueio.
- Testes de segurança, concorrência, autenticação e preservação do modo local.
- Atualização do README e instruções de instalação nos demais computadores.

## Fora do escopo

- Publicação na internet ou hospedagem em nuvem.
- Envio do certificado para Cloudflare, GitHub ou qualquer navegador.
- Abertura automática de portas no roteador.
- Banco de dados externo ou servidor central separado.

## Critérios de aceitação

- O PC central consulta usando o certificado A1 instalado.
- Dois computadores da LAN conseguem acessar o site autenticado.
- Consultas simultâneas à mesma chave não geram chamadas duplicadas à SEFAZ.
- O modo local continua funcionando quando a exposição LAN está desativada.
- O servidor rejeita chamadas fiscais sem autenticação.
- O lote usa a mesma fila e o mesmo cooldown da consulta única.
