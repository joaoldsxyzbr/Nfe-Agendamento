# NFe Agendamento

Aplicativo Windows interno para consultar, visualizar e baixar NF-e usando o certificado A1 instalado somente no PC central.

## Versão publicada

**v0.1.16**

A `main` contém a evolução completa da Central Windows até o **Bloco 6**. A próxima release de teste será a primeira a reunir o fechamento desta etapa, incluindo atualização pelo próprio aplicativo e o acabamento final da Central.

## Arquitetura atual

```text
PCs da equipe
    ↓ navegador
http://IP-DO-PC-CENTRAL:17345
    ↓
Central NFe Agendamento
    ↓
Certificado A1 + cache + fila + SEFAZ
```

O aplicativo Windows é a Central/administrador. O sistema de consulta continua web para os computadores clientes.

O certificado A1 e a chave privada permanecem no Windows Certificate Store do PC central. Cache criptografado, cooldown, configuração e auditoria também permanecem armazenados somente no PC central. O aplicativo não envia esses dados para GitHub ou nuvem. Quando um cliente consulta, visualiza ou baixa uma NF-e, o XML solicitado é entregue pela Central ao navegador através da rede interna HTTP; o certificado e a chave privada nunca são enviados ao cliente.

## Blocos implementados

### Bloco 1 — Central Windows

- janela **Central NFe Agendamento**;
- status ativa/parada;
- IPv4, porta `17345` e URL de acesso;
- ações **Iniciar Central**, **Parar Central** e **Abrir sistema**;
- estado persistido em `%LOCALAPPDATA%\NfeAgendamento\state\central.json`;
- instalação nova inicia habilitada, mas um arquivo de estado existente e inválido desabilita o acesso remoto por segurança;
- operação em bandeja.

### Bloco 2 — Rede e Firewall

- seleção do IPv4 utilizável da rede;
- diagnóstico de interface, listener e firewall;
- servidor preparado para atender a LAN em `17345`;
- Host da LAN aceito somente quando corresponde a um IPv4 realmente atribuído ao PC central, além dos nomes locais explicitamente permitidos;
- configuração via UAC de regra de entrada TCP estável entre atualizações, restrita à porta `17345`, perfis **Domínio/Privado** e origem `LocalSubnet`, sem vínculo ao caminho do executável;
- `nfeagendamento.local` continua opcional via mDNS e anuncia o mesmo IPv4 selecionado pelo painel; o IPv4 é o fallback confiável.

### Bloco 3 — Robustez fiscal

- fila fiscal única e serializada;
- limite de 12 operações únicas admitidas;
- deduplicação de consultas simultâneas da mesma chave;
- fila cheia retorna HTTP `429`, status `fila_ocupada` e `Retry-After: 5`;
- `cStat=656` cria cooldown de uma hora, aplicado primeiro em memória e persistido para sobreviver ao reinício;
- se a persistência falhar após um `656`, o processo atual continua bloqueado em memória;
- consultas já aguardando revalidam o cooldown antes de tocar na SEFAZ;
- até 3 tentativas somente para falhas transitórias: erro de rede sem status HTTP, `408`, `429`, `5xx` e timeout;
- outros erros HTTP `4xx` não são repetidos automaticamente;
- timeout e respostas inválidas viram erros controlados;
- estado fiscal corrompido falha fechado, sem nova consulta à SEFAZ;
- cache XML corrompido é descartado e tratado como cache miss, sem impedir a consulta;
- auditoria local sem XML, chave completa, certificado ou CPF/CNPJ.

### Bloco 4 — Operação pelos clientes

- bandeja mostra `Acesso: http://IP:17345`;
- ação **Copiar endereço da Central**;
- navegador diferencia **Central ocupada** de bloqueio **SEFAZ / cStat=656**;
- mensagens usam o `Retry-After` e o horário real de desbloqueio;
- administração de certificado é exclusiva do PC central; clientes remotos não podem listar, consultar nem selecionar certificados.

### Bloco 5 — Prontidão de release

- CI e Release Bridge executam testes .NET e todas as regressões JS;
- existe um único caminho oficial de publicação: **Release Bridge** manual;
- workflow legado de release por tag foi removido;
- o Release Bridge faz checkout, testa, empacota e cria a tag/release no **SHA exato do disparo**, sem depender de uma `main` que possa avançar durante a execução;
- regressão impede dependência de certificado `.pfx/.p12`, credencial fiscal ou transporte SEFAZ real nos testes/workflows;
- teste comprova que o cooldown `656` continua válido em uma nova instância do serviço e bloqueia o transporte;
- teste comprova que `/api/bootstrap` expõe somente dados operacionais (`csrfToken`, `lanMode`, `accessUrl`), sem XML ou dados de certificado.

### Bloco 6 — Testes, atualização e acabamento

