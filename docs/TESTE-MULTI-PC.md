# Validação física multi-PC

Este roteiro valida em máquinas Windows reais o que o CI não consegue provar sozinho: comportamento do lock via SMB, certificado A1 em cada PC, cooldown compartilhado, contingência pelo Portal e atualização.

## Preparação

Use pelo menos 2 PCs confiáveis com:

- a mesma versão do NFe Agendamento;
- acesso de leitura/gravação a `P:\01-Nfe agendamento`;
- certificado A1 válido instalado/configurado localmente em cada PC;
- UF autora correta;
- SMB normal, sem Offline Files/Arquivos Offline ou cache desconectado.

Não provoque `cStat=656` real somente para teste.

## Critérios de aceitação

### 1. Inicialização simultânea

1. Feche o app nos dois PCs.
2. Abra o app nos dois quase ao mesmo tempo.
3. Abra **Status da fila**.

Esperado:

- [ ] ambos mostram **Coordenação por pasta compartilhada**;
- [ ] ambos mostram a mesma pasta;
- [ ] ambos indicam a pasta como disponível;
- [ ] não existe estado de líder, standby ou pareamento.

### 2. Certificado local em cada PC

Em cada computador:

1. abra **Configurar**;
2. selecione o certificado A1 e a UF autora;
3. faça uma consulta conhecida.

Esperado:

- [ ] certificado está em `CurrentUser\My`;
- [ ] chave privada está acessível ao usuário do app;
- [ ] a consulta funciona independentemente em cada PC;
- [ ] nenhum certificado, PFX ou senha aparece na pasta compartilhada.

### 3. Lock fiscal entre PCs

1. Use duas NF-e diferentes, uma em cada PC.
2. Inicie as consultas praticamente ao mesmo tempo.
3. Observe `status\fiscal.lock` e os logs locais.

Esperado:

- [ ] apenas um PC entra no trecho fiscal por vez;
- [ ] o segundo aguarda o handle exclusivo ficar disponível;
- [ ] a segunda consulta começa após a primeira liberar o lock;
- [ ] não existe `central.lock`, eleição ou heartbeat necessário para isso.

### 4. Cache é local

1. Consulte uma NF-e no PC A.
2. Consulte a mesma chave novamente no PC A dentro de 24 horas.
3. Consulte a mesma chave no PC B.

Esperado:

- [ ] a segunda consulta no PC A usa o cache local;
- [ ] o XML não aparece em `P:\01-Nfe agendamento\cache` como cache operacional novo;
- [ ] o PC B só usa cache se já possuir sua própria cópia local;
- [ ] o cache local fica em `%LOCALAPPDATA%\NfeAgendamento\cache` e permanece criptografado por DPAPI.

### 5. Perda do compartilhamento

1. Interrompa o acesso do PC A a `P:\01-Nfe agendamento`.
2. Tente iniciar uma nova consulta que não esteja no cache local.
3. Restaure o compartilhamento e repita.

Esperado:

- [ ] nenhuma nova chamada fiscal é iniciada enquanto a pasta estiver indisponível;
- [ ] a interface informa a indisponibilidade;
- [ ] após a recuperação do compartilhamento, a consulta volta a funcionar;
- [ ] o Windows não apresenta uma cópia offline como se fosse o compartilhamento real.

### 6. Cooldown compartilhado

Não force um `656` real. Valide esse cenário apenas se ele ocorrer naturalmente ou em ambiente controlado.

Esperado quando um PC registrar o bloqueio:

- [ ] `status\fiscal-cooldown.bin` é atualizado;
- [ ] o outro PC observa o mesmo `blockedUntilUtc`;
- [ ] nenhum dos dois envia nova consulta enquanto o cooldown estiver ativo;
- [ ] após o prazo, consultas podem voltar a ocorrer normalmente.

### 7. Encerramento inesperado durante o lock

Somente em teste controlado:

1. inicie uma consulta no PC A;
2. encerre o processo enquanto ele mantém o lock;
3. tente uma consulta no PC B.

Esperado:

- [ ] o sistema operacional libera o handle exclusivo do PC A;
- [ ] o PC B consegue adquirir o lock depois da liberação;
- [ ] nenhuma repetição automática é feita para uma operação do PC A que possa já ter alcançado a SEFAZ.

### 8. Consulta em lote

Em um PC:

- [ ] até 50 chaves únicas são aceitas;
- [ ] duplicatas são removidas;
- [ ] os itens fiscais são serializados;
- [ ] cancelar impede o início dos próximos itens;
- [ ] um eventual `656` interrompe o restante do lote.

Enquanto o lote estiver trabalhando, faça uma consulta no segundo PC.

- [ ] o segundo PC também respeita o mesmo `fiscal.lock`.

### 9. Portal Nacional / WebView2

Em pelo menos um PC com A1 local:

- [ ] **Baixar pelo Portal** abre o host oficial esperado;
- [ ] WebView2 usa o certificado A1 local correto;
- [ ] hCaptcha continua manual;
- [ ] XML baixado é validado contra a chave solicitada;
- [ ] XML válido entra no cache local daquele PC;
- [ ] a interface local carrega a NF-e sem nova consulta automática à SEFAZ;
- [ ] uma segunda janela de contingência não abre enquanto a primeira está ativa;
- [ ] fechar a janela durante navegação/download não derruba o app por callback tardio do WebView2.

### 10. Atualização assinada

Use uma release oficial produzida pelo fluxo do projeto.

Esperado:

- [ ] a release contém o ZIP oficial Windows e o bundle Sigstore correspondente;
- [ ] hash e identidade do workflow são validados;
- [ ] pacote/bundle alterados são rejeitados;
- [ ] health check confirma a versão exata iniciada;
- [ ] falha do health check restaura a instalação anterior.

### 11. Migração da arquitetura antiga

Antes de retomar uso simultâneo:

1. feche versões antigas em todos os PCs;
2. atualize todos para a versão nova;
3. confirme o A1 local em cada máquina;
4. confirme acesso ao compartilhamento;
5. abra os PCs novamente;
6. faça o teste de lock simultâneo.

Esperado:

- [ ] nenhum PC pede código de pareamento;
- [ ] nenhum PC tenta virar líder;
- [ ] arquivos antigos de Central/pareamento podem permanecer no compartilhamento, mas não são usados pelo fluxo novo;
- [ ] a coordenação ativa acontece por `status\fiscal.lock` e `status\fiscal-cooldown.bin`.

## Cancelamento fiscal

No lote ou consulta iniciada, valide que:

- [ ] cancelar impede o início de trabalho ainda não iniciado;
- [ ] uma operação que já pode ter alcançado a SEFAZ não é repetida automaticamente;
- [ ] o lock é liberado ao concluir/encerrar a operação.

## Cobertura automatizada relacionada

O CI cobre, entre outros:

- lock compartilhado entre instâncias;
- fail-safe sem pasta compartilhada;
- cooldown compartilhado sem chave de grupo;
- cache XML local criptografado;
- ausência de Central e endpoints de pareamento na composição ativa;
- segurança de caminhos/reparse points;
- política conservadora para erros fiscais;
- loopback/Host/Origin/CSRF;
- Portal/WebView2;
- DANFE;
- atualização e rollback.

Os checkboxes acima permanecem manuais porque dependem de comportamento real do SMB, Windows Certificate Store, rede, WebView2 e processos em máquinas diferentes.