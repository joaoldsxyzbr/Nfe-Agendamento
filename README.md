# NFe Agendamento

Aplicativo Windows interno para consultar, visualizar e baixar NF-e usando o certificado A1 instalado no PC central.

## Versão publicada

**v0.1.16**

- [Baixar o pacote Windows x64](https://github.com/joaoldsxyzbr/Nfe-Agendamento/releases/download/v0.1.16/Nfe-Agendamento-win-x64.zip)
- [Ver a release v0.1.16](https://github.com/joaoldsxyzbr/Nfe-Agendamento/releases/tag/v0.1.16)

O pacote é autocontido e não exige instalação do .NET.

> A `main` já contém o painel Windows da Central, o diagnóstico de rede/firewall e o reforço da fila fiscal. Essas mudanças ainda precisam de uma nova release para chegar ao pacote publicado.

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
- fila fiscal serializada e limitada a 12 operações únicas admitidas;
- deduplicação de consultas simultâneas para a mesma chave;
- proteção da fila contra novas consultas após `cStat=656`;
- tratamento de `137`, `138` e `656`;
- cooldown persistente de uma hora após `cStat=656`;
- retry limitado apenas para falhas transitórias de rede;
- auditoria fiscal operacional local sem armazenar a chave de acesso completa;
- proteção CSRF, validação de Host e Origin;
- controle do acesso remoto pelo painel Windows da Central;
- seleção mais robusta do IPv4 da interface de rede;
- diagnóstico de listener TCP `17345`;
- verificação e configuração da regra privada do Firewall do Windows;
- domínio interno via mDNS;
- mapeamento interno de produtos da Fernando Klein sem alterar o XML original.

A consulta em lote foi removida para reduzir risco de consumo indevido e manter a operação da central simples e previsível para vários computadores.

## Fila e proteção fiscal

Todas as consultas externas à SEFAZ passam por uma única fila no PC central. O limite padrão é de **12 operações únicas admitidas ao mesmo tempo**: uma pode estar executando e até 11 podem aguardar.

Consultas simultâneas da **mesma chave** são deduplicadas antes de entrar nessa fila. Portanto, vários cliques ou vários computadores pedindo a mesma NF-e compartilham a mesma operação fiscal e não consomem vagas adicionais.

Quando o limite é atingido, uma nova chave não fica acumulada indefinidamente. A API responde:

- HTTP `429`;
- status `fila_ocupada`;
- cabeçalho `Retry-After: 5`.

Esse retorno é diferente do bloqueio `cStat=656`. Quando a SEFAZ retorna `656`, a Central grava um cooldown de uma hora. Operações que já estavam aguardando na fila verificam novamente esse estado antes de chamar a SEFAZ e são bloqueadas localmente sem gerar uma nova consulta fiscal.

A comunicação fiscal também possui limites defensivos:

- no máximo 3 tentativas quando a falha é transitória;
- espera de 2 segundos antes da segunda tentativa e 5 segundos antes da terceira;
- timeout de 45 segundos na comunicação com a SEFAZ;
- resposta fiscal limitada a 10 MB;
- corpo das requisições locais limitado a 256 KB.

Falhas finais de rede, timeout e respostas inválidas são convertidas em erros controlados. Se o arquivo local de cooldown estiver corrompido ou não puder ser validado, a Central falha de forma segura e **não envia uma nova consulta à SEFAZ** até que o estado fiscal seja corrigido.

## Auditoria fiscal local

Cada operação fiscal compartilhada registra um evento operacional em:

```text
%LOCALAPPDATA%\NfeAgendamento\logs\fiscal-audit.jsonl
```

O arquivo gira ao atingir aproximadamente 2 MB e mantém um backup em `fiscal-audit.jsonl.1`.

A auditoria registra somente:

- horário UTC;
- fingerprint SHA-256 de 12 caracteres da chave;
- status interno da operação;
- `cStat`, quando existir;
- indicação de uso do cache;
- duração da operação.

Ela **não grava** XML, chave de acesso completa, certificado, chave privada, CPF/CNPJ nem a mensagem integral retornada pela SEFAZ. Uma falha ao gravar o log também não interrompe a consulta fiscal.

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
- o cache e o cooldown fiscal são protegidos localmente com DPAPI;
- operações de alteração e consulta fiscal exigem CSRF válido;
- Host e Origin são validados;
- conexões remotas são bloqueadas imediatamente quando a Central é parada;
- o sistema local continua disponível em `127.0.0.1:17345`;
- a regra automática do firewall fica limitada à rede privada, porta `17345` e executável atual;
- a auditoria fiscal não armazena XML nem identificadores fiscais completos;
- não há banco externo nem hospedagem em nuvem.

O aplicativo não possui autenticação própria. Portanto, enquanto a Central estiver ativa, qualquer computador autorizado pela rede interna que consiga alcançar a porta 17345 poderá acessar a interface. Mantenha a porta restrita à rede da empresa.

## Retornos HTTP 429

Existem dois cenários diferentes que usam HTTP `429`:

### `fila_ocupada`

A Central já possui 12 operações fiscais únicas admitidas. Aguarde pelo menos os 5 segundos indicados em `Retry-After` e tente novamente. Esse caso **não significa bloqueio da SEFAZ**.

### `consumo_indevido` / `cStat=656`

A SEFAZ bloqueou temporariamente as consultas por consumo indevido. Nesse caso:

1. não repita a consulta;
2. aguarde o horário informado na tela;
3. verifique se outro sistema não está consultando o mesmo CNPJ;
4. confirme que todos os usuários estão usando a central, e não cópias independentes do aplicativo.

O cooldown é persistido por uma hora e vale para toda a Central. A fila revalida esse estado antes de cada acesso à SEFAZ, inclusive para consultas que já estavam aguardando.

## Atualização

Para atualizar:

1. encerre a versão antiga pelo menu da bandeja;
2. baixe a nova release;
3. extraia em uma pasta de aplicação;
4. execute a nova versão;
5. confirme o certificado e a UF autora.

Como a regra do firewall é vinculada ao caminho do executável, se a aplicação for movida para outra pasta o painel pode pedir para configurar a regra novamente. Isso é esperado e evita liberar executáveis diferentes pela mesma regra.

Não substitua a pasta de dados do usuário. O cache, o estado fiscal, a auditoria e a configuração da Central ficam em:

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