- cobertura específica do modo Central, persistência e acesso local/remoto;
- testes de inicialização automática sem dependência da flag legada `--lan`;
- testes do diagnóstico de rede/listener e da regra restrita do Firewall do Windows;
- testes de segurança para Host, Origin, CSRF, tamanho de requisição, administração de certificado somente local e bloqueio remoto com a Central parada;
- testes da fila fiscal, liberação de capacidade e rejeição quando cheia;
- testes de concorrência e deduplicação: a mesma chave compartilha uma única operação em andamento e chaves diferentes podem ser coordenadas independentemente;
- testes de cache corrompido, cooldown `656` mesmo sem persistência e retry HTTP seletivo;
- atualizador integrado ao menu da bandeja;
- pacote de atualização aceito somente pela release oficial do GitHub, com validação de tamanho e SHA-256;
- extração protegida contra escrita fora da pasta temporária;
- instalação acontece após o encerramento da instância atual e o aplicativo é reaberto;
- interface Windows simples, com identidade azul/amarelo e sem alterar o fluxo operacional;
- CI obrigatório continua executando testes, regressões, build e geração do pacote Windows.

> A aceitação física da LAN continua obrigatória após gerar a release: instalar no PC central e abrir o endereço exibido pelo painel a partir de outro computador. O CI não consegue reproduzir VLAN, ACL, isolamento Wi-Fi ou políticas reais da rede da empresa.

## Uso no PC central

1. Execute `NfeAgendamento.App.exe`.
2. Confirme **Central ativa**.
3. Confira **Rede**, **Servidor** e **Firewall**.
4. Se necessário, use **Configurar firewall** e autorize o UAC.
5. Abra o sistema local.
6. Selecione o certificado A1 válido e a UF autora.
7. Faça uma consulta individual de teste.

Fechar a janela mantém o aplicativo na bandeja. Para encerrar completamente, use **Sair**.

### Iniciar com o Windows

A opção **Iniciar com o Windows** registra apenas o executável da Central no perfil do usuário atual. Não é mais necessário usar nem preservar `--lan`.

O estado **Central ativa/parada** é salvo separadamente. Assim, iniciar automaticamente o programa não depende de argumentos especiais de linha de comando.

## Uso nos outros PCs

Use o endereço mostrado no painel ou copiado pela bandeja, por exemplo:

```text
http://10.0.0.29:17345
```

Não use `127.0.0.1` em outro computador: esse endereço sempre aponta para o próprio PC que está acessando.

Se a rede suportar mDNS, também pode funcionar:

```text
http://nfeagendamento.local:17345
```

Os computadores clientes precisam apenas de navegador. Não copie nem instale o certificado A1 neles. A configuração e seleção do certificado ficam disponíveis somente no PC central; os clientes usam o sistema para consulta, visualização e download.

Como o acesso entre a Central e os clientes usa HTTP na LAN, o conteúdo da NF-e solicitado trafega pela rede interna sem TLS fornecido pelo aplicativo. Mantenha a porta restrita a uma rede corporativa confiável e nunca a publique na internet.

## Atualização pelo próprio aplicativo

No menu da bandeja, use **Verificar atualização**.

Quando existir uma versão nova, o aplicativo:

1. consulta a release oficial do projeto;
2. exige o pacote `Nfe-Agendamento-win-x64.zip` e um digest SHA-256 válido;
3. pede confirmação antes do download;
4. baixa o pacote via HTTPS;
5. confere tamanho e SHA-256;
6. valida as entradas do ZIP para impedir path traversal;
7. prepara a atualização em diretório temporário;
8. inicia o atualizador, encerra a Central, substitui os arquivos e reabre o executável.

Se o pacote oficial não puder ser validado, a instalação automática não prossegue e o usuário é orientado a usar a release oficial manualmente.

A **v0.1.16 não possui esse novo updater**. Portanto, a primeira instalação da release que introduzir o Bloco 6 deve ser feita manualmente. Depois disso, as versões seguintes poderão validar o fluxo de atualização pelo próprio app de ponta a ponta.

## Consulta fiscal

A consulta em lote foi removida. Todas as consultas são individuais e coordenadas pela Central.

Quando a fila atingir 12 operações únicas, uma nova chave recebe `429 fila_ocupada`. Isso significa apenas que a Central está ocupada.

Quando a SEFAZ retornar `cStat=656`, a Central aplica imediatamente um bloqueio de uma hora em memória e tenta persistir esse estado. Durante o bloqueio, não force novas tentativas; aguarde o horário informado na interface.

Falhas transitórias de comunicação podem ser tentadas até três vezes com backoff. Erros HTTP permanentes de cliente, como `400`, `401`, `403` e `404`, falham na primeira tentativa e não geram repetição automática.

## Cache e dados locais

Os dados ficam em:

```text
%LOCALAPPDATA%\NfeAgendamento
```

