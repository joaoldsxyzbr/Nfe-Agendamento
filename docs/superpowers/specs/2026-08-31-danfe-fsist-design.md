# DANFE Completo inspirado no FSist — Design

## Objetivo

Substituir o DANFE simplificado atual por uma visualização/impressão A4 muito mais completa, usando o PDF do FSist enviado pelo usuário como referência funcional e visual, sem copiar marca, textos proprietários ou identidade do FSist.

## Escopo

O novo DANFE deve continuar sendo gerado localmente a partir do XML já obtido pelo NFe Agendamento. Nenhum dado fiscal será inventado: campos ausentes no XML ficam vazios ou deixam de ser exibidos conforme o bloco.

## Estrutura visual

A impressão deve seguir a organização tradicional de DANFE vista na referência:

1. Canhoto de recebimento quando aplicável.
2. Cabeçalho com identificação do emitente.
3. Bloco DANFE com tipo de operação, número, série e folha.
4. Chave de acesso em destaque e código de barras Code 128.
5. Natureza da operação, protocolo de autorização, inscrições e CNPJ/CPF.
6. Destinatário/remetente completo.
7. Pagamento.
8. Cálculo completo dos impostos.
9. Transportador e volumes transportados.
10. Produtos/serviços em tabela fiscal completa.
11. Dados adicionais / informações complementares.
12. Reservado ao fisco quando houver espaço/bloco correspondente.

## Dados fiscais

### Cabeçalho e identificação

Extrair do XML, quando disponíveis:
- `ide/natOp`
- `ide/tpNF`
- `ide/nNF`
- `ide/serie`
- `ide/dhEmi` ou `ide/dEmi`
- `ide/dhSaiEnt` / `dSaiEnt` / `hSaiEnt`
- protocolo `protNFe/infProt/nProt`
- data/hora da autorização `protNFe/infProt/dhRecbto`
- chave via `infNFe/@Id`

### Emitente e destinatário

Exibir razão social, CNPJ/CPF, IE, IM quando houver, endereço completo, município, UF, CEP e telefone quando presente.

### Pagamento

Ler `pag/detPag` e exibir forma e valor. Quando houver múltiplos pagamentos, mostrar todos de forma compacta.

### Totais fiscais

Ler `total/ICMSTot` e exibir, quando presentes:
- Base de cálculo do ICMS (`vBC`)
- ICMS (`vICMS`)
- Base ICMS ST (`vBCST`)
- ICMS ST (`vST`)
- FCP (`vFCP`)
- FCP ST (`vFCPST`)
- produtos (`vProd`)
- frete (`vFrete`)
- seguro (`vSeg`)
- desconto (`vDesc`)
- outras despesas (`vOutro`)
- IPI (`vIPI`)
- PIS (`vPIS`)
- COFINS (`vCOFINS`)
- valor total da NF-e (`vNF`)
- demais totais existentes no XML que tenham bloco equivalente útil no DANFE.

### Produtos e tributação por item

A tabela deve conter, conforme espaço e disponibilidade:
- código do produto
- descrição
- NCM
- CST/CSOSN consolidado
- CFOP
- unidade
- quantidade
- valor unitário
- valor total
- desconto
- base ICMS
- valor ICMS
- valor IPI
- alíquota ICMS
- alíquota IPI

Também deve mostrar, junto da descrição do item, informações relevantes de ICMS-ST existentes no XML, como base ST e valor ST, seguindo a leitura visual da referência.

O parser deve localizar o grupo tributário efetivamente presente dentro de `imposto/ICMS/*`, sem assumir um único CST.

### Transporte

Exibir, quando disponíveis:
- modalidade do frete
- transportador
- CNPJ/CPF
- IE
- endereço
- município/UF
- placa/UF
- quantidade de volumes
- espécie
- marca
- numeração
- peso bruto
- peso líquido

### Informações adicionais

