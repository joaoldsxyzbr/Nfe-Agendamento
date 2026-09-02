# Consulta em lote sobre a fila compartilhada — Design

## Contexto

A arquitetura atual já possui uma fila segura entre os PCs clientes e o PC Central usando `P:\01-Nfe agendamento`. Cada consulta individual passa por `POST /api/nfe/lookup`; no cliente, esse endpoint publica um pedido autenticado/criptografado na pasta compartilhada e, no Central, o mesmo endpoint usa diretamente o fluxo fiscal local quando a Central está ativa.

A consulta em lote deve aproveitar exatamente esse caminho. O lote não deve criar um segundo protocolo, não deve abrir paralelismo fiscal e não deve aumentar a superfície de segurança.

## Objetivo

Permitir que o usuário cole várias chaves NF-e e processe todas de forma simples, acompanhando o progresso de cada item, sem comprometer a proteção contra `cStat=656`, deduplicação, cache, pareamento ou serialização fiscal já existentes.

## Decisão arquitetural

A implementação será uma camada de orquestração **no frontend local**, isolada em `wwwroot/batch.js`.

Não será criado endpoint `/batch`, modelo de arquivo especial, fila paralela ou serviço backend adicional. Para cada chave, o lote chamará sequencialmente o endpoint já existente:

```text
POST /api/nfe/lookup
```

Fluxo:

```text
Usuário cola várias chaves
        ↓
batch.js normaliza e deduplica
        ↓
1 requisição local por vez
        ↓
/api/nfe/lookup existente
        ↓
Central local OU fila compartilhada existente
        ↓
Fila fiscal / cache / cooldown / SEFAZ
```

Mesmo que dois ou três PCs iniciem lotes ao mesmo tempo, cada PC terá no máximo uma consulta de lote pendente por vez. A Central continua sendo responsável pela ordenação real entre computadores e pela serialização da chamada fiscal.

## Interface

A tela continuará priorizando a consulta unitária. A consulta em lote será um painel secundário abaixo da área principal, sem alterar o layout essencial do aplicativo.

Elementos:

- textarea para colar uma chave por linha;
- contador de chaves válidas, duplicadas e inválidas;
- botão **Iniciar lote**;
- botão **Cancelar lote** visível somente durante execução;
- botão **Limpar** quando o lote não estiver executando;
- resumo `concluídas / total`;
- tabela de resultados com número, chave, estado e ações.

Estados por item:

- `Aguardando`;
- `Consultando`;
- `Concluída`;
- `Não encontrada`;
- `Manifestação necessária`;
- `Erro`;
- `Bloqueada pela SEFAZ`;
- `Cancelada`.

Para itens concluídos ficam disponíveis:

- **Ver DANFE**;
- **Baixar XML**.

Os XMLs retornados pelo lote ficam apenas em memória enquanto a página estiver aberta. Não haverá `localStorage`, IndexedDB ou arquivos adicionais no compartilhamento.

## Entrada e limites

- máximo de **50 chaves únicas por lote**;
- uma chave por linha;
- espaços, pontos, hífens e demais caracteres não numéricos da linha são removidos antes da validação;
- após normalização, a linha precisa conter exatamente 44 dígitos;
- duplicatas dentro do próprio lote são removidas preservando a primeira ocorrência;
- entradas inválidas são informadas antes do início e não são enviadas ao backend;
- a validação fiscal definitiva continua no backend por `AccessKeyValidator`.

O limite de 50 é deliberado: suficiente para a operação interna e pequeno o bastante para evitar lotes acidentais enormes, consumo excessivo de memória e longas filas sem supervisão.

## Execução serial

`batch.js` terá um executor serial testável. Ele nunca usa `Promise.all` para consultas fiscais.

Para cada item:

1. marca `Consultando`;
2. chama `/api/nfe/lookup`;
3. aguarda a resposta completa;
4. atualiza o estado do item;
5. somente então avança para a próxima chave.

Isso garante concorrência máxima de **1 consulta de lote por instalação**.

A fila compartilhada permanece responsável por arbitrar consultas originadas em máquinas diferentes.

## Backpressure e erros

### Central/fila ocupada

Se o backend responder `429` com `status = fila_ocupada`:

- respeitar `Retry-After`;
- repetir o mesmo item;
- máximo de 3 novas tentativas;
- após isso, marcar o item como erro e continuar o lote.