Principais itens:

- cache XML criptografado por DPAPI;
- estado/cooldown fiscal criptografado por DPAPI;
- configuração da Central;
- auditoria em `logs\fiscal-audit.jsonl` com rotação aproximada de 2 MB e um backup `.1`.

Se uma entrada do cache XML estiver corrompida, incompatível ou não puder ser validada pelo DPAPI/JSON, ela é apagada e tratada como ausente; a consulta pode seguir normalmente para a fila fiscal e SEFAZ.

A auditoria guarda somente horário UTC, fingerprint curta da chave, status, `cStat`, indicação de cache e duração.

## Segurança

- certificado e chave privada ficam somente no PC central;
- rotas de administração de certificado são bloqueadas para clientes remotos;
- Host e Origin são validados;
- Host remoto precisa corresponder ao IP real da Central ou ao nome interno explicitamente permitido;
- operações POST exigem CSRF;
- requisições possuem limite de tamanho;
- conexões remotas são rejeitadas quando a Central está parada;
- arquivo de estado da Central existente e inválido desabilita o acesso remoto por segurança;
- firewall automático é restrito aos perfis Domínio/Privado, TCP `17345` e origem `LocalSubnet`, sem depender do caminho do executável;
- cache inválido é descartado sem reutilizar conteúdo não validado;
- um `656` continua bloqueando o processo mesmo se a persistência em disco falhar;
- atualizações automáticas exigem pacote oficial com SHA-256 válido;
- a porta não deve ser publicada na internet;
- o aplicativo não possui autenticação própria: a segurança de acesso depende da rede interna e do estado ativa/parada da Central.

## Mapeamento Fernando Klein

O mapeamento interno é aplicado somente quando o CPF/CNPJ do **emitente** corresponde ao fornecedor configurado. O XML e o `cProd` fiscal original nunca são alterados. Descrições desconhecidas não recebem código inventado.

A regressão automatizada cobre os 17 produtos cadastrados, aliases, normalização, isolamento por emitente e um item desconhecido.

## Desenvolvimento e validação

```bash
dotnet restore Nfe-Agendamento.sln
dotnet test Nfe-Agendamento.sln -c Release
node tests/js/product-mapping-regression.test.js
node tests/js/lookup-feedback-regression.test.js
node tests/js/release-readiness-regression.test.js
dotnet build Nfe-Agendamento.sln -c Release
```

O CI também publica um pacote Windows autocontido de teste e disponibiliza o ZIP como artifact.

## Criar uma release

Existe um único fluxo oficial:

1. abra **Actions**;
2. escolha **Release Bridge**;
3. clique em **Run workflow**;
4. informe uma versão maior que a última publicada;
5. execute.

Antes de publicar, o workflow valida a versão, executa testes e todas as regressões, compila e gera o pacote Windows x64 autocontido. A tag e a release apontam para o mesmo SHA que foi obtido no início do workflow e validado por esses testes, evitando publicar mudanças que tenham entrado posteriormente na `main`.

## Checklist de aceitação da release do Bloco 6

- [ ] instalar/extrair a nova versão no PC central;
- [ ] painel Windows abre com identidade azul/amarelo e sem perda dos controles operacionais;
- [ ] painel mostra **Central ativa**;
- [ ] **Rede: OK**;
- [ ] **Servidor: OK**;
- [ ] **Firewall: OK**;
- [ ] acesso local em `http://127.0.0.1:17345` funciona;
- [ ] segundo PC abre o endereço `http://IP-DO-CENTRAL:17345`;
- [ ] terceiro PC também consegue acessar a mesma Central;
- [ ] certificado continua somente no PC central e não pode ser administrado pelos clientes;
- [ ] consulta de uma NF-e conhecida funciona;
- [ ] duas consultas simultâneas não quebram a fila;
- [ ] consultas simultâneas da mesma chave não geram chamadas fiscais duplicadas;
- [ ] download XML e DANFE funcionam no cliente;
- [ ] **Iniciar com o Windows** relança o aplicativo sem `--lan`;
- [ ] em uma versão futura à primeira release com updater, **Verificar atualização** instala e reinicia o app corretamente.

Não provoque um `656` real apenas para testar o cooldown; persistência e bloqueio após reinício possuem cobertura automatizada.

## Documentação técnica

- [Guia operacional da Central](docs/CENTRAL-LAN.md)
- [Arquitetura atual da Central LAN](docs/superpowers/specs/2026-09-01-central-lan-architecture-design.md)
- [Plano e estado de verificação](docs/superpowers/plans/2026-09-01-central-lan-architecture.md)
- [Design do navegador local](docs/superpowers/specs/2026-08-31-local-browser-nfe-design.md)
- [Design do DANFE](docs/superpowers/specs/2026-08-31-danfe-fsist-design.md)
