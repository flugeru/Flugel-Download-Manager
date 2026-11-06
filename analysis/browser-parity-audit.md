# Auditoria de integração navegador — IDM versus contrato

Data: 2026-11-06. Resultado: **paridade parcial, não demonstrada em navegador real**. O risco prioritário é cancelar um download do navegador ao receber apenas confirmação de abertura/encaminhamento para o desktop. Redirects, POST e recuperação de handoff após interrupção do worker também permanecem incompletos.

### Atualização do implementador, após a auditoria

O texto abaixo registra o estado lido antes das alterações paralelas. Etapa posterior corrigiu uso de URL final/nome, transporte validado do User-Agent observado e primeira escrita de mídia após restauro. Métodos desconhecidos/POST/ambíguos ficam no navegador; isso não implementa replay POST. Redirects do motor removem credenciais entre origens e rejeitam downgrade HTTPS→HTTP.

Fixture real Helium `auth-live-iOwA43` confirmou redirect com cookie e User-Agent obrigatórios, arquivo de 4.194.304 bytes/hash correto no FDM e cópia do navegador cancelada; POST ficou no navegador e concluiu/hash correto. Detalhes em `browser/REAL-SITE-CHECK.md`. Não foi execução real do IDM nem prova de paridade universal. Aceite durável, ledger/reconciliação, suspensão real, iframe/SPA e portabilidade continuam pendentes. O título “não demonstrada” refere-se à paridade, não à ausência de qualquer teste real do FDM.

## Escopo e classificação

- **Observação:** fato encontrado em artefato estático, fonte atual ou check com APIs simuladas. Não equivale a execução real do IDM.
- **Inferência:** consequência plausível dos fatos; precisa de teste no ambiente indicado.
- **Desconhecido:** evidência disponível não resolve a questão.
- **Recomendação:** política independente de segurança/confiabilidade, não comportamento proprietário a reproduzir.

Foram lidos `IDM-NATIVE-ANALYSIS.md`, `integration-contract.md`, `inventory-report.md`, `MEDIA-AND-IPC-ANALYSIS.md`, inventários e exports REA existentes antes de qualquer aprofundamento. Reutilizados os registros Ghidra 12.1 e recibos de fechamento. Nenhuma sessão nativa foi aberta nesta auditoria; nenhum reparo de MCP, instalação ou configuração global foi necessário. O fallback `rea-local-session.mjs` não precisou ser executado. Não foram executados IDM, seus scripts ou seu instalador, nem acessados perfil de navegador, dados ou credenciais reais.

Único arquivo produzido por esta tarefa: este relatório. Nenhum código app/browser alterado; nenhum código proprietário incluído no relatório ou destinado à implementação. Análise de contexto seguro de requests pertence ao implementador principal; aqui ficam apenas requisitos comportamentais e limites.

## Fontes e Evidence

### Evidência REA/Ghidra reutilizada

| Referência | Evidence ID | Export / alcance |
|---|---|---|
| H3 | `ev_25a8ff4ea0f39d25f3056680f10d13926fc460109a1d20d1a3d15fbc2234ceb6` | `idmmsghost-evidence.bundle.json`; inventário de funções/imports do host |
| H4 | `ev_f2bba580ef73fd90541827743174df1a5492fa821ffcd2120e244257f486b2e8` | Mesmo export; ponte stdio/IPC, enquadramento e lifecycle estáticos |
| H5 | `ev_d806c1aeec73833b7a7ef4c44cbbc153627006dcafbe05bb58648821b7d1f710` | Mesmo export; codificação do envelope |
| H6 | `ev_0b016c5ba9939b96d37a217f985ef4d005fb124cee6750f9ee0dacd1ae692cea` | Mesmo export; decodificação do envelope |
| H12 | `ev_1a2781c447923e4ddffdf258d95ba059d691af82121c23dd0b9cde19b05e260c` | Mesmo export; dossier de I/O |
| H13 | `ev_2fbfdd6196410e4ecc9665395b272977629849307d3a468d4e047cdde1ff4304` | `idmmsghost-worker-evidence.bundle.json`; worker de I/O **nativo**, não service worker MV3 |
| D6 | `ev_7a75ae18ad32f4bf5cc911ce4c7db833906f98979843662b4fe9dd7459aea5e8` | `idman-focused-evidence.bundle.json`; montagem de requests HTTP em ramo inspecionado |
| D9 | `ev_6ed89a6b1dd41de5cfd029fe8302a33705086538156b61e31a5593edcb18a7b6` | Mesmo export; parsing de metadados HTTP, sem prova de replay browser/desktop |