### `cStat=656` / consumo indevido

Se o backend responder `429` com `status = consumo_indevido`:

- marcar o item atual como `Bloqueada pela SEFAZ`;
- interromper imediatamente o lote;
- marcar os itens ainda pendentes como `Não processada — cooldown SEFAZ`;
- mostrar a mensagem retornada pelo backend, incluindo o horário de desbloqueio quando disponível.

O lote nunca deve tentar contornar o cooldown persistente.

### Cancelamento manual

O botão **Cancelar lote**:

- aciona um `AbortController` para a requisição atual;
- impede o início de novos itens;
- marca itens ainda não iniciados como `Cancelada`;
- não tenta apagar itens que já tenham sido reivindicados pela Central; esse comportamento continua sendo responsabilidade do protocolo atual da fila.

### Demais erros

Erros de uma NF-e individual não encerram o lote, exceto o bloqueio `656`. O lote segue para o próximo item depois de registrar a mensagem da falha.

## DANFE e XML

O lote não duplicará o renderizador DANFE.

Ao clicar **Ver DANFE** em uma linha concluída:

1. o XML daquela linha é atribuído ao estado já usado pela consulta unitária (`currentXml` e `currentKey`);
2. o renderizador existente `renderDanfe()` é chamado.

**Baixar XML** cria um `Blob` em memória e usa o mesmo padrão de nome da consulta unitária.

Nenhum XML adicional será persistido pelo frontend.

## Segurança

A consulta em lote não altera o modelo de confiança:

- nenhum novo endpoint de rede;
- HTTP continua somente em `127.0.0.1:17345`;
- CSRF continua obrigatório em cada POST;
- clientes continuam precisando estar pareados;
- a chave pública fixada da Central, HMAC do cliente, sequência anti-replay e AES/RSA existentes continuam inalterados;
- nenhuma chave NF-e ou XML é gravada em texto puro em `P:`;
- o lote não acessa diretamente `P:`;
- limite de 50 reduz abuso acidental do frontend.

## Arquivos

Criar:

- `src/NfeAgendamento.App/wwwroot/batch.js`
- `tests/js/batch-lookup-regression.test.js`

Modificar:

- `src/NfeAgendamento.App/wwwroot/index.html`
- `src/NfeAgendamento.App/wwwroot/ui-adjustments.css`
- `.github/workflows/ci.yml`
- `.github/workflows/release-bridge.yml`
- `README.md`
- `docs/CENTRAL-LAN.md`

Não modificar o protocolo da fila nem os serviços fiscais salvo se um teste demonstrar necessidade concreta.

## Testes obrigatórios

O módulo de lote deve expor funções puras para Node/CommonJS sem depender do DOM.

Cobertura mínima:

1. normalização de chave com separadores;
2. rejeição de linha que não resulte em 44 dígitos;
3. remoção de duplicatas preservando ordem;
4. limite máximo de 50;
5. executor processa estritamente em série (`maxConcurrent === 1`);
6. cancelamento impede início dos itens seguintes;
7. `fila_ocupada` gera retry limitado;
8. `consumo_indevido` interrompe o restante do lote;
9. `index.html` contém os controles esperados e carrega `batch.js`;
10. CI e Release Bridge executam o teste de regressão do lote.

Após os testes JS, a validação final continua executando toda a suíte .NET, regressões existentes, build Release e publish Windows.

## Critérios de aceite físico

Em ambiente da empresa:

1. Central ativa e dois clientes pareados;
2. lote com 3 chaves no Central conclui em ordem;
3. lote com 3 chaves em cliente sem A1 conclui pela pasta compartilhada;
4. dois PCs iniciando lotes simultaneamente não provocam paralelismo fiscal fora do controle da Central;
5. chave repetida no textarea é enviada apenas uma vez;
6. cancelar lote impede novas consultas;
7. DANFE e download XML funcionam por linha concluída;
8. nenhum arquivo fora de `P:\01-Nfe agendamento` é tocado;
9. nenhum pedido de regra de firewall aparece.

## Não objetivos

Ficam explicitamente fora desta entrega:

- importação de Excel/CSV;
- histórico persistente de lotes;
- ZIP com todos os XMLs;
- execução paralela de consultas fiscais;
- agendamento automático de lotes;
- retentativa automática após término de um cooldown `656`;
- mudança no formato criptográfico da fila compartilhada.
