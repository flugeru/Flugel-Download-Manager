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

O hash identifica o arquivo local; o servidor não forneceu hash para comparação. Não foi validada a reprodução integral do vídeo. A interceptação automática ainda não está implementada.

## Como usar
1. Recarregue a extensão em `chrome://extensions` após atualizar seus arquivos. Recarregue a página do site.
2. No popup, ative o envio de cookies somente para o site desejado.
3. Clique no arquivo no site. Abra o popup e escolha **Baixar no Flugel** em **Arquivos detectados nesta página**.
4. Confirme a pasta no desktop. Se o item entrar pausado na fila, selecione-o e clique em **Resume**.

O navegador pode já ter iniciado sua própria cópia: este fluxo manual não cancela nem substitui automaticamente esse download.

Regressão executável: `node browser/check-media-bridge.cjs`. Inclui isolamento por aba, permissão de cookies, envio do popup, rejeição de URL não detectada e recuperação após suspensão do worker.