Validação local desta auditoria: exports JSON legíveis, respectivamente 13/14/13 registros; recibos `host-close.json`, `host-worker-close.json`, `idman-focused-close.json` concordam com essas contagens. SHA-256 dos dois executáveis originais continuam iguais aos registrados:

- `IDMMsgHost.exe`: `dba25a49adb88f675db26d2dc7a0fa9d4a5db2326858cb9d2515f6f34b8e0b65`.
- `IDMan.exe`: `a705c0154fafc7cd509decf165cb6f6c050b6521b40adb6f730dd827c86051c7`.

Oito scripts extraídos do pacote Chrome tiveram SHA-256 conferido contra `integration-script-index.json`. Isso confirma identidade com o inventário, não comportamento de execução nem autenticidade independente das ferramentas. Proveniência oficial de Ghidra/Java continua não verificada, conforme relatório nativo.

### Outras fontes, sem IDs REA

| Referência | Fonte / limite |
|---|---|
| P1 | `integration-contract.md`, §§2–8; contrato sanitizado e distinção entre observado/recomendado |
| P2 | `integration-script-index.json`, `integration-packages.json`, `inventory-report.md`; inventário estático, não execução |
| P3 | Pacote Chrome `background.js`, linhas 29–30, 51–54, 104–108, 125: eventos, URL final, associação de request/download, POST e controle pause/resume/cancel. Apenas resumo comportamental; arquivo proprietário não é entrada de implementação |
| S1 | `browser/extension/service-worker.js`: `recordMedia`, `mediaForTab`, `sendNative`, `enqueueLink`, listeners webRequest, `interceptDownload` e mensagens runtime |
| S2 | `browser/extension/content.js`: `collectVariants`, `renderMenu`, `scan`, `observe`; `browser/extension/manifest.json` |
| S3 | `app/Flugel.NativeHost/Program.cs`: `RunWithPayload`, `ParseStructuredRequest`, `ForwardToRunningInstance`, `ForwardOrLaunchDesktop` |
| S4 | `app/Flugel.Desktop/App.xaml.cs`: `HandleIpcRequestAsync`; confirmação de apresentação não é comprovação de transferência concluída |
| T1 | `node browser/check-media-bridge.cjs`: PASS nesta auditoria, sem argumento executável; APIs Chrome simuladas, sem host/desktop real |
| T2 | Probes adicionais em Node/vm contra S1, sem gravação de arquivos: URL inicial/final, candidato POST e reidratação de candidatos após worker novo; resultados descritos abaixo |

S1–S4 são observações da árvore de trabalho no momento da leitura; o implementador principal pode alterá-las em paralelo. Não usar números de linha como identidade permanente.

## Observações e lacunas

### 1. Transporte nativo não comprova replay HTTP

**Observado no IDM:** H4–H6 mostram framing separado do envelope de aplicação e forwarding bidirecional; H12/H13 mostram tratamento de transferências de I/O parciais. H3 não apresenta imports de rede comuns no inventário recuperado do host. **Inferência limitada:** esse host atua principalmente como adaptador IPC, não como responsável pelo download. Nada disso prova fidelidade de headers, redirects, POST ou blobs no navegador. D6/D9 cobrem ramos HTTP do gerenciador, não associação de uma requisição browser específica a esses ramos.

O protocolo próprio com JSON objeto é uma divergência **intencional e correta** em relação ao envelope proprietário. Não falta reproduzir opcodes, handshake ou nomes internos. O contrato novo ainda precisa de ID de pedido e resposta correlacionada para reconciliação/idempotência; S1/S3 não os possuem. [P1 §§2–3; H4–H6]

### 2. Aceite para confirmação não é aceite durável

