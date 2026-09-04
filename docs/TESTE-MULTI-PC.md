# Validação física multi-PC

Este roteiro fecha a parte da arquitetura que não pode ser provada apenas pelo CI: comportamento real de lock/SMB, failover, A1, pareamento, revogação e atualização em máquinas Windows distintas.

## Preparação

Use 2 ou 3 PCs confiáveis com a mesma versão do NFe Agendamento, acesso a `P:\01-Nfe agendamento`, autorização de candidato concluída e A1 válido/configurado localmente.

Confirme também que a pasta da fila usa SMB normal e que **Offline Files/Arquivos Offline** ou cache desconectado não estão habilitados para esse compartilhamento.

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
- [ ] a identidade pública do grupo permanece a mesma enquanto não houver uma rotação de confiança;
- [ ] clientes continuam autorizados;
- [ ] a consulta funciona sem reapareamento.

### 5. Cache após troca de líder

1. Consulte uma NF-e e confirme que o XML foi armazenado no cache compartilhado.
2. Troque o líder sem revogar nenhum PC.
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
- [ ] após recuperação, a eleição volta ao estado de exatamente um líder;
- [ ] o Windows não apresenta uma cópia offline da fila como se fosse o compartilhamento real.

### 7. A1 nos candidatos

Em cada PC que pode virar líder, valide uma consulta conhecida quando ele estiver na liderança.

Esperado:

- [ ] certificado selecionado está disponível no `CurrentUser\My`;
- [ ] chave privada está acessível ao usuário do app;
- [ ] UF autora está correta;
- [ ] consulta funciona em cada candidato.

### 8. Pareamento one-shot

1. Gere um código no líder atual.
2. Autorize um PC novo.
3. Depois do sucesso, tente usar o mesmo código em outro PC.

Esperado:

- [ ] a primeira autorização conclui somente após importar/validar o estado seguro do grupo;
- [ ] o código é consumido depois do primeiro sucesso;
- [ ] o mesmo código não autoriza um segundo PC;
- [ ] clique ou `Enter` duplicado no mesmo PC não cria autorizações duplicadas.

### 9. Gerenciamento e revogação de PC

1. No líder atual, abra **Configurar**.
2. Confira a lista de PCs autorizados.
3. Revogue um PC de teste que não seja o líder atual.
4. Mantenha pelo menos outro candidato autorizado disponível.

Esperado:

- [ ] a lista não expõe segredo criptográfico do cliente;
- [ ] o líder atual não pode ser removido acidentalmente pela interface;
- [ ] a revogação gera nova chave de estado e nova identidade RSA;
- [ ] o PC removido deixa de constar na lista autorizada;
- [ ] apenas os PCs restantes recebem o novo estado;
- [ ] o cooldown fiscal permanece preservado;
- [ ] o cache compartilhado anterior é purgado;
- [ ] um PC restante continua conseguindo operar depois de importar a nova identidade.

### 10. Candidato offline durante rotação

1. Tenha pelo menos três PCs autorizados: A, B e C.
2. Deixe C offline.
3. No líder, revogue um PC de teste/execute uma rotação válida que mantenha C autorizado.
4. Se possível em ambiente de teste controlado, faça uma segunda rotação válida antes de religar C.
5. Religue C.

Esperado:

- [ ] C aceita a identidade nova somente por uma cadeia RSA assinada a partir do pin que já conhecia;
- [ ] uma sequência válida A→B→C é aceita;
- [ ] uma identidade não ligada à cadeia confiável é rejeitada;
- [ ] C volta a poder participar da eleição depois de importar o novo estado.

### 11. Recuperação de rotação interrompida

Este cenário deve ser feito somente em ambiente de teste controlado.

1. Inicie uma revogação/rotação.
2. Interrompa o líder depois que `status\rotation.json` existir.
3. Inicie outro candidato autorizado.

Esperado:

- [ ] nenhum novo trabalho fiscal começa enquanto a rotação está pendente;
- [ ] o candidato autorizado conclui ou recupera a rotação antes de virar líder operacional;
- [ ] o estado final não mistura identidade/lista/cooldown de gerações diferentes;
- [ ] `rotation.json` e artefatos preparados são limpos somente após conclusão segura.

### 12. Atualização assinada

Use uma release oficial produzida pelo `Release Bridge`.

Esperado:

- [ ] a release contém `Nfe-Agendamento-win-x64.zip`;
- [ ] a release contém `Nfe-Agendamento-win-x64.zip.sigstore.json`;
- [ ] o ZIP possui digest SHA-256 publicado;
- [ ] o bundle Sigstore é keyless e corresponde ao workflow oficial `release-bridge.yml@refs/heads/main`;
- [ ] pacote/bundle alterados são rejeitados;
- [ ] health check em até 20 segundos confirma que o aplicativo iniciou;
- [ ] falha do health check restaura a instalação anterior.

Para **v0.1.31 e posteriores**, acrescente:

- [ ] `/api/bootstrap` responde `appVersion` igual à versão recém-instalada;
- [ ] um HTTP 2xx de uma versão diferente não é aceito como health check válido;
- [ ] versão diferente na porta local provoca rollback.

## Portal Nacional / WebView2

Em pelo menos um PC autorizado em standby:

- [ ] **Baixar pelo Portal** abre o host oficial esperado;
- [ ] WebView2 usa o certificado A1 local correto;
- [ ] hCaptcha continua manual;
- [ ] XML baixado é validado contra a chave solicitada;
- [ ] XML válido entra no cache compartilhado;
- [ ] a interface local carrega a NF-e sem nova consulta automática à SEFAZ;
- [ ] uma segunda janela de contingência não abre enquanto a primeira ainda está ativa;
- [ ] fechar a janela durante navegação/download não derruba o app por callback tardio do WebView2.

## Cancelamento fiscal

No lote ou em consulta iniciada, valide que:

- [ ] cancelar impede o início dos próximos itens;
- [ ] trabalho ainda não iniciado é interrompido;
- [ ] uma operação fiscal que já pode ter alcançado a SEFAZ não é repetida automaticamente apenas porque o usuário cancelou/fechou a interface.

## Cobertura automatizada relacionada

O CI cobre eleição de um único líder, takeover, conservação/rotação controlada da identidade, fail-closed após perda de lease, não repetição de pedido fiscal ambíguo, cache compartilhado, fencing imediatamente antes da SEFAZ, bootstrap recuperável, pareamento one-shot, staging de rotação, cadeia RSA assinada, gerenciamento de PCs, rollback do atualizador, health check vinculado à versão e tratamento estreito de falhas de ciclo de vida do WebView2.

Os checkboxes acima permanecem deliberadamente manuais porque validam diferenças reais de SMB, Windows Certificate Store, rede, WebView2 e processo entre máquinas distintas; eles não devem ser marcados como concluídos apenas com evidência de CI.
