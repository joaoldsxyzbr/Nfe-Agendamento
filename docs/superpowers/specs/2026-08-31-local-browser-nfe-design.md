# Design — NFe Agendamento local por PC

**Data:** 2026-08-31  
**Status:** arquitetura aprovada para implementação inicial.

## 1. Objetivo

O projeto será uma ferramenta interna, simples, rápida, segura e objetiva para apenas três usuários.

O fluxo principal é:

1. informar uma chave de NF-e;
2. consultar a SEFAZ usando o certificado A1 já instalado no próprio PC;
3. visualizar a NF-e;
4. baixar o XML;
5. imprimir ou salvar o DANFE;
6. quando necessário, consultar várias chaves conhecidas e gerar um ZIP.

O sistema não deve virar ERP, monitor fiscal, dashboard, gestor administrativo ou plataforma de descoberta automática de documentos.

## 2. Arquitetura

Cada computador terá sua própria instalação do aplicativo Windows.

```text
PC 1
Navegador -> http://127.0.0.1:17345 -> NFe Agendamento local -> certificado A1 local -> SEFAZ

PC 2
Navegador -> http://127.0.0.1:17345 -> NFe Agendamento local -> certificado A1 local -> SEFAZ

PC 3
Navegador -> http://127.0.0.1:17345 -> NFe Agendamento local -> certificado A1 local -> SEFAZ
```

`127.0.0.1` sempre aponta para o próprio computador. Portanto os três PCs podem usar a mesma porta `17345` sem interferência entre si.

Apenas outro processo usando a porta `17345` no mesmo PC poderia causar conflito. Nesse caso o aplicativo deve falhar de forma clara e acionável; não deve trocar silenciosamente de porta e confundir atalhos ou documentação.

## 3. Rede e segurança local

O host HTTP existe somente para servir a interface web local no navegador.

Regras obrigatórias:

- bind exclusivamente em `127.0.0.1`;
- nunca escutar em `0.0.0.0`, IP privado da LAN ou interface pública;
- nenhuma regra de Firewall para acesso de outros computadores;
- nenhuma função de servidor compartilhado;
- sem login ou usuários no fluxo local;
- rejeitar `Host` inesperado;
- validar `Origin` nas requisições mutáveis;
- manter proteção anti-CSRF nas ações fiscais/mutáveis;
- limitar tamanho de payload;
- não expor certificado, chave privada, XML protegido ou segredos em endpoints administrativos.

O navegador deve acessar somente o próprio aplicativo local.

## 4. Aplicativo Windows

O aplicativo Windows roda em segundo plano e hospeda a interface local.

A bandeja deve ser mínima, contendo apenas o necessário:

- Abrir sistema;
- Configurar certificado;
- Verificar atualização;
- Sair.

Não criar painel administrativo grande.

O aplicativo pode iniciar com o Windows, desde que essa opção seja simples e previsível.

## 5. Interface web

A interface deve permanecer pequena e direta.

### Consulta única

- campo para chave de 44 dígitos;
- botão `Consultar`;
- resultado curto;
- `Visualizar NF`;
- `Baixar XML`;
- `Imprimir / Salvar DANFE`.

### Lote

- colar ou importar chaves conhecidas;
- validar e deduplicar;
- até 100 chaves por execução inicialmente;
- botão para consultar e gerar ZIP;
- relatório simples das encontradas e não encontradas.

A interface não deve mostrar NSU, cStat, contadores internos, fila técnica, métricas ou gráficos, exceto quando uma condição exigir ação ou espera do usuário.

## 6. Certificado digital

Cada instalação usa o certificado A1 já existente no Windows Certificate Store daquele PC.

Regras:

- nunca exportar a chave privada;
- nunca enviar PFX/certificado ao navegador, GitHub ou nuvem;
- permitir escolher entre certificados válidos disponíveis localmente;
- lembrar apenas a identificação necessária da seleção;
- validar validade e identidade fiscal antes da consulta;
- se a identidade fiscal mudar, não reutilizar estado sensível como se fosse a mesma empresa.

## 7. Consulta fiscal

O produto trabalha somente com chaves conhecidas.

A consulta normal usa `consChNFe` do `NFeDistribuicaoDFe`.

Não haverá `distNSU` no fluxo do produto.

Motivo: o sistema não precisa descobrir notas automaticamente por CNPJ. `distNSU` adicionaria coordenação e risco desnecessários entre três instalações independentes.

Fluxo:

1. validar chave localmente;
2. verificar cache criptografado;
3. se não houver XML completo válido em cache, consultar a SEFAZ;
4. processar resposta;
5. armazenar XML completo no cache criptografado;
6. disponibilizar visualização/download/DANFE.

## 8. Proteção contra consumo indevido

Não haverá uma cota artificial global de 20 chaves diferentes por hora.

O aplicativo deve:

- evitar repetir automaticamente a mesma chave;
- reutilizar cache válido;
- nunca fazer retry automático de NF-e simplesmente não localizada;
- usar retry apenas em falhas transitórias de rede, com backoff controlado;
- ao receber `cStat=656`, persistir cooldown local de uma hora;
- reiniciar o app ou o Windows não deve limpar esse cooldown;
- não consultar novamente durante cooldown local.