**Observado:** S1 considera sucesso apenas `version === 1` e `status === "accepted-for-confirmation"`; `interceptDownload` então cancela o download pausado. S3 pode devolver esse status após `Process.Start` retornar, sem confirmar que a janela abriu ou que o job foi persistido. No caminho IPC, S4 usa a aceitação da janela de confirmação. T1/T2 confirmam que esse status basta para produzir `pause`, depois `cancel`.

**Inferência:** usuário cancelar a confirmação, falha do desktop antes da persistência ou crash após o lançamento pode deixar o navegador cancelado sem job recuperável. Não foi provocada perda real de arquivo. **Lacuna contratual:** P1 §§2,4,8 exige fallback quando o handoff não se completa; explicita que confirmação e conclusão são estados diferentes. Aguardar aceite durável ou preservar controle do navegador até uma decisão recuperável é requisito independente, não paridade IDM verificada.

### 3. Identidade da requisição insuficiente

**Observado:** pacote IDM associa metadados de request/frame/download e usa URL final ou inicial como fallback. Existem branches para GET/POST e disponibilidade de corpo; isso não garante cobertura de todo POST. S1 registra candidatos por URL e tab, sem preservar método, requestId, documentId/frameId, status de descoberta ou cadeia de redirect. O handoff automático usa `item.url`, não `item.finalUrl`. T2 passou um item com URLs distintas e confirmou envio da inicial. Um evento POST com extensão de mídia também foi aceito como candidato sem conservar seu método. [P3; S1; T2]

**Inferência:** URL isolada pode corresponder a várias requisições/frames/métodos; retry de uma URL inicial descartável pode divergir do download já autorizado no navegador. Uma nova busca GET não substitui automaticamente a requisição original. Não foi verificada falha real em servidor.

### 4. Metadados de mídia não são comprovação de download possível

**Observado:** S1 reconhece extensão de arquivo, MIME e Content-Disposition; o observer inicial registra antes da resposta e o observer de headers ignora status >=400, mas não elimina o candidato inicial. Não há listener de erro/conclusão para corrigir esse estado. S2 aceita apenas HTTP(S) e combina candidatos por tab com fontes DOM; `all_frames` não cria associação entre uma candidata de rede e um player específico. A resposta `get-page-media` pode oferecer a cada iframe as candidatas de toda a tab. [S1/S2]

**Inferência:** URL que falha depois, redirect intermediário ou mídia de outro frame pode aparecer como opção. MIME/extensão tampouco provam ausência de proteção, replay autorizado ou completude de stream. Blob/MediaSource pode ter URLs HTTP relacionadas, mas essa associação não prova que sejam um arquivo independente.

### 5. Worker: restauração parcial e handoff sem reconciliação

**Observado:** S1 grava listas de mídia em `storage.session` e consegue lê-las após perder o Map. Contudo `recordMedia` cria Map vazio quando o worker recomeça e grava apenas as novas observações. T2 criou novo contexto VM mantendo o mesmo armazenamento: as candidatas antigas eram legíveis; a primeira candidata nova substituiu a lista armazenada por uma lista de um item.

**Observado:** S1 não mantém ledger de downloads pausados/pedidos pendentes, deadline de aplicação para resposta nativa nem reconciliação no startup. O `finally` restaura em rejeição/exceção normal; não é proteção contra encerramento abrupto do worker. O check T1 chamado de suspensão limpa o Map, mas não testa a primeira gravação posterior, interrupção de processo ou handoff pendente.

**Inferência:** término inesperado pode deixar download pausado; retry após resposta perdida pode duplicar solicitação. Isso é risco arquitetural, não falha medida em navegador real. H13 trata worker de I/O nativo e **não** resolve lifecycle MV3.

## Matriz de casos

Todos os resultados IDM abaixo são estáticos. A coluna de aceitação define testes futuros independentes, não promete reprodução de comportamento proprietário.

