# NFe Agendamento

Aplicativo Windows interno para consultar, visualizar e baixar NF-e. Cada computador executa sua própria interface local, usa seu próprio certificado A1 e coordena as operações fiscais diretamente pela pasta compartilhada.

## Estado da versão

- última release publicada: **v0.1.31**;
- `main`: arquitetura **sem Central e sem pareamento**, em preparação para a próxima release.

A decisão arquitetural desta mudança está documentada em [Arquitetura sem central e sem pareamento](docs/superpowers/specs/2026-09-08-no-central-shared-lock-design.md).

## Arquitetura atual

Cada PC executa o aplicativo localmente e abre somente:

```text
http://127.0.0.1:17345
```

Todos os PCs usam a mesma pasta compartilhada:

```text
P:\01-Nfe agendamento
```

Não existe:

- PC Central;
- líder eleito;
- código de pareamento;
- lista de PCs autorizados;
- servidor HTTP exposto na LAN;
- mDNS;
- regra automática de firewall;
- encaminhamento de consulta de um PC para outro.

Cada computador faz sua própria consulta à SEFAZ usando o certificado A1 configurado naquele Windows.

```text
PC local
   ↓
cache XML local (DPAPI, 24h)
   ↓
deduplicação local
   ↓
lock fiscal exclusivo na pasta compartilhada
   ↓
cooldown 656 compartilhado
   ↓
SEFAZ
   ↓
validação do XML
   ↓
cache XML local criptografado
```

## Por que ainda existe a pasta compartilhada

A pasta compartilhada não transporta mais pedidos ou XML entre computadores. Ela serve somente para coordenação operacional.

O lock fiscal fica em:

```text
P:\01-Nfe agendamento\status\fiscal.lock
```

Antes de chamar a SEFAZ, o processo abre esse arquivo com exclusividade (`FileShare.None`). Em um compartilhamento SMB normal, somente um PC consegue manter esse handle exclusivo por vez.

Quando a consulta termina, o handle é liberado. Se o processo for encerrado inesperadamente, o sistema operacional libera o handle automaticamente.

Se a pasta compartilhada estiver indisponível, o aplicativo **não inicia uma nova chamada fiscal**. Esse comportamento é intencional e fail-safe.

> Não use Offline Files/Arquivos Offline ou cache desconectado do Windows na pasta compartilhada. O projeto depende dos locks normais do SMB.

## Cooldown fiscal compartilhado

O bloqueio provocado por `cStat=656` continua compartilhado entre todos os computadores.

Estado:

```text
P:\01-Nfe agendamento\status\fiscal-cooldown.bin
```

Esse arquivo contém apenas o instante até o qual novas consultas devem permanecer bloqueadas. Ele não contém XML, certificado, senha ou chave privada.

Ao receber `656`, o PC grava o bloqueio de uma hora. Os demais PCs consultam o mesmo estado antes de chegar à SEFAZ.

## Cache XML local

O XML não é mais colocado na pasta compartilhada.

Cada PC mantém seu próprio cache em:

```text
%LOCALAPPDATA%\NfeAgendamento\cache
```

Proteções:

- DPAPI `CurrentUser`;
- retenção padrão de 24 horas;
- nome dos arquivos derivado da chave por hash;
- XML validado antes do uso;
- nenhum segredo compartilhado entre computadores.

Uma NF-e presente no cache local pode ser aberta sem disputar o lock fiscal porque nenhuma chamada à SEFAZ é feita nesse caso.

## Certificado A1

**Todos os PCs que farão consultas precisam ter o certificado A1 instalado e configurado localmente.**

Em cada computador valide:

- certificado correto em `CurrentUser\My`;
- certificado dentro da validade;
- chave privada acessível ao usuário que executa o app;
- UF autora configurada;
- acesso de leitura/gravação à pasta compartilhada;
- uma consulta conhecida de teste.