Como os três PCs são independentes, um não conhece preventivamente o cooldown recebido por outro. Essa limitação é aceita para preservar a arquitetura simples. Se a SEFAZ responder `656`, cada instalação deve obedecer imediatamente.

## 9. Lote

O lote é local e sequencial.

Regras:

- somente chaves conhecidas pelo usuário;
- validar antes da rede;
- deduplicar;
- consultar cache primeiro;
- consultar somente ausentes;
- sem chamadas paralelas agressivas;
- sem `distNSU`;
- se uma nota não for encontrada, registrar o resultado e seguir para a próxima;
- erro de rede pode ser repetido com backoff controlado;
- `656` interrompe novas chamadas;
- gerar ZIP somente com XMLs completos encontrados;
- incluir `resultado.txt` curto com resumo e chaves não encontradas.

A V1 não terá fila persistente de lotes. Se o processo for encerrado no meio, o usuário pode reenviar as chaves; os XMLs já obtidos no cache evitam consultas duplicadas enquanto válidos.

## 10. Cache e dados locais

- XMLs criptografados em repouso;
- chave de proteção vinculada ao Windows usando DPAPI;
- retenção padrão de 24 horas;
- limpeza automática;
- não manter ZIP permanentemente em texto aberto;
- gerar ZIP para download e descartar temporários;
- logs sanitizados, sem XML completo, chave privada ou segredos.

Não introduzir banco de dados sem necessidade comprovada.

## 11. DANFE

O DANFE deve ser gerado somente a partir do XML autorizado retornado.

Deve permitir:

- visualização legível;
- impressão A4;
- salvar como PDF pelo mecanismo do navegador;
- código de barras da chave;
- dados fiscais essenciais do XML.

Não inventar dados ausentes do XML.

## 12. Atualização

Cada PC atualiza sua própria instalação.

Requisitos:

- versão oficial publicada no GitHub/site de download;
- validação de integridade por SHA-256 inicialmente;
- evoluir para manifesto assinado criptograficamente;
- Authenticode recomendado quando houver certificado adequado;
- atualização não pode apagar configuração válida, certificado selecionado ou cache protegido sem migração explícita.

## 13. Regra de produto obrigatória

Antes de qualquer nova funcionalidade, perguntar:

1. é necessária para consultar, visualizar, imprimir ou baixar NF-e?
2. melhora segurança, confiabilidade, velocidade ou simplicidade desses fluxos?
3. pode ser resolvida automaticamente sem criar nova tela ou configuração?

Se a resposta não justificar claramente a mudança, não implementar.

A complexidade pode existir no motor, mas deve permanecer invisível para o usuário comum.

## 14. Testes obrigatórios

### Host local

- responde em `127.0.0.1:17345`;
- não responde pelo IP da LAN;
- não aceita bind externo;
- rejeita `Host` inesperado;
- valida origem nas ações mutáveis;
- protege ações fiscais contra CSRF;
- trata porta ocupada com erro claro.

### Fiscal

- rejeita chave inválida antes da rede;
- cache evita consulta desnecessária;
- XML retornado corresponde à chave consultada;
- `137` de consulta direta não cria cooldown global;
- `656` cria cooldown de uma hora e persiste após reinício;
- timeout usa backoff;
- nota não localizada não entra em retry automático;
- lote nunca chama `distNSU`;
- lote é sequencial e deduplicado.

### Segurança

- certificado/chave privada não são expostos pela API;
- cache permanece criptografado;
- logs não contêm XML real ou segredos;
- fixtures de teste são sanitizadas.

### Interface

- consulta única funciona ponta a ponta com serviço fiscal simulado;
- XML pode ser baixado;
- DANFE abre para visualização/impressão;
- lote gera ZIP e `resultado.txt` corretamente.

CI nunca deve chamar a SEFAZ real nem utilizar certificado da empresa.

## 15. Critérios de aceite

A primeira versão estará pronta quando:

- cada um dos três PCs funcionar de forma independente;
- todos puderem usar `http://127.0.0.1:17345` sem interferência entre máquinas;
- desligar um PC não afetar os outros;
- nada fiscal estiver exposto na LAN;
- consulta exigir apenas a chave e uma ação do usuário;
- certificado permanecer somente no Windows local;
- consulta única, XML, DANFE e lote estiverem funcionando;
- cache estiver criptografado com retenção de 24 horas;
- `656` e falhas de rede forem tratados com segurança;
- interface permanecer objetiva e sem funções administrativas desnecessárias;
- um piloto controlado nos três PCs validar o uso real antes de considerar a versão estável.

## 16. Fora de escopo

- servidor compartilhado na LAN;
- login e usuários;
- acesso remoto;
- `distNSU` e descoberta automática de NF-e;
- fila global;
- sincronização entre PCs;
- dashboard;
- métricas operacionais;
- ERP;
- arquivo fiscal permanente;
- integração com sistemas externos;
- múltiplas empresas como produto.