| Caso | IDM observado / desconhecido | Implementação lida | Lacuna e aceitação necessária |
|---|---|---|---|
| GET público direto | Handoff por link/request e controle de download existem; sucesso E2E não comprovado [P1/P3] | Context menu, fontes DOM e enqueue HTTP(S) presentes [S1/S2] | **Parcial.** Fixture própria deve confirmar bytes/hash e estado desktop; não rotular enqueue como conclusão |
| Redirect GET mesma origem | Pacote usa URL final e pode transportar URL inicial distinta [P3]; cadeia completa não estabelecida | Automático envia `item.url` [S1/T2] | **Lacuna.** Testar 301/302/303/307/308, URL final distinta e associação sem duplicar candidata |
| Redirect entre origens / URL descartável | Tratamento exato de consentimento e credenciais desconhecido | Não há modelo explícito de cadeia/identidade em S1 | **Desconhecido E2E.** Preservar fallback; não repetir automaticamente URL descartável nem propagar contexto sensível para outra origem. Contexto seguro fica com implementador principal |
| Headers de resposta | Classificador usa MIME/disposição/metadados [P1/P3]; D9 não prova classificação browser | S1 usa MIME, nome e Content-Length, sem manter estado de sucesso/erro | **Parcial.** Testar nome UTF-8, comprimento ausente, MIME sem extensão e falha depois da descoberta; não anunciar tamanho final com base só nesse header |
| Headers de request / sessão | Pacote tem capacidade de serialização [P1 §6]; fidelidade runtime desconhecida | Subconjunto de contexto, não replay fiel [S1/S3] | **Fora deste ajuste.** Requer autorização, minimização e escopo por request/origem; ausência de contexto deve resultar em rejeição/fallback, não coleta global |
| POST / formulário | Branches POST e corpo raw/formData observados; uploads não representáveis têm limites [P3] | Candidata POST perde método; protocolo lido não transporta corpo/método [S1/S3/T2] | **Não suportado fielmente.** Não assumir GET nem cancelar navegador sem saber método. Testar POST, multipart e 307/308 preservando método em fixture controlada; manter no browser enquanto não houver suporte explícito |
| `blob:` / `data:` / MediaSource | Não há prova de handoff genérico de blob no material inspecionado; bridge de página não demonstra isso [P1/P2] | S1/S2 rejeitam URL não HTTP(S); automático retorna antes de pause para blob | **Limite intencional.** Blob deve continuar no browser sem envio nativo; relação blob/segmentos não vira suporte universal |
| Host ausente, proibido ou rejeição | Pacote possui lifecycle/disconnect/retry [P1/P3]; garantia runtime desconhecida | Erros categorizados; handoff normal rejeitado tenta resume; T1 cobre mocks | **Parcial.** Testar permissão negada, host termina, resposta inválida e desktop indisponível em navegador real; browser continua |
| Aceite apenas para confirmação | Sem prova de garantia durável IDM [H4/P3] | Status de confirmação provoca cancel [S1/S3/S4/T1/T2] | **Crítico.** Fechar/cancelar diálogo e matar desktop após lançamento não pode descartar a única transferência recuperável |
| Falha de pause/cancel/resume | Há branches de pause/resume/cancel, sem garantia exaustiva [P3] | T1 cobre pause/cancel falhando; falha de resume só gera orientação [S1] | **Parcial.** Testar item já completo, cancel falhando e resume indisponível; resultado explícito, sem anunciar takeover concluído |
| Worker reiniciado, ocioso ou crash | MV3 declarado no pacote Chrome; algoritmo de recuperação não demonstrado [P2] | Snapshot de mídia parcialmente restaurado; ledger de handoff ausente [S1/T2] | **Lacuna.** Restart antes/depois de pause e ACK deve reconciliar sem perda/duplicação; nova observação deve preservar candidatas válidas anteriores |
| Navegação SPA / iframe removido | Limpeza por frame/navegação observada no pacote [P1/P2] | Limpeza por tab/loading; candidatas de rede por tab; S2 não rastreia ciclo completo de frame/documento | **Parcial.** Duas mídias em frames distintos, remoção e navegação same-document não podem selecionar recurso obsoleto ou de outro player |

## Recomendações priorizadas

### P0 — prevenir descarte e replay incorreto