PFX, senha e chave privada nunca devem entrar no repositório ou na pasta compartilhada.

## Primeira configuração de um PC

1. copie/execute o NFe Agendamento naquele PC;
2. confirme acesso a `P:\01-Nfe agendamento`;
3. instale o certificado A1 no usuário do Windows que utilizará o app;
4. abra **Configurar**;
5. selecione o certificado e a UF autora;
6. confirme que **Pasta compartilhada** aparece como disponível;
7. faça uma consulta conhecida.

Não há qualquer etapa de pareamento.

## Consulta individual

Endpoint local:

```text
POST /api/nfe/lookup
```

Fluxo:

1. validar a chave de 44 dígitos;
2. verificar o cache local;
3. deduplicar consultas simultâneas da mesma chave neste PC;
4. entrar na fila fiscal local;
5. adquirir `status\fiscal.lock` na pasta compartilhada;
6. verificar o cooldown compartilhado;
7. consultar a distribuição de DF-e;
8. validar a resposta/XML;
9. persistir o XML no cache local criptografado;
10. liberar o lock compartilhado.

## Política contra consumo indevido e duplicação

A política permanece conservadora:

- `cStat=656` bloqueia novas consultas por uma hora;
- HTTP `429` não recebe retry automático;
- timeout fiscal não recebe retry automático;
- `5xx`, falha de conexão e `HttpRequestException` ambígua não geram retry fiscal automático;
- o cancelamento da interface impede trabalho ainda não iniciado;
- uma operação que pode já ter alcançado a SEFAZ não é repetida automaticamente;
- somente um PC por vez chega ao trecho fiscal quando todos usam a mesma pasta compartilhada.

## Contingência pelo Portal Nacional

Quando a consulta automática recebe `cStat=656`, o botão **Baixar pelo Portal** permanece disponível no PC que possui certificado A1 local e WebView2.

O hCaptcha continua manual e não é automatizado nem contornado.

Fluxo:

1. o app abre o Portal oficial em WebView2;
2. preenche a chave de acesso;
3. o usuário resolve o hCaptcha;
4. o Portal usa o certificado A1 local quando necessário;
5. o XML baixado é validado contra a chave solicitada;
6. o XML válido entra no cache local criptografado;
7. a tela acompanha o cache por `GET /api/nfe/cache/{accessKey}`;
8. a NF-e é carregada quando o XML aparece.

O polling do cache não consulta a SEFAZ.

## Consulta em lote

O lote reutiliza o mesmo endpoint e o mesmo gate fiscal:

- até 50 chaves únicas;
- duplicatas removidas;
- uma operação fiscal por vez neste PC;
- o lock compartilhado garante uma operação fiscal por vez entre os PCs;
- cache e deduplicação são locais;
- cooldown `656` é compartilhado;
- `656` interrompe o restante do lote;
- cancelar impede o início dos próximos itens.

## DANFE

O DANFE é produzido localmente a partir do XML validado:

- visualização em popup próprio;
- `Ctrl + scroll` aplica zoom somente ao DANFE;
- impressão/salvar PDF usa o navegador local;
- XML fiscal original não é modificado.

## Mapeamento Fernando Klein

O mapeamento interno altera somente a apresentação de código/descrição quando aplicável. O XML e o `cProd` fiscal original permanecem intactos.

## Dados locais

Os dados locais ficam em:

```text
%LOCALAPPDATA%\NfeAgendamento
```

Principais itens:

```text
cache\                 XML criptografado por DPAPI
state\                 seleção de certificado e estados locais
logs\fiscal-audit.jsonl auditoria sem XML/chave completa
```

Arquivos antigos de pareamento/grupo deixados por versões anteriores podem existir localmente ou no compartilhamento, mas não participam da composição de produção atual.

## Estrutura compartilhada ativa

A estrutura mínima usada pelo fluxo atual é:

