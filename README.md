# NFe Agendamento

Ferramenta interna para consultar e baixar NF-e por chave usando o certificado A1 já instalado no Windows.

## Objetivo

O projeto deve permanecer simples e direto. Cada PC roda sua própria instalação local e o navegador acessa somente `http://127.0.0.1:17345`.

## Marco atual

A fundação da V1 agora cobre:

- host local fixo em loopback;
- proteção por `Host`, `Origin` e CSRF;
- seleção de certificado A1 no Windows Certificate Store;
- identidade fiscal derivada do certificado;
- consulta única por chave via `consChNFe`;
- cache de XML criptografado com DPAPI e retenção de 24 horas;
- tratamento persistente de `cStat=656`;
- tratamento de `137`, `138` com XML e `138` sem XML;
- retry limitado para falhas transitórias de rede;
- interface web mínima para selecionar certificado, consultar, visualizar e baixar XML;
- ações de bandeja para abrir, configurar certificado e verificar atualização;
- consulta em lote sequencial de até 100 chaves com download em ZIP;
- CI sem acesso à SEFAZ real.

## Fora do marco atual

O instalador assinado e o piloto nos três PCs continuam como marcos separados.

## Teste real no Windows

1. Baixe o artefato `Nfe-Agendamento-win-x64` na execução mais recente da PR.
2. Extraia o ZIP em uma pasta local, por exemplo `C:\NfeAgendamento`.
3. Execute `NfeAgendamento.App.exe`.
4. Abra `http://127.0.0.1:17345` no mesmo PC.
5. Selecione o certificado A1 instalado no usuário atual.
6. Teste uma chave conhecida, o download XML, o DANFE e `Imprimir / Salvar PDF`.
7. Teste o lote somente com chaves conhecidas e confirme o ZIP.

O pacote não instala serviço, não abre a porta para a rede e não envia certificado ou XML para a nuvem. Para encerrar, use `Sair` no ícone da bandeja.

## Segurança

- certificado e chave privada permanecem no Windows Certificate Store;
- nenhuma interface fiscal deve ficar acessível pela LAN;
- XMLs em repouso são criptografados localmente;
- CI nunca consulta a SEFAZ real nem usa certificado/XML fiscal real.

## Documentação

- Design: `docs/superpowers/specs/2026-08-31-local-browser-nfe-design.md`
- Plano: `docs/superpowers/plans/2026-08-31-foundation-single-lookup.md`
