# Inicialização e atualização do NFe Agendamento

## Arquitetura vigente

Cada PC executa sua própria cópia do `NfeAgendamento.App.exe` e atende somente em:

```text
http://127.0.0.1:17345
```

Todos os computadores usam a mesma pasta para coordenação:

```text
P:\01-Nfe agendamento
```

Não existe Central, líder, standby, pareamento, servidor HTTP na LAN, mDNS ou configuração automática de firewall.

Cada PC precisa ter:

- acesso de leitura/gravação à pasta compartilhada;
- certificado A1 válido instalado no `CurrentUser\My`;
- certificado selecionado no NFe Agendamento;
- UF autora configurada.

A pasta compartilhada deve usar SMB normal com locks exclusivos. Não use Offline Files/Arquivos Offline nem cache desconectado.

## Inicialização normal

O executável oficial é:

```text
NfeAgendamento.App.exe
```

O argumento legado `--lan` pode permanecer em atalhos antigos, mas é ignorado e nunca expõe a porta 17345 para outros computadores.

Ao iniciar:

1. o serviço HTTP local sobe em loopback;
2. o app verifica a pasta compartilhada;
3. a interface permanece disponível mesmo se a pasta estiver fora do ar;
4. nenhuma nova chamada à SEFAZ é permitida enquanto o compartilhamento estiver indisponível;
5. quando a pasta volta, novas consultas podem adquirir o lock normalmente.

A janela **Status da fila** mostra o PC local, o caminho compartilhado e a disponibilidade da pasta. Não existem estados de líder ou standby.

## Coordenação entre PCs

Antes de uma operação fiscal, o app adquire exclusivamente:

```text
P:\01-Nfe agendamento\status\fiscal.lock
```

Enquanto um PC mantém esse arquivo aberto com `FileShare.None`, os demais aguardam. O lock é liberado ao final da operação ou pelo Windows se o processo terminar.

O cooldown de `cStat=656` fica em:

```text
P:\01-Nfe agendamento\status\fiscal-cooldown.bin
```

Assim, todos os PCs observam o mesmo bloqueio antes de chamar a SEFAZ.

## Certificado A1

O certificado é estritamente local. Em cada PC:

1. instale o A1 no usuário do Windows que executará o app;
2. abra **Configurar**;
3. selecione o certificado;
4. informe a UF autora;
5. salve;
6. confirme que a pasta compartilhada aparece como disponível;
7. valide uma NF-e conhecida.

Não há código de autorização ou pareamento.

PFX, senha e chave privada nunca devem ser colocados no repositório ou no compartilhamento.

## Cache XML

O cache operacional é local:

```text
%LOCALAPPDATA%\NfeAgendamento\cache
```

Ele é protegido por DPAPI e possui retenção padrão de 24 horas. O XML não é usado como estado compartilhado entre PCs.

Arquivos antigos de cache compartilhado podem permanecer depois de upgrades, mas não fazem parte do fluxo vigente.

## Portal Nacional

Após `cStat=656`, **Baixar pelo Portal** pode ser usado no PC que possui A1 local e WebView2 disponível.

O hCaptcha permanece manual.

O XML baixado:

1. é validado contra a chave solicitada;
2. entra no cache local daquele PC;
3. é detectado pela interface por polling local de cache;
4. não provoca uma nova consulta automática à SEFAZ.

## Iniciar com o Windows

A opção **Iniciar com o Windows** registra o executável no perfil atual em:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

Se o executável for movido, desmarque e marque novamente a opção para atualizar o caminho.

## Atualizador integrado

A ação **Verificar atualização** fica no menu da bandeja.

O atualizador aceita somente pacote oficial verificável:

- release oficial do projeto;
- versão maior que a instalada;
- asset `Nfe-Agendamento-win-x64.zip`;
- bundle `Nfe-Agendamento-win-x64.zip.sigstore.json`;
- HTTPS com origem esperada;
- tamanho dentro dos limites;
- SHA-256 válido;
- assinatura Sigstore keyless vinculada ao workflow oficial `release-bridge.yml` em `main`;
- transparency log válido.

Depois da confirmação, o atualizador:

1. baixa e valida o ZIP;
2. valida o bundle Sigstore;
3. extrai somente depois das verificações criptográficas;
4. prepara a versão nova em área separada;
5. encerra/troca a instalação;
6. inicia a nova versão;
7. valida `http://127.0.0.1:17345/api/bootstrap` por até 20 segundos;
8. exige `appVersion` exatamente igual à versão preparada;
9. faz rollback se o health check falhar.

