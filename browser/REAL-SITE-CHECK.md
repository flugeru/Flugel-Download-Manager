# Download autenticado: verificação real

Verificado em 2026-11-06 no perfil existente do Helium, com login ativo em `anitsu.moe` e `nuvem.anitsu.moe`.

## Problemas encontrados
- O perfil real ainda executava StreamHarbor 2.0.0. Publicar arquivos não recarrega uma extensão já carregada. Após clicar em **Recarregar** em `chrome://extensions`, o perfil executou Flugel Download Manager 2.1.0.
- A permissão de cookies estava desligada para `nuvem.anitsu.moe`.
- O site inicia downloads por JavaScript. Não há necessariamente um link HTML ou player que permita usar o balão. O popup agora lista os arquivos observados na rede e permite enviá-los explicitamente ao desktop.
- O mapa do service worker desaparecia após suspensão MV3. Os metadados agora ficam em `chrome.storage.session`, em memória durante a sessão do navegador. Cookies continuam obtidos somente ao enviar um download, nunca armazenados nessa lista.

## Resultado observado
- O arquivo original respondeu HTTP 206 no Helium autenticado.
- Clique em **Baixar no Flugel**, confirmação pela interface WPF, depois **Resume**.
- Quatro segmentos concluídos pelo motor desktop, sem HTTP 401.
- Tamanho final: **346.372.662 bytes**.
- Cabeçalho Matroska: `1A-45-DF-A3`.
- SHA-256 local: `cc93c13f4cc4b1a5589e8855c27b1cc61d12fab691deda7de81aa454fc8a6652`.

O hash identifica o arquivo local; o servidor não forneceu hash para comparação. Não foi validada a reprodução integral do vídeo. Esse resultado usou o envio manual pelo popup, não a interceptação automática.

## Como usar
1. Recarregue a extensão em `chrome://extensions` após atualizar seus arquivos. Recarregue a página do site.
2. No popup, ative o envio de cookies somente para o site desejado.
3. Clique no arquivo no site. Abra o popup e escolha **Baixar no Flugel** em **Arquivos detectados nesta página**.
4. Confirme a pasta no desktop. Se o item entrar pausado na fila, selecione-o e clique em **Resume**.

O navegador pode já ter iniciado sua própria cópia: este fluxo manual não cancela nem substitui automaticamente esse download.

Regressão executável: `node browser/check-media-bridge.cjs`. Inclui isolamento por aba, permissão de cookies, envio do popup, rejeição de URL não detectada e recuperação após suspensão do worker.

## Interceptação automática: extensão 2.2.0

- Implementada com `chrome.downloads.onCreated`: pausa a cópia HTTP(S), encaminha ao aplicativo e cancela a cópia do navegador somente após `accepted-for-confirmation`.
- Se a ponte falhar ou o desktop rejeitar o pedido, retoma a cópia do navegador. A confirmação no desktop continua necessária; cancelar essa confirmação não restaura automaticamente a cópia já cancelada.
- Respeita a opção automática, o controle global e o controle do site informado pelo referrer (ou pelo domínio do arquivo quando não há referrer). Ignora downloads de outras extensões, incógnitos e URLs `blob:`/`data:`. Downloads concluídos antes da pausa permanecem no navegador. Requisições que dependem de corpo POST ou cabeçalhos especiais não foram validadas.
- A confirmação passa a selecionar **Start now** por padrão, tanto com o desktop aberto quanto ao iniciá-lo pela ponte.
- Helium real, perfil temporário, arquivo autenticado de **4.194.304 bytes**: clique no link, cancelamento da cópia do navegador (`USER_CANCELED`), confirmação WPF e arquivo final com SHA-256 idêntico à fixture. Passou com desktop inicialmente fechado e já aberto, em 2026-11-06 às 14:02 UTC.
- SHA-256 da fixture: `469169f01110bb3f23c42932a46f7f6214df68202a256af8019978fc57552436`.
- No perfil pessoal: extensão **2.2.0**, opção automática marcada e ponte conectada foram observadas. Ao repetir o teste externo, a página principal `https://nuvem.anitsu.moe/` respondeu **502 Bad Gateway — nginx/1.18.0**. Portanto, a interceptação automática no Anitsu ainda não foi comprovada.