```text
P:\01-Nfe agendamento\
├── .nfe-agendamento
└── status\
    ├── fiscal.lock
    └── fiscal-cooldown.bin
```

Diretórios antigos como `fila`, `processando`, `respostas`, `pareamento`, `candidatos` e `cache` podem continuar existindo para compatibilidade de atualização. O fluxo atual não depende deles para enviar consultas ou armazenar XML.

## Segurança de rede

- HTTP somente em `127.0.0.1:17345`;
- Host e Origin validados;
- operações mutáveis protegidas por CSRF;
- nenhuma porta LAN adicional;
- nenhuma regra de firewall criada;
- nenhum mDNS necessário.

Loopback impede exposição direta da interface à rede, mas não isola processos do mesmo Windows. O projeto assume PCs corporativos confiáveis.

## Segurança do compartilhamento

O fluxo atual reduz bastante o material sensível na rede:

- certificado A1 permanece local;
- chave privada permanece local;
- senha/PFX permanecem locais;
- XML permanece local e criptografado por DPAPI;
- a pasta compartilhada guarda somente marcador, lock e cooldown operacional;
- caminhos operacionais rejeitam reparse points;
- se a pasta não puder ser validada, a consulta fiscal falha fechada.

Permita leitura/gravação na pasta somente para os usuários/PCs corporativos que realmente usam o sistema.

## Interface de status

A janela da bandeja mostra:

- modo **Coordenação por pasta compartilhada**;
- nome do PC local;
- caminho compartilhado;
- disponibilidade da pasta;
- estado do lock fiscal.

Não existem mais estados de líder, standby ou heartbeat.

## Atualização

Na bandeja use **Verificar atualização**.

O atualizador continua exigindo:

- origem oficial por HTTPS;
- tamanho esperado;
- SHA-256 válido;
- bundle Sigstore válido;
- identidade vinculada ao workflow oficial;
- health check do aplicativo reiniciado;
- rollback se a versão nova não responder corretamente.

Veja [Inicialização e atualização](docs/ATUALIZACAO-E-INICIALIZACAO.md).

## Desenvolvimento e validação

Requer SDK **.NET 10**.

Gate oficial do repositório:

```powershell
./scripts/verify.ps1 -Restore
```

O script executa:

- restore;
- auditoria NuGet;
- testes .NET em Release;
- regressões JavaScript;
- build Release.

A cobertura relevante para a arquitetura atual inclui:

- lock fiscal compartilhado entre duas instâncias;
- fail-safe quando a pasta compartilhada está indisponível;
- cooldown `656` compartilhado entre instâncias sem chave de grupo;
- cache XML local criptografado;
- ausência de Central/pareamento na composição de produção;
- ausência de endpoints e controles de pareamento;
- segurança de caminhos/reparse points;
- comportamento conservador para falhas fiscais ambíguas;
- segurança de loopback/Host/Origin/CSRF;
- Portal/WebView2;
- DANFE;
- atualização e rollback.

## Teste em múltiplos PCs

Antes de considerar a próxima release pronta para uso geral, valide em pelo menos dois PCs reais:

1. ambos apontando para a mesma pasta compartilhada;
2. certificado A1 configurado nos dois;
3. consulta individual funcionando nos dois;
4. tentativa simultânea confirmando serialização pelo lock;
5. indisponibilidade temporária da pasta impedindo nova chamada fiscal;
6. cooldown `656` sendo observado por ambos;
7. lote em pelo menos um PC;
8. Portal fallback em pelo menos um PC;
9. atualização/rollback.

Veja também [Teste multi-PC](docs/TESTE-MULTI-PC.md).

## Regra operacional importante

Durante a migração para esta arquitetura, **atualize todos os PCs que usam a mesma pasta compartilhada antes de voltar a fazer consultas simultâneas**. Não misture por longos períodos versões antigas baseadas em líder/pareamento com a versão nova baseada em `fiscal.lock`.