Os dados em `%LOCALAPPDATA%\NfeAgendamento` não são substituídos.

## Atualização manual segura

Para atualizar manualmente vários PCs:

1. encerre o NFe Agendamento por **Sair** em todos os computadores que usam o mesmo compartilhamento;
2. preserve uma cópia da versão anterior;
3. use somente arquivos da release oficial validada;
4. atualize **todos os PCs** antes de retomar consultas simultâneas;
5. abra os PCs novamente;
6. confirme o A1 local e a disponibilidade de `P:\01-Nfe agendamento` em cada máquina;
7. valide uma consulta em cada PC;
8. faça o teste de duas consultas simultâneas para confirmar o lock compartilhado;
9. valide DANFE, XML, lote e Portal quando aplicável.

Não misture por longos períodos versões antigas baseadas em líder/pareamento com a arquitetura atual baseada em `fiscal.lock`.

## Migração de versões antigas com Central/pareamento

A versão nova não usa:

- `central.lock`;
- heartbeat de líder;
- identidade/chave de grupo;
- lista de clientes autorizados;
- códigos de pareamento;
- fila remota de pedidos/respostas;
- cache XML compartilhado.

Arquivos antigos podem continuar fisicamente na pasta ou em `%LOCALAPPDATA%`, mas não controlam a operação atual.

A migração operacional é simples:

1. feche as versões antigas;
2. atualize todos os PCs;
3. confirme o certificado local em cada PC;
4. confirme o compartilhamento;
5. reabra o aplicativo;
6. execute o roteiro multi-PC.

## O que permanece persistente

Em `%LOCALAPPDATA%\NfeAgendamento` podem existir:

- seleção local do certificado/UF;
- cache XML local criptografado;
- auditoria fiscal;
- perfil local do WebView2;
- material legado de versões anteriores.

No compartilhamento, o fluxo atual depende operacionalmente de:

```text
.nfe-agendamento
status\fiscal.lock
status\fiscal-cooldown.bin
```

Diretórios e estados antigos podem existir por compatibilidade, mas não são usados para encaminhar consultas ou armazenar XML novo.

## Recuperação de falha

Se uma atualização falhar antes de instalar, a versão atual permanece. Não desative SHA-256, Sigstore ou outras validações para contornar o problema.

Se uma versão nova não iniciar, o atualizador tenta restaurar o backup automaticamente. Se ainda houver intervenção manual:

1. encerre processos restantes;
2. restaure a pasta anterior do executável;
3. não apague `%LOCALAPPDATA%\NfeAgendamento` sem diagnóstico;
4. confirme o CI/commit da versão antes de tentar novamente.

Se o app abrir, mas a pasta compartilhada aparecer indisponível, corrija primeiro o acesso ao compartilhamento. O app não deve enviar nova consulta fiscal sem esse estado saudável.

## Cancelamento fiscal conservador

Cancelar uma ação impede trabalho ainda não iniciado e, em lote, impede os próximos itens. Depois que uma operação pode ter alcançado a SEFAZ, o app não força cancelamento seguido de retry automático.

Essa regra evita duplicidade em resultados ambíguos.

## Modelo de ameaça local

A API em `127.0.0.1` não fica exposta à LAN, mas loopback não isola processos ou usuários do mesmo Windows. O app assume PCs corporativos confiáveis.

No compartilhamento ficam somente estados operacionais de coordenação; certificado, chave privada, senha e XML permanecem locais.

## Validação após atualização

O mínimo recomendado é:

- [ ] todos os PCs na mesma versão;
- [ ] pasta compartilhada disponível em pelo menos dois PCs;
- [ ] Offline Files desabilitado para esse compartilhamento;
- [ ] A1 local configurado nos dois PCs;
- [ ] consulta individual funcionando em cada PC;
- [ ] duas consultas simultâneas serializadas por `fiscal.lock`;
- [ ] indisponibilidade do compartilhamento bloqueando nova chamada fiscal;
- [ ] cooldown compartilhado observado quando aplicável;
- [ ] DANFE/XML funcionando;
- [ ] lote validado;
- [ ] Portal validado em pelo menos um PC;
- [ ] atualização oficial passando pelo health check e rollback.

O roteiro completo está em [Validação física multi-PC](TESTE-MULTI-PC.md).