1. Separar pedido recebido, confirmação exibida, aceite durável e transferência concluída. Não cancelar a cópia do navegador em `accepted-for-confirmation`. Definir cancelamento/rejeição/timeout do usuário e falha pós-lançamento; estabelecer teste de recuperação antes de habilitar takeover geral.
2. Restringir takeover a requests comprovadamente reproduzíveis e suportadas. Não converter POST/identidade desconhecida em GET. Conservar browser fallback em blobs, métodos desconhecidos, contexto incompleto e URLs descartáveis. O implementador principal define o contexto seguro; não ampliar coleta para obter paridade aparente.
3. Correlacionar handoff por ID próprio, com reconciliação após resposta perdida/restart. Recuperar downloads que esta integração pausou; não retomar os que o usuário pausou por conta própria. Deadline e estado recuperável são necessários; manter worker vivo indefinidamente não substitui reconciliação.

### P1 — fidelidade de seleção e lifecycle

4. Distinguir URL inicial/final, método e ciclo request/frame/documento; documentar o que não pode ser associado com segurança. Reidratar candidatas antes da primeira escrita após restart e invalidar estados em erro/navegação/remoção. Guardar apenas metadados mínimos não secretos; URL também pode conter token, portanto não registrar nem persistir indiscriminadamente.
5. Testar os casos da matriz em fixture local, com navegador/host próprios, antes de declarar paridade. T1 é útil, mas não verifica APIs reais, suspensão real, permissão, registro do host, usuário cancelando diálogo ou persistência do job. Nenhum teste deste relatório estabelece sucesso E2E.

### P2 — manter limites explícitos

6. Preservar protocolo, identidade e endpoint próprios. Não implementar wire protocol IDM, listener local alternativo, page-world hooks ou captura de corpos para cobrir lacunas sem requisito concreto e autorização. Documentar blob/POST sem suporte e diferenças Chrome/Edge/Firefox; um pacote MV2 antigo não garante funcionamento atual.

## Plataforma e limites de confiança

Documentação oficial Chrome consultada via Context7 nesta auditoria, separada de Evidence REA:

- https://developer.chrome.com/docs/extensions/reference/api/downloads — `url` inicial versus `finalUrl` após redirects; pause/resume podem falhar para item inativo; `DownloadItem` não fornece corpo/método original como garantia de replay.
- https://developer.chrome.com/docs/extensions/reference/api/webRequest — `requestId` associa eventos; corpo exige `requestBody` e tem representações/limites próprios; observar metadados não equivale a interceptação bloqueante universal.
- https://developer.chrome.com/docs/extensions/develop/concepts/service-workers/lifecycle — término por inatividade/limites e necessidade de suportar encerramento inesperado; estado global não é durável. Port nativo persistente não torna crash impossível.

Não houve captura de tráfego, execução da extensão IDM, abertura de browser profile ou inspeção de sessão autenticada. Não estão estabelecidos: sucesso de download browser/IDM, garantias de aceite/recuperação IDM, semântica completa de redirects/POST/blob, correlação exata de requisições com `downloads.onCreated`, compatibilidade corrente Firefox/Edge ou proteção de conteúdo. Strings/imports e estática Ghidra não preenchem esses desconhecidos.

H4 tem corpo não contíguo e limites de recuperação de fluxo; H12/H13 são apenas ramos de I/O nativo. PDBs correspondentes não foram fornecidos; nomes/tipos sintéticos podem estar incorretos. Negativas de inventário não excluem código dinâmico/outros módulos. O teste de retomada segura 200/206/416 e validação de entidade continua fora desta auditoria de integração.

## Verificações reproduzíveis sem IDM

Checks executados nesta auditoria: `node --check browser/extension/service-worker.js` e `node browser/check-media-bridge.cjs` passaram. Para repetir o check existente sem executar host/desktop:

```powershell
node browser/check-media-bridge.cjs
```

As três observações T2 acima foram asserts transitórios, sem fixture nova gravada; não são cobertura permanente. A reprodução com worker realmente encerrado, captura de eventos do navegador e desktop próprio permanece pendente. Não executar `live-*` contra sites ou perfis reais para fechar essas lacunas sem autorização específica.
