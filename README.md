# NFe Agendamento

Ferramenta interna para consultar, visualizar e baixar NF-e por chave usando o certificado A1 já instalado no Windows.

## Versão atual

**v0.1.4**

A versão atual consolida a aplicação Windows portátil, execução local pela bandeja e a visualização do DANFE em um popup dedicado.

## Objetivo

O projeto deve permanecer simples e direto. Cada PC roda sua própria instalação local e o navegador acessa somente `http://127.0.0.1:17345`.

## Funcionalidades atuais

- host local fixo em loopback;
- proteção por `Host`, `Origin` e CSRF;
- seleção de certificado A1 no Windows Certificate Store;
- identidade fiscal derivada do certificado;
- consulta única por chave via `consChNFe`;
- cache de XML criptografado com DPAPI e retenção de 24 horas;
- tratamento persistente de `cStat=656`;
- tratamento de `137`, `138` com XML e `138` sem XML;
- retry limitado para falhas transitórias de rede;
- consulta em lote sequencial de até 100 chaves com download em ZIP;
- download do XML da NF-e;
- visualização do DANFE;
- DANFE aberto em popup/modal dedicado, sem ocupar a tela principal;
- fechamento do popup pelo botão, tecla `Esc` ou clique no fundo;
- popup adaptado para ocupar a tela no celular;
- impressão e salvamento em PDF mostrando somente o DANFE;
- ações de bandeja para abrir o sistema, configurar certificado e verificar atualização;
- atualização do aplicativo pelo fluxo de releases do GitHub;
- pacote Windows x64 autocontido e portátil;
- CI sem acesso à SEFAZ real.

## DANFE

O DANFE atual já possui visualização isolada em popup e impressão própria.

Está especificada a evolução para um DANFE fiscal mais completo, inspirado na organização e riqueza de informações do FSist, sem copiar marca ou conteúdo proprietário. O objetivo é incluir, conforme os dados realmente existentes no XML:

- canhoto;
- cabeçalho fiscal tradicional;
- chave de acesso e código de barras Code 128;
- protocolo de autorização;
- emitente e destinatário completos;
- pagamentos;
- cálculo de impostos mais completo;
- ICMS, ICMS-ST, IPI, PIS e COFINS quando presentes;
- CST/CSOSN, NCM e CFOP nos produtos;
- transporte, volumes e pesos;
- informações adicionais e área reservada ao fisco;
- paginação automática com `Folha X/Y` e continuação dos produtos.

Essa evolução completa do layout fiscal ainda é trabalho planejado e não deve ser considerada concluída na v0.1.4.

## Teste real no Windows

1. Baixe `Nfe-Agendamento-win-x64.zip` na release mais recente do GitHub.
2. Extraia o ZIP em uma pasta local, por exemplo `C:\NfeAgendamento`.
3. Execute `NfeAgendamento.App.exe`.
4. Abra `http://127.0.0.1:17345` no mesmo PC, caso o navegador não seja aberto automaticamente.
5. Selecione o certificado A1 instalado no usuário atual.
6. Consulte uma chave conhecida.
7. Teste o download do XML.
8. Clique em `Visualizar DANFE` e confirme que a nota abre no popup dedicado.
9. Teste `Imprimir / Salvar PDF` e confirme que somente o DANFE aparece na impressão.
10. Teste o lote somente com chaves conhecidas e confirme o ZIP.

O pacote não instala serviço, não abre a interface fiscal para a rede e não envia certificado ou XML para a nuvem. Para encerrar, use `Sair` no ícone da bandeja.

## Segurança

- certificado e chave privada permanecem no Windows Certificate Store;
- nenhuma interface fiscal deve ficar acessível pela LAN;
- XMLs em repouso são criptografados localmente;
- o conteúdo do XML exibido no DANFE deve ser escapado antes de entrar no HTML;
- o DANFE completo planejado não deve depender de serviço externo para gerar código de barras ou processar dados fiscais;
- CI nunca consulta a SEFAZ real nem usa certificado/XML fiscal real.

## Releases

A versão atual publicada é **v0.1.4**, com pacote `Nfe-Agendamento-win-x64.zip`.

As releases são geradas pelo fluxo automatizado do GitHub após as alterações entrarem na `main`.

## Próximos passos

- implementar o DANFE fiscal completo conforme a especificação aprovada;
- validar o layout com XMLs reais conhecidos sem armazenar dados fiscais reais no repositório;
- manter os testes e CI verdes antes de novas releases;
- instalador assinado e piloto nos PCs permanecem como marcos separados.

## Documentação técnica

### Aplicação local

- Design: `docs/superpowers/specs/2026-08-31-local-browser-nfe-design.md`
- Plano: `docs/superpowers/plans/2026-08-31-foundation-single-lookup.md`

### DANFE completo

- Design aprovado: `docs/superpowers/specs/2026-08-31-danfe-fsist-design.md`

A documentação deve acompanhar as alterações do projeto para que a `main` represente sempre o comportamento efetivamente publicado.