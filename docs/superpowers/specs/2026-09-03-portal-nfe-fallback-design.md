# Contingência pelo Portal NF-e — Design

## Objetivo

Adicionar ao NFe Agendamento uma segunda rota, manual e segura, para obtenção de XML quando a consulta automática `NFeDistribuicaoDFe/consChNFe` estiver bloqueada (especialmente cStat 656), sem copiar o certificado A1 para PCs clientes e sem automatizar ou contornar hCaptcha.

## Fluxo preservado

O fluxo principal não muda:

1. validar chave;
2. consultar cache criptografado de 24 horas;
3. serializar/deduplicar consulta;
4. respeitar cooldown fiscal;
5. consultar `NFeDistribuicaoDFe` via `consChNFe`;
6. validar XML retornado;
7. gravar no mesmo cache criptografado;
8. gerar DANFE localmente.

## Novo fluxo de contingência

Somente no PC configurado como Central:

1. uma consulta retorna `cStat 656`;
2. a interface mostra `Consultar pela Fazenda`;
3. o usuário aciona a contingência;
4. o backend local valida que este PC é a Central e que a chave é válida;
5. uma janela WinForms com WebView2 abre o Portal Nacional da NF-e em `consultaRecaptcha.aspx?tipoConsulta=resumo`;
6. o aplicativo preenche apenas a chave de acesso; o hCaptcha continua totalmente manual;
7. quando o Portal solicitar autenticação por certificado, o WebView2 seleciona exclusivamente o certificado já configurado no NFe Agendamento, comparando o thumbprint;
8. quando o Portal iniciar o download do XML, o WebView2 redireciona o arquivo para um caminho temporário controlado;
9. o XML é lido com limite de tamanho, DTD e resolução externa proibidos, e deve conter `infNFe/@Id = NFe{chave}`;
10. somente XML válido entra no `EncryptedXmlCache` existente;
11. a janela informa sucesso e pode ser fechada; uma nova consulta da mesma chave passa a retornar do cache sem chamar `consChNFe`.

## PCs clientes

A contingência não abre WebView2 nos PCs clientes. O certificado continua existindo somente no PC Central. Em caso de 656, o cliente recebe a mensagem normal de bloqueio e a interface orienta que a consulta alternativa deve ser feita no Central.

## Segurança

- não automatizar, resolver ou contornar hCaptcha;
- aceitar navegação inicial somente no domínio oficial `www.nfe.fazenda.gov.br`;
- selecionar certificado apenas quando o host solicitante for o domínio oficial da NF-e;
- comparar o thumbprint do certificado solicitado pelo WebView2 com o thumbprint configurado no `CertificateService`;
- interceptar como XML de contingência somente downloads originados de `www.nfe.fazenda.gov.br/portal/downloadNFe.aspx`;
- usar arquivo temporário e apagá-lo após importação ou falha;
- limitar XML a 10 MiB;
- `DtdProcessing = Prohibit` e `XmlResolver = null`;
- exigir que a chave do XML corresponda exatamente à chave solicitada;
- nunca persistir senha/PFX; continuar usando o Windows Certificate Store.

## Concorrência e UX

A Central permitirá uma janela de contingência por vez. Uma segunda tentativa enquanto a janela estiver aberta retorna estado `busy` sem iniciar outro navegador.

O endpoint de abertura retorna imediatamente após iniciar a janela; o captcha/download continuam sendo uma interação local no PC Central. O botão só aparece para o 656 no próprio Central.

## Dependência

Usar `Microsoft.Web.WebView2` estável. A versão selecionada para implementação é `1.0.4191.47` (estável publicada em 28/08/2026). Se o WebView2 Runtime não estiver instalado, a API local deve responder com erro de pré-requisito em vez de falhar silenciosamente.

## Fora do escopo

- `distNSU`/sincronização automática de documentos;
- automação de captcha;
- scraping em background da consulta pública;
- instalação de certificado nos PCs clientes;
- alteração do cooldown de 656;
- alteração do protocolo da fila compartilhada;
- substituição de `consChNFe` como método principal.

## Critérios de aceite

- lookup normal e cache de 24h continuam idênticos;
- 656 não dispara nova chamada automática à SEFAZ;
- no Central, 656 oferece contingência pelo Portal;
- no cliente, 656 não expõe certificado nem tenta abrir o Portal com A1;
- chave é pré-preenchida, captcha permanece manual;
- certificado configurado é selecionado automaticamente apenas no domínio oficial;
- XML baixado só é aceito se corresponder à chave;
- XML importado aparece em consulta seguinte via cache;
- falhas de WebView2, certificado, download e XML inválido são tratadas sem afetar o processo principal;
- `dotnet test`, regressões Node, `dotnet build` e `dotnet publish` permanecem verdes.