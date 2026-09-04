# Hardening pós-auditoria v0.1.30 — design

## Objetivo

Fechar os riscos restantes identificados na auditoria da v0.1.30 sem alterar a premissa operacional do projeto: aplicação Windows interna, HTTP somente em loopback, coordenação multi-PC pela pasta compartilhada e uma única liderança fiscal por vez.

## Princípios

- Falhar fechado antes de qualquer chamada fiscal ambígua.
- Nenhuma rotação de confiança pode depender de uma única gravação não recuperável.
- Um PC revogado deixa de poder autenticar requisições, assumir a liderança e ler o estado criptográfico novo do grupo.
- Migrações existentes continuam compatíveis.
- Nenhum segredo fiscal ou chave privada de release é adicionado ao repositório.
- Mudanças entram por TDD e `scripts/verify.ps1 -Restore` permanece o gate único do projeto.

## 1. Bootstrap recuperável

O bootstrap inicial passa a tratar `CandidateStateStore` como journal local protegido por DPAPI.

Fluxo:

1. reutilizar uma chave de grupo local válida quando ela já existir e a identidade compartilhada ainda não existir;
2. se a chave não existir, gerar 32 bytes aleatórios e salvá-los localmente antes de publicar `group-identity.bin`;
3. inicializar a identidade compartilhada com essa mesma chave;
4. continuar a migração dos clientes legados.

Assim, uma queda entre a gravação local e a gravação compartilhada é recuperada na próxima inicialização, em vez de criar uma identidade cifrada por uma chave perdida.

## 2. Revogação e rotação recuperável

A revogação só pode ser iniciada pelo líder ativo e não pode remover o próprio cliente que está executando a rotação.

A rotação terá preparação e commit explícitos:

1. obter snapshot dos clientes autorizados e remover o alvo;
2. gerar nova chave de grupo e nova identidade RSA;
3. criar bundles de candidatura para todos os clientes restantes usando os segredos individuais já existentes;
4. preparar, sem substituir os arquivos ativos, a nova identidade compartilhada, nova lista autorizada e o cooldown fiscal atual cifrados pela nova chave;
5. publicar um marcador de rotação com identificador aleatório;
6. promover os arquivos preparados para os caminhos ativos;
7. atualizar o estado local/pin criptográfico do líder;
8. eliminar o bundle do cliente revogado, purgar o cache fiscal antigo e remover o marcador.

Se houver queda depois da publicação do marcador, qualquer cliente restante possui bundle suficiente para importar a nova chave. O próximo PC que adquirir `central.lock` conclui a promoção dos arquivos preparados antes de processar trabalho fiscal.

O cooldown fiscal nunca é descartado durante a rotação. O cache XML pode ser purgado, pois sua perda causa apenas uma nova consulta explícita; o cooldown protege contra repetição fiscal e precisa sobreviver.

## 3. Detecção de chave obsoleta

`IsCandidateReady` deixa de significar somente “arquivo local existe + identidade compartilhada existe”. Ele valida que a chave local consegue abrir a identidade compartilhada atual e o armazenamento de clientes autorizados. Se a rotação estiver pendente, o bundle individual pode atualizar a chave e o pin da identidade.

`ClientPairingStore` ganha uma operação atômica para substituir somente a identidade pública mantendo `ClientId`, segredo e sequência.

## 4. Gerenciamento de clientes

Novos endpoints locais:

- `GET /api/pairing/clients`: somente no líder, lista `clientId` e nome; nunca expõe segredos.
- `POST /api/pairing/revoke`: somente no líder, recebe `clientId`, executa revogação + rotação e retorna erro explícito em caso de rotação pendente/cliente inexistente/autorrevogação.

A interface de pareamento mostra PCs autorizados apenas no líder e oferece `Remover` com confirmação explícita.

## 5. Pareamento one-shot

`PairingCodeService` recebe consumo atômico do código. O processador só consome o código depois que a autorização, candidate bundle e resposta foram gravados com sucesso. Depois disso novas solicitações com o mesmo código não são aceitas.

Falhas transitórias antes da conclusão não consomem o código.

## 6. Supply chain

Todas as actions oficiais usadas por CI, CodeQL e Release Bridge são fixadas por commit SHA. Dependabot continua responsável por propor atualizações. O teste de hardening passa a reprovar referências `@vN` nos workflows.

## 7. Health check de atualização

`/api/bootstrap` expõe `appVersion` derivado do assembly em execução. O script do atualizador, depois do swap, exige simultaneamente:

- HTTP 2xx;
- JSON válido;
- `appVersion` exatamente igual à versão que foi preparada.

Só depois disso o backup é removido.

## 8. Limpeza de legado

A migração não deve acessar campo privado por reflection. `AuthorizedClientStore` expõe internamente um snapshot validado para migração, mantendo o arquivo e a proteção DPAPI existentes. Depois disso `SharedQueueGroupBootstrapService` deixa de depender de `FieldInfo`.

## 9. Portal e operações longas

Handlers assíncronos do WebView2 continuam bloqueando navegação externa e passam a capturar também falhas de ciclo de vida do controle para não derrubar a janela por uma exceção tardia.

O uso deliberado de `CancellationToken.None` depois do início da operação fiscal permanece: cancelar a interface não pode induzir retry ambíguo. Essa decisão será documentada como requisito de segurança, com timeout do transporte como limite da operação.

## 10. Threat model local

O HTTP continua somente em loopback. O app assume estações Windows corporativas com usuário local confiável. Loopback não é fronteira contra malware ou outro usuário com execução local; isso será explicitado na documentação. Não será adicionada autenticação web paralela que complique o fluxo sem mudar o risco de um processo já comprometido no mesmo Windows.

## Testes obrigatórios

- bootstrap interrompido entre estado local e identidade compartilhada recupera;
- código de pareamento é consumido apenas após sucesso;
- cliente revogado desaparece da lista e não autentica mais;
- rotação troca chave de grupo e identidade pública;
- clientes restantes importam bundle rotacionado e preservam `ClientId`/sequência;
- revogado não recebe bundle novo;
- cooldown sobrevive à rotação;
- rotação interrompida com marcador é concluída pelo próximo líder;
- bootstrap só considera candidato pronto quando a chave abre o estado atual;
- endpoint não permite autorrevogação nem revogação em standby;
- workflows rejeitam tags móveis de actions;
- health check exige versão exata;
- migração não usa reflection;
- WebView2 mantém bloqueios atuais.

## Critério de conclusão

A mudança só é concluída quando o CI da `main`, CodeQL e `scripts/verify.ps1 -Restore` estão verdes no SHA final. Uma nova release só será solicitada depois da documentação e versão estarem alinhadas.