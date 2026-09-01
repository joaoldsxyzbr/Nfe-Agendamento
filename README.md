# NFe Agendamento

Ferramenta interna para consultar, visualizar e baixar NF-e por chave usando o certificado A1 já instalado no Windows.

## Versão atual

**v0.1.5**

A versão atual consolida a aplicação Windows portátil, execução local pela bandeja e a visualização do DANFE em um popup dedicado.

## Objetivo

O projeto permanece simples e direto. Por padrão, o navegador acessa somente `http://127.0.0.1:17345`. Para uso da equipe, o PC central pode ser iniciado explicitamente com `--lan`; nesse modo, os demais computadores acessam `http://nfeagendamento.local:17345` e entram com a senha numérica da central.

## Funcionalidades atuais

- host local fixo em loopback por padrão, com modo LAN explícito no PC central;
- autenticação local por senha numérica para acessos pela LAN;
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

Essa evolução completa do layout fiscal ainda é trabalho planejado e não deve ser considerada concluída na v0.1.5.

## Teste real no Windows

1. Baixe `Nfe-Agendamento-win-x64.zip` na release mais recente do GitHub.
2. Extraia o ZIP em uma pasta local, por exemplo `C:\NfeAgendamento`.
3. Execute `NfeAgendamento.App.exe`.
4. Abra `http://127.0.0.1:17345` no mesmo PC, caso o navegador não seja aberto automaticamente.
5. No primeiro uso do PC central, crie a senha numérica de seis dígitos.
6. Selecione o certificado A1 instalado no usuário atual.
7. Consulte uma chave conhecida.
8. Teste o download do XML.
9. Clique em `Visualizar DANFE` e confirme que a nota abre no popup dedicado.
10. Teste `Imprimir / Salvar PDF` e confirme que somente o DANFE aparece na impressão.
11. Teste o lote somente com chaves conhecidas e confirme o ZIP.

### Acesso pelos demais computadores

1. No PC central, crie um atalho que execute `NfeAgendamento.App.exe --lan`.
2. Permita no Firewall do Windows somente a porta TCP `17345` na rede privada da empresa.
3. Nos demais PCs, abra `http://nfeagendamento.local:17345` e informe a senha numérica criada no central.
4. Se a rede bloquear mDNS, use o endereço IPv4 do PC central, por exemplo `http://192.168.1.50:17345`.
5. Não copie o certificado A1 nem a pasta de dados para os demais computadores.

O pacote não instala serviço e não envia certificado ou XML para a nuvem. Sem `--lan`, a interface fiscal continua restrita ao próprio PC. Para encerrar, use `Sair` no ícone da bandeja.

## Segurança

- certificado e chave privada permanecem no Windows Certificate Store;
- o modo LAN só deve ser ativado na rede privada da empresa;
- acessos pela LAN exigem sessão autenticada;
- XMLs em repouso são criptografados localmente;
- o conteúdo do XML exibido no DANFE deve ser escapado antes de entrar no HTML;
- o DANFE completo planejado não deve depender de serviço externo para gerar código de barras ou processar dados fiscais;
- CI nunca consulta a SEFAZ real nem usa certificado/XML fiscal real.

## Releases

A versão atual publicada é **v0.1.5**, com pacote `Nfe-Agendamento-win-x64.zip`.

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
