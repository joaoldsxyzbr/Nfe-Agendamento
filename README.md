# NFe Agendamento

Aplicativo Windows interno para consultar, visualizar e baixar NF-e usando o certificado A1 instalado no PC central.

## Versão publicada

**v0.1.16**

- [Baixar o pacote Windows x64](https://github.com/joaoldsxyzbr/Nfe-Agendamento/releases/download/v0.1.16/Nfe-Agendamento-win-x64.zip)
- [Ver a release v0.1.16](https://github.com/joaoldsxyzbr/Nfe-Agendamento/releases/tag/v0.1.16)

O pacote é autocontido e não exige instalação do .NET.

> A `main` já contém o painel Windows da Central e o diagnóstico de rede/firewall. Essas mudanças ainda precisam de uma nova release para chegar ao pacote publicado.

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
- endereço que deve ser usado pelos outros computadores;
- status da interface de rede;
- se o servidor está realmente ouvindo na LAN;
- status da regra do Firewall do Windows;
- um resumo objetivo do que precisa ser corrigido quando a rede não está pronta.

Ações principais:

- **Iniciar Central**: libera o acesso pela rede interna;
- **Parar Central**: bloqueia novas conexões remotas, mantendo o sistema local disponível;
- **Abrir sistema**: abre a interface web local em `http://127.0.0.1:17345`;
- **Configurar firewall**: solicita elevação do Windows e cria a regra necessária somente para TCP `17345`, somente no perfil **Privado** e vinculada ao executável atual.

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
- seleção mais robusta do IPv4 da interface de rede;
- diagnóstico de listener TCP `17345`;
- verificação e configuração da regra privada do Firewall do Windows;
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
5. Confira os indicadores **Rede**, **Servidor** e **Firewall**.
6. Se o Firewall indicar **Precisa configurar**, clique em **Configurar firewall** e autorize o UAC do Windows.
7. Clique em **Abrir sistema**.
8. Selecione o certificado A1 válido.
9. Informe a UF autora correta.
10. Faça uma consulta de teste com uma chave conhecida.

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

O painel verifica a regra usada pela Central. Quando necessário, o botão **Configurar firewall** cria uma regra com estas restrições:

- entrada TCP;
- porta local `17345`;
- perfil de rede **Privado**;
- somente o executável atual do `NfeAgendamento.App.exe`;
- nenhuma regra para o perfil Público.

A configuração exige autorização do UAC porque altera o Firewall do Windows. Se o computador for administrado por política corporativa e a alteração for bloqueada, o painel continuará indicando que o firewall precisa de atenção; nesse caso, a liberação deve ser feita pelo administrador da rede.

O UDP `5353` usado pelo nome `nfeagendamento.local` continua opcional. Se o mDNS for bloqueado pela rede, use diretamente o IPv4 mostrado no painel.

Não publique a porta `17345` na internet e não crie regra ampla para redes públicas.

## Segurança e dados

- o certificado A1 permanece no Windows Certificate Store;
- a chave privada não é enviada aos navegadores;
- os XMLs ficam no PC central;
- o cache é criptografado com DPAPI do usuário do Windows;
- operações de alteração e consulta fiscal exigem CSRF válido;
- Host e Origin são validados;
- conexões remotas são bloqueadas imediatamente quando a Central é parada;
- o sistema local continua disponível em `127.0.0.1:17345`;
- a regra automática do firewall fica limitada à rede privada, porta `17345` e executável atual;
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

Como a regra do firewall é vinculada ao caminho do executável, se a aplicação for movida para outra pasta o painel pode pedir para configurar a regra novamente. Isso é esperado e evita liberar executáveis diferentes pela mesma regra.

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