Exibir `infAdic/infCpl` e `infAdic/infAdFisco` separadamente quando disponíveis.

## Arquitetura

O `app.js` atual deve deixar de conter toda a regra de geração do DANFE. A responsabilidade será separada em arquivos dedicados no `wwwroot`:

- `danfe.js`: parser do XML, normalização do modelo e construção do HTML do DANFE.
- `danfe.css`: layout A4, tabela fiscal, paginação e regras de impressão.
- `app.js`: continua responsável pelo fluxo da aplicação e apenas chama o renderer.

A solução deve permanecer sem backend adicional e sem dependência de serviço externo.

## Código de barras

Usar Code 128 para a chave de acesso de 44 dígitos. Preferir implementação local/embutida compatível com o projeto, sem chamadas de rede durante o uso. A chave textual permanece visível mesmo se o código de barras não puder ser renderizado.

## Paginação

A impressão deve suportar múltiplas folhas A4.

Regras:
- Cabeçalho fiscal reaparece nas páginas seguintes de forma compacta.
- Tabela de produtos pode quebrar entre páginas.
- Cabeçalho da tabela deve repetir nas páginas seguintes.
- Blocos pequenos não devem ser cortados no meio quando for evitável.
- Número de folha deve refletir `Folha X/Y`.
- Para notas longas, os produtos continuam nas folhas seguintes, como na referência FSist.

Como o navegador não oferece contagem de páginas CSS totalmente confiável antes da impressão, o renderer deverá particionar os itens em páginas lógicas antes de gerar o DOM. O particionamento deve usar limites previsíveis de linhas/altura e manter a primeira página com mais blocos fiscais e as páginas seguintes prioritariamente para itens.

## Comportamento no app

Os botões existentes `Visualizar DANFE` e `Imprimir / Salvar PDF` continuam funcionando.

`Visualizar DANFE` mostra a prévia completa no próprio app.

`Imprimir / Salvar PDF` renderiza o mesmo documento e aciona a impressão do navegador.

A consulta da NF-e, download/visualização do XML, certificado, lote e demais funcionalidades não devem mudar.

## Tratamento de ausência de dados

- Campo simples ausente: deixar vazio ou omitir rótulo sem valor.
- Grupo inteiro ausente: ocultar bloco quando isso melhorar a leitura.
- XML inválido: manter o erro atual de leitura do XML.
- Nunca preencher valores fiscais por cálculo próprio quando o XML não trouxer explicitamente o valor.

## Segurança

Todo conteúdo proveniente do XML deve continuar escapado antes de entrar no HTML para evitar injeção de marcação/script.

Nenhum dado do XML será enviado a terceiros para gerar o DANFE ou código de barras.

## Testes

Adicionar cobertura de regressão para o parser/modelo do DANFE usando um XML de fixture anonimizado ou sintético com a mesma estrutura fiscal da NF-e de referência.

Cobrir pelo menos:
- cabeçalho e chave
- protocolo
- emitente/destinatário
- pagamento
- ICMS e ICMS-ST
- produtos em diferentes grupos ICMS
- transporte/volumes
- informações adicionais
- múltiplas páginas
- ausência segura de campos opcionais

Executar a suíte .NET existente e validações específicas dos arquivos estáticos. A feature só deve ir para a `main` após testes e CI verdes.

## Critérios de aceitação

1. A mesma NF-e usada na comparação deixa de gerar o PDF simplificado de uma página e passa a resultar em um DANFE visualmente próximo da organização do FSist.
2. Todos os oito produtos aparecem com os campos fiscais disponíveis no XML.
3. ICMS-ST e demais totais presentes no XML são exibidos.
4. Transporte, volumes, pesos, pagamento e dados adicionais aparecem quando presentes.
5. Notas grandes paginam corretamente sem perder itens.
6. O restante do NFe Agendamento continua funcionando sem alteração de fluxo.
7. Nenhum dado fiscal é inventado ou enviado para serviço externo.
