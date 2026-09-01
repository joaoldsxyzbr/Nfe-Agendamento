# NFe Agendamento

Aplicativo Windows interno para consultar, visualizar e baixar NF-e usando o certificado A1 instalado no PC central.

## Versão publicada

**v0.1.16**

- [Baixar o pacote Windows x64](https://github.com/joaoldsxyzbr/Nfe-Agendamento/releases/download/v0.1.16/Nfe-Agendamento-win-x64.zip)
- [Ver a release v0.1.16](https://github.com/joaoldsxyzbr/Nfe-Agendamento/releases/tag/v0.1.16)

O pacote é autocontido e não exige instalação do .NET.

> A `main` já contém o novo painel Windows da Central. Essas mudanças ainda precisam de uma nova release para chegar ao pacote publicado.

## Como o sistema funciona

O NFe Agendamento deve ser executado em um único PC central da empresa. Esse computador mantém:

- o certificado A1 no Windows Certificate Store;
- o cache criptografado dos XMLs;
- a coordenação das consultas fiscais;
- o estado de bloqueio fiscal da SEFAZ;
- o servidor interno acessado pelos demais computadores;
- o painel Windows que controla se a Central está disponível para a rede.

Os outros PCs não precisam ter o certificado instalado. Eles acessam a central pelo navegador.

```text
Computadores da equipe
        ↓
http://IP-DO-PC-CENTRAL:17345
        ↓
PC central com o NFe Agendamento
        ↓
Certificado A1 + SEFAZ
```

Nenhum certificado, chave privada ou XML é enviado para a nuvem.

## Painel Windows da Central

Ao abrir o `NfeAgendamento.App.exe`, o aplicativo mostra a janela **Central NFe Agendamento**.

Ela informa:

- se a Central está ativa ou parada;
- IPv4 detectado no PC;
- porta `17345`;
- endereço que deve ser usado pelos outros computadores.

Ações principais:

- **Iniciar Central**: libera o acesso pela rede interna;
- **Parar Central**: bloqueia novas conexões remotas, mantendo o sistema local disponível;
- **Abrir sistema**: abre a interface web local em `http://127.0.0.1:17345`.

O estado da Central é persistido em `%LOCALAPPDATA%\NfeAgendamento\state\central.json`. Em uma instalação nova, a Central inicia habilitada.

Fechar a janela não encerra o aplicativo: ele continua na bandeja. Use **Sair** no menu da bandeja para encerrar completamente.

## Recursos

- consulta individual por chave de acesso de 44 dígitos;
- download de XML individual;
- visualização do DANFE em popup;
- impressão e salvamento do DANFE em PDF;
- cache local criptografado por DPAPI;
- validade do cache de 24 horas;
- coordenação fiscal única no PC central;
- deduplicação de consultas simultâneas para a mesma chave;
- tratamento de `137`, `138` e `656`;
- cooldown persistente de uma hora após `cStat=656`;
- retry limitado apenas para falhas transitórias de rede;
- proteção CSRF, validação de Host e Origin;
- controle do acesso remoto pelo painel Windows da Central;
- domínio interno via mDNS;
- mapeamento interno de produtos da Fernando Klein sem alterar o XML original.

A consulta em lote foi removida para reduzir risco de consumo indevido e manter a operação da central simples e previsível para vários computadores.

## Mapeamento Fernando Klein

O mapeamento é aplicado somente quando o CPF/CNPJ do emitente corresponde ao fornecedor configurado. O código fiscal `cProd` da NF-e é sempre preservado e o código interno é exibido separadamente no DANFE.

As descrições passam por normalização de acentos, espaços, pontuação e prefixos como `VERDURAS -`. O vínculo continua estrito por aliases cadastrados: não existe aproximação automática ou fuzzy matching.

Se um item não existir no catálogo, o sistema mantém apenas o `cProd` original e registra no console do navegador um aviso com a descrição e o código do fornecedor. Também registra um resumo da NF.

Esses logs não incluem XML completo, certificado, chave privada nem CPF/CNPJ do emitente.

## Instalação no PC central

1. Baixe o ZIP da release.
2. Extraia, por exemplo, em `C:\NfeAgendamento`.
3. Execute `NfeAgendamento.App.exe`.
4. No painel da Central, confirme que o status está **Central ativa**.
5. Clique em **Abrir sistema**.
6. Selecione o certificado A1 válido.
7. Informe a UF autora correta.
8. Faça uma consulta de teste com uma chave conhecida.

O certificado deve estar instalado no perfil do Windows que executará o aplicativo. O app não exporta nem copia a chave privada.

## Acesso pelos demais computadores

Use o endereço mostrado no painel Windows. Exemplo:

```text
http://10.0.0.29:17345
```

O endereço `127.0.0.1` nunca deve ser usado em outro PC, pois ele sempre aponta para a própria máquina que está acessando.

O endereço por nome continua disponível quando o mDNS funciona na rede:

```text
http://nfeagendamento.local:17345
```

O acesso remoto só é aceito enquanto o painel indicar **Central ativa**.

## Firewall do Windows

O Bloco 1 não cria nem altera regras de firewall automaticamente. No PC central, a rede ainda precisa permitir:

- TCP `17345` para o aplicativo;
- UDP `5353` para descoberta mDNS, se o domínio `nfeagendamento.local` for usado.

A automação e o diagnóstico de firewall pertencem ao próximo bloco de rede.

Não publique essas portas na internet e não habilite regras amplas para redes públicas.

## Segurança e dados

- o certificado A1 permanece no Windows Certificate Store;
- a chave privada não é enviada aos navegadores;
- os XMLs ficam no PC central;
- o cache é criptografado com DPAPI do usuário do Windows;
- operações de alteração e consulta fiscal exigem CSRF válido;
- Host e Origin são validados;
- conexões remotas são bloqueadas imediatamente quando a Central é parada;
- o sistema local continua disponível em `127.0.0.1:17345`;
- não há banco externo nem hospedagem em nuvem.

O aplicativo não possui autenticação própria. Portanto, enquanto a Central estiver ativa, qualquer computador autorizado pela rede interna que consiga alcançar a porta 17345 poderá acessar a interface. Mantenha a porta restrita à rede da empresa.

## Consultas e erro 429

O retorno HTTP 429 representa o bloqueio fiscal causado por `cStat=656`, chamado pela SEFAZ de consumo indevido. Nesse caso:

1. não repita a consulta;
2. aguarde o horário informado na tela;
3. verifique se outro sistema não está consultando o mesmo CNPJ;
4. confirme que todos os usuários estão usando a central, e não cópias independentes do aplicativo.

A central impede chamadas simultâneas duplicadas da mesma chave e serializa o acesso fiscal, mas não pode impedir outro sistema externo de consultar o mesmo CNPJ.

## Atualização

Para atualizar:

1. encerre a versão antiga pelo menu da bandeja;
2. baixe a nova release;
3. extraia em uma pasta de aplicação;
4. execute a nova versão;
5. confirme o certificado e a UF autora.

Não substitua a pasta de dados do usuário. O cache, o estado fiscal e a configuração da Central ficam em:

```text
%LOCALAPPDATA%\NfeAgendamento
```

## Desenvolvimento e validação

A solução usa .NET 8, Windows Forms, ASP.NET Core minimal APIs, xUnit e JavaScript sem framework.

Antes de publicar uma alteração:

```bash
dotnet restore Nfe-Agendamento.sln
dotnet test Nfe-Agendamento.sln -c Release
node tests/js/product-mapping-regression.test.js
dotnet build Nfe-Agendamento.sln -c Release
```

O CI executa testes, regressão do mapeamento, build e geração do pacote Windows. Execuções antigas da mesma branch são canceladas quando uma nova começa.

## Criar uma release

A release é criada manualmente pelo GitHub Actions:

1. abra **Actions**;
2. escolha **Release Bridge**;
3. clique em **Run workflow**;
4. informe uma versão maior que a última publicada;
5. execute.

O workflow rejeita versão repetida ou menor/igual à última publicada, roda testes e regressão antes da publicação e impede duas releases simultâneas.

## Documentação técnica

- [Guia operacional da central](docs/CENTRAL-LAN.md)
- [Especificação da arquitetura](docs/superpowers/specs/2026-09-01-central-lan-architecture-design.md)
- [Plano de implementação](docs/superpowers/plans/2026-09-01-central-lan-architecture.md)
- [Design do navegador local](docs/superpowers/specs/2026-08-31-local-browser-nfe-design.md)
- [Design do DANFE](docs/superpowers/specs/2026-08-31-danfe-fsist-design.md)
