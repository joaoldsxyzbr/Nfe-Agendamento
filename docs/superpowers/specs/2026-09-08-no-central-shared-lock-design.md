# Arquitetura sem central e sem pareamento

## Contexto

O fluxo de pareamento/eleição de líder tornou o uso multi-PC dependente de uma etapa operacional que está falhando em campo. A pasta compartilhada já é o ponto comum entre os computadores e todos os PCs que usarão o sistema podem ter o certificado A1 instalado localmente.

## Decisão

O NFe Agendamento deixa de ter os papéis de **Central**, **líder autorizado** e **cliente pareado**.

Cada PC:

- executa somente o servidor HTTP local em `127.0.0.1:17345`;
- usa o certificado A1 instalado/configurado localmente;
- realiza a própria consulta à SEFAZ;
- antes de iniciar qualquer operação fiscal, adquire um lock exclusivo na pasta compartilhada `P:\01-Nfe agendamento`;
- respeita o cooldown fiscal compartilhado;
- mantém o cache XML local, criptografado com DPAPI.

A pasta compartilhada deixa de transportar solicitações/respostas criptografadas entre PCs. Ela passa a coordenar somente estado operacional não sensível.

## Coordenação fiscal

O arquivo `status/fiscal.lock` é aberto com exclusividade (`FileShare.None`) durante a janela fiscal. Em compartilhamento SMB, somente um processo/PC consegue manter o handle exclusivo por vez. O handle é liberado ao final da operação ou automaticamente pelo sistema operacional se o processo morrer.

O `FiscalOperationGate` continua limitando a fila local e passa a adquirir também esse lock compartilhado quando configurado com `SharedQueuePaths`.

Se a pasta compartilhada não estiver disponível, nenhuma nova consulta à SEFAZ deve ser iniciada. O comportamento é fail-safe para evitar consultas concorrentes entre PCs.

## Cooldown 656

`FiscalCooldownStore` mantém `status/fiscal-cooldown.bin` na pasta compartilhada, mas não depende mais de chave de grupo/pareamento. O conteúdo contém somente o instante de bloqueio e é persistido atomicamente em JSON estrito.

Todos os PCs consultam esse mesmo estado antes de chamar a SEFAZ. Ao receber `cStat=656`, qualquer PC publica o bloqueio de uma hora para os demais.

## Cache XML

O cache de XML deixa de ser compartilhado. Cada PC usa `EncryptedXmlCache()` local, com DPAPI e retenção de 24 horas. Isso elimina a necessidade de distribuir uma chave de grupo e evita expor XML fiscal no compartilhamento.

## Portal NF-e

O fallback pelo Portal é local. Ele não depende de pareamento; fica disponível quando o PC tem o certificado local configurado. O hCaptcha permanece manual.

## API e interface

Remover da composição ativa:

- `/api/pairing/code`;
- `/api/pairing/client`;
- `/api/pairing/clients`;
- `/api/pairing/revoke`;
- serviços de pareamento, rotação de grupo, cliente remoto e processador/líder da fila.

`/api/bootstrap` passa a informar o modo `shared_lock`, disponibilidade da pasta compartilhada e ausência de pareamento.

A aba **Configurar** deve mostrar apenas certificado local e estado da pasta compartilhada. Textos sobre líder, central, autorização e códigos de pareamento são removidos.

## Bandeja

A janela de status deixa de mostrar líder/heartbeat. Ela mostra:

- modo: coordenação por pasta compartilhada;
- PC local;
- caminho da pasta;
- disponibilidade da pasta;
- informação de que o lock fiscal é adquirido somente durante consultas.

## Compatibilidade

Classes e arquivos antigos de pareamento podem permanecer temporariamente no código-fonte se não fizerem parte da composição de produção, evitando uma remoção ampla desnecessária nesta mudança. Nenhum endpoint/UI/fluxo de produção pode depender deles.

## Critérios de aceitação

1. Nenhum usuário precisa gerar ou informar código de pareamento.
2. Cada PC usa seu próprio certificado A1.
3. Duas instâncias apontando para a mesma pasta não iniciam operações fiscais simultaneamente.
4. A indisponibilidade da pasta impede nova chamada à SEFAZ.
5. O cooldown 656 é observado por todos os PCs.
6. XML/cache fiscal não depende de chave compartilhada.
7. Interface e documentação não orientam o usuário a configurar Central/líder/pareamento.
8. CI, testes .NET e regressões JS permanecem verdes.