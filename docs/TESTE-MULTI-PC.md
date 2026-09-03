# Validação física multi-PC

Este roteiro fecha a parte da arquitetura que não pode ser provada apenas pelo CI: comportamento real de lock/SMB, failover, A1 e atualização em máquinas Windows distintas.

## Preparação

Use 2 ou 3 PCs confiáveis com a mesma versão do NFe Agendamento, acesso a `P:\01-Nfe agendamento`, autorização de candidato concluída e A1 válido/configurado localmente.

Não provoque `cStat=656` real apenas para teste.

## Critérios de aceitação

### 1. Eleição simultânea

1. Feche o app em todos os PCs.
2. Abra em 2 ou 3 PCs quase ao mesmo tempo.
3. Confira **Status da fila**.

Esperado:

- [ ] exatamente um PC fica líder;
- [ ] os demais ficam em standby;
- [ ] não existem dois líderes simultâneos;
- [ ] somente o líder mantém `status\central.lock`.

### 2. Consulta pelo standby

1. No PC em standby, consulte uma NF-e conhecida.
2. Confirme o resultado e o DANFE/XML.

Esperado:

- [ ] o pedido atravessa a fila compartilhada;
- [ ] apenas o líder executa eventual operação fiscal;
- [ ] o standby recebe a resposta normalmente.

### 3. Deduplicação entre PCs

1. Use a mesma chave válida em dois PCs com poucos segundos de diferença.
2. Observe o resultado e o log fiscal.

Esperado:

- [ ] não ocorre consulta fiscal duplicada para a mesma necessidade;
- [ ] cache/deduplicação atendem o segundo pedido quando aplicável.

### 4. Failover normal

1. Identifique o líder atual.
2. Encerre o app pelo comando **Sair**.
3. Aguarde outro candidato assumir.
4. Faça uma nova consulta no antigo standby.

Esperado:

- [ ] outro PC assume automaticamente;
- [ ] a identidade pública do grupo permanece a mesma;
- [ ] clientes continuam autorizados;
- [ ] a consulta funciona sem reapareamento.

### 5. Cache após troca de líder

1. Consulte uma NF-e e confirme que o XML foi armazenado no cache compartilhado.
2. Troque o líder.
3. Consulte a mesma chave dentro da retenção de 24 horas.

Esperado:

- [ ] o novo líder lê o cache existente;
- [ ] não é necessária nova consulta à SEFAZ.

### 6. Perda do compartilhamento

1. Com um líder ativo, interrompa o acesso desse PC a `P:\01-Nfe agendamento`.
2. Tente iniciar uma nova consulta.
3. Restaure o compartilhamento.

Esperado:

- [ ] o líder perde autoridade para iniciar novo trabalho fiscal;
- [ ] a consulta falha de forma segura enquanto o lock não é saudável;
- [ ] após recuperação, a eleição volta ao estado de exatamente um líder.

### 7. A1 nos candidatos

Em cada PC que pode virar líder, valide uma consulta conhecida quando ele estiver na liderança.

Esperado:

- [ ] certificado selecionado está disponível no `CurrentUser\My`;
- [ ] chave privada está acessível ao usuário do app;
- [ ] UF autora está correta;
- [ ] consulta funciona em cada candidato.

### 8. Atualização assinada

Depois que o Secret `NFE_UPDATE_SIGNING_KEY_PKCS8_B64` estiver provisionado e uma release assinada for publicada:

- [ ] a release contém `Nfe-Agendamento-win-x64.zip` e `Nfe-Agendamento-win-x64.zip.sig`;
- [ ] uma assinatura válida permite preparar a atualização;
- [ ] pacote/assinatura alterados são rejeitados;
- [ ] health check em até 20 segundos confirma a nova versão;
- [ ] falha do health check restaura a instalação anterior.

## Cobertura automatizada relacionada

O CI já cobre eleição de um único líder, takeover, conservação da identidade, fail-closed após perda de lease, não repetição de pedido fiscal ambíguo, cache compartilhado, fencing imediatamente antes da SEFAZ, atualização/rollback e validação criptográfica RSA-PSS do pacote.

Este roteiro existe especificamente para validar diferenças de SMB, Windows Certificate Store, rede e processo entre máquinas reais.
