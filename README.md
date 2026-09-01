# NFe Agendamento

Aplicativo Windows interno para consultar, visualizar e baixar NF-e usando o certificado A1 instalado no PC central.

## Versão publicada

**v0.1.9**

- [Baixar o pacote Windows x64](https://github.com/joaoldsxyzbr/Nfe-Agendamento/releases/download/v0.1.9/Nfe-Agendamento-win-x64.zip)
- [Ver a release v0.1.9](https://github.com/joaoldsxyzbr/Nfe-Agendamento/releases/tag/v0.1.9)

O pacote é autocontido e não exige instalação do .NET.

## Como o sistema funciona

O NFe Agendamento deve ser executado em um único PC central da empresa. Esse computador mantém:

- o certificado A1 no Windows Certificate Store;
- o cache criptografado dos XMLs;
- a fila de consultas;
- o estado de bloqueio fiscal da SEFAZ;
- o site interno acessado pelos demais computadores.

Os outros PCs não precisam ter o certificado instalado. Eles acessam a central pelo navegador.

```text
Computadores da equipe
        ↓
http://nfeagendamento.local:17345
        ↓
PC central com o NFe Agendamento
        ↓
Certificado A1 + SEFAZ
```

Nenhum certificado, chave privada ou XML é enviado para a nuvem.

## Recursos

- consulta única por chave de acesso de 44 dígitos;
- consulta em lote de até 100 chaves;
- download de XML individual;
- download dos XMLs em ZIP;
- visualização do DANFE em popup;
- impressão e salvamento do DANFE em PDF;
- cache local criptografado por DPAPI;
- validade do cache de 24 horas;
- fila fiscal única;
- deduplicação de consultas simultâneas para a mesma chave;
- tratamento de `137`, `138` e `656`;
- cooldown persistente de uma hora após `cStat=656`;
- retry limitado apenas para falhas transitórias de rede;
- proteção CSRF, validação de Host e Origin;
- autenticação por senha numérica no modo LAN;
- domínio interno via mDNS.

## Instalação no PC central

1. Baixe o ZIP da release.
2. Extraia, por exemplo, em `C:\NfeAgendamento`.
3. Execute `NfeAgendamento.App.exe`.
4. Para uso apenas nesse PC, abra `http://127.0.0.1:17345`.
5. Selecione o certificado A1 válido.
6. Informe a UF autora correta.
7. Faça uma consulta de teste com uma chave conhecida.

O certificado deve estar instalado no perfil do Windows que executará o aplicativo. O app não exporta nem copia a chave privada.

## Ativar o modo central pela rede

No PC central, execute:

```text
NfeAgendamento.App.exe --lan
```

O modo LAN é opt-in. Sem `--lan`, o app escuta somente em `127.0.0.1`.

No primeiro acesso local em modo LAN:

1. Abra `http://127.0.0.1:17345`.
2. Crie uma senha numérica de seis dígitos.
3. Configure o certificado A1.
4. Mantenha o aplicativo aberto na bandeja.

## Acesso pelos demais computadores

Abra:

```text
http://nfeagendamento.local:17345
```

Informe a senha numérica criada no PC central.

Se o domínio não resolver, use o IPv4 do PC central:

```text
http://192.168.1.50:17345
```

O domínio depende de mDNS na rede. O firewall e a política de rede precisam permitir TCP 17345 e, para a descoberta automática, UDP 5353 na rede privada.

## Firewall do Windows

No PC central, permita somente na rede privada:

- TCP 17345 para o aplicativo;
- UDP 5353 para descoberta mDNS, se o domínio `nfeagendamento.local` não for resolvido.

Não publique essa porta na internet e não habilite regra para redes públicas.

## Segurança e dados

- o certificado A1 permanece no Windows Certificate Store;
- a chave privada não é enviada aos navegadores;
- os XMLs ficam no PC central;
- o cache é criptografado com DPAPI do usuário do Windows;
- acessos LAN exigem sessão autenticada;
- operações de consulta exigem CSRF válido;
- o modo padrão continua restrito ao loopback;
- o acesso por IP é apenas fallback do domínio interno;
- não há banco externo nem hospedagem em nuvem.

A senha numérica é armazenada como um verificador protegido por DPAPI, não em texto puro.

## Consultas e erro 429

O retorno HTTP 429 representa o bloqueio fiscal causado por `cStat=656`, chamado pela SEFAZ de consumo indevido. Nesse caso:

1. não repita a consulta;
2. aguarde o horário informado na tela;
3. verifique se outro sistema não está consultando o mesmo CNPJ;
4. confirme que todos os usuários estão usando a central, e não cópias independentes do aplicativo.

A central impede chamadas simultâneas duplicadas da mesma chave, mas não pode impedir outro sistema externo de consultar o mesmo CNPJ.

## Atualização

Para atualizar:

1. encerre a versão antiga pelo menu da bandeja;
2. baixe a nova release;
3. extraia em uma pasta de aplicação;
4. execute a nova versão;
5. confirme o certificado e a UF autora.

Não substitua a pasta de dados do usuário. O cache e o estado fiscal ficam em:

```text
%LOCALAPPDATA%\NfeAgendamento
```

## Parar a central

Use `Sair` no menu do ícone da bandeja. Isso encerra o servidor e o anúncio mDNS.

## Desenvolvimento e validação

A solução usa .NET 8, Windows Forms, ASP.NET Core minimal APIs, xUnit e JavaScript sem framework.

Antes de publicar uma alteração:

```bash
dotnet restore Nfe-Agendamento.sln
dotnet test Nfe-Agendamento.sln -c Release
dotnet build Nfe-Agendamento.sln -c Release
```

O CI executa testes e build em Windows. Ele não usa certificado real, XML real nem consulta a SEFAZ.

## Documentação técnica

- [Guia operacional da central](docs/CENTRAL-LAN.md)
- [Especificação da arquitetura](docs/superpowers/specs/2026-09-01-central-lan-architecture-design.md)
- [Plano de implementação](docs/superpowers/plans/2026-09-01-central-lan-architecture.md)
- [Design do navegador local](docs/superpowers/specs/2026-08-31-local-browser-nfe-design.md)
- [Design do DANFE](docs/superpowers/specs/2026-08-31-danfe-fsist-design.md)