### Reteste pelo portal: aprovado, 14:14 UTC

A conclusão de bloqueio acima foi superada pelo teste seguindo o caminho indicado pelo usuário: **`https://anitsu.moe/` → BAIXAR AGORA → Animes → Letra Y → Youjo Senki → [2026] Youjo Senki II → [Anitsu] → download do episódio 08**. O acervo abriu normalmente após o clique no portal; o 502 da navegação direta não demonstrava falha desse fluxo.

- Clique no botão de download do site abriu automaticamente **Add a download** no FDM, sem usar o popup da extensão.
- **Start now** já selecionado; destino separado para preservar o arquivo anterior.
- Job `2b241d83b6cc48098ddfc216374ea76b`, concluído em `2026-11-06T14:14:28.0368949+00:00`, sem erro.
- Arquivo: `C:\Users\Flugel\Downloads\FDM-anitsu-auto-20261106-111413\[Anitsu] Youjo Senki S2 - 08 [WEB DUAL 1080p HEVC].mkv`.
- **346.372.662 bytes**, assinatura Matroska `1A-45-DF-A3`, SHA-256 `cc93c13f4cc4b1a5589e8855c27b1cc61d12fab691deda7de81aa454fc8a6652`, idêntico ao arquivo obtido anteriormente pelo popup.
- Histórico recente do Helium inspecionado após o teste: nova cópia do episódio 08 com estado **Cancelado**. A cópia antiga do navegador continua preservada; não foi removido histórico nem arquivo do usuário.
- Nenhuma credencial registrada na evidência. Reprodução integral continua não verificada.

### Contexto de request e redirect: fixture local, 14:35 UTC

Extensão **2.2.1**; teste real no Helium em perfil temporário, com host registrado e confirmação WPF. `FDM_INTERCEPT_TEST=1 FDM_REQUEST_CONTEXT_TEST=1` em `live-auth-check.cjs`:

- Link normal responde 302; o FDM recebe a URL final, sem repetir o endpoint inicial de redirect.
- Servidor exige cookie de fixture e User-Agent específico. Browser e desktop passaram nas duas condições, com permissão por site explícita.
- Transferência automática de **4.194.304 bytes**, SHA-256 `469169f01110bb3f23c42932a46f7f6214df68202a256af8019978fc57552436`; cópia do navegador `USER_CANCELED`.
- Formulário POST para o mesmo arquivo permanece no navegador e conclui com o mesmo hash. **Isso valida fallback, não suporte POST no FDM.**
- Evidência sanitizada: `%LOCALAPPDATA%\Temp\opencode\auth-live-iOwA43\evidence.json`. O primeiro teste de POST selecionava prematuramente o item GET anterior; corrigido para aguardar um novo ID antes de avaliar o resultado.
- Motor: **52/52** checks, incluindo retirada de Cookie/Authorization/Referer em redirect entre origens e preservação na mesma origem. Ponte nativa: **18/18**; smoke WPF: **19/19** a 125%, sem alegação de matriz de resoluções completa.
- Regressão simulada agora cobre primeira gravação após reinício do worker, métodos desconhecidos/ambíguos e rejeição de User-Agent injetado. Suspensão real do worker continua pendente.
- O cancelamento da cópia do navegador ainda ocorre no aceite **para confirmação**, não no aceite durável do job. Auditoria e pendências em `analysis/browser-parity-audit.md`; paridade IDM não declarada.
- Repetição com binários finais e desktop inicialmente fechado: `%LOCALAPPDATA%\Temp\opencode\auth-live-7iurxD\evidence.json`, 14:41 UTC; redirect/cookie/User-Agent, hash e fallback POST aprovados novamente. Perfil pessoal não recarregado nesta rodada; atualização permanece nos artifacts e nos perfis temporários testados.

Teste reproduzível em PowerShell, com host previamente registrado para o ID da extensão:

```powershell
$env:FDM_INTERCEPT_TEST = '1'
try {
    node browser/live-auth-check.cjs "$env:LOCALAPPDATA\imput\Helium\Application\chrome.exe"
} finally {
    Remove-Item Env:FDM_INTERCEPT_TEST
}
```
