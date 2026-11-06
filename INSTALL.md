# Instalar o Flugel Download Manager (Windows)

Estado: instalador **sem assinatura digital** (Windows pode avisar "editor desconhecido"). Só instale se você gerou o arquivo ou confere o SHA-256 em `artifacts/installer/SHA256SUMS.txt`.

## 1. Gerar o instalador (no seu PC)
Pré-requisitos: .NET 10 SDK e Inno Setup 6.

```powershell
.\build-installer.ps1 -Version 0.2.0
```
Saída: `artifacts\installer\FlugelDownloadManager-Setup-0.2.0.exe`.

## 2. Instalar
Execute o `.exe` (pede elevação) ou, silenciosamente:
```powershell
.\FlugelDownloadManager-Setup-0.2.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```
Os arquivos ficam em `C:\Program Files\Flugel Download Manager\`. Downloads, histórico e configurações ficam em `%LOCALAPPDATA%\Flugel Download Manager\`.

## 3. Ligar ao navegador (opcional, explícito)
1. No Chrome, Edge ou Helium, abra `chrome://extensions` (ou `edge://extensions`), ative **Modo do desenvolvedor** e clique em **Carregar sem compactação**, escolhendo `C:\Program Files\Flugel Download Manager\extension`.
2. Copie o **ID da extensão** exibido no popup dela.
3. Registre a ponte nativa para esse ID (só altera o registro do usuário atual, HKCU):
```powershell
& "C:\Program Files\Flugel Download Manager\tools\Register-NativeHost.ps1" -ExtensionId <ID> -Browser Chrome -HostPath "C:\Program Files\Flugel Download Manager\Flugel.NativeHost\Flugel.NativeHost.exe"
```
   Use `-Browser Edge` para o Edge. Helium usa o registro do Chrome.
4. No popup, marque **Enviar cookies desta sessão…** apenas para os sites em que você quer baixar arquivos com login.

## 4. Desinstalar
Pelo "Aplicativos instalados" do Windows, ou `unins000.exe` na pasta de instalação. Remova o registro da ponte com:
```powershell
& "C:\Program Files\Flugel Download Manager\tools\Unregister-NativeHost.ps1" -ExtensionId <ID> -Browser Chrome
```
Os dados em `%LOCALAPPDATA%\Flugel Download Manager\` são mantidos de propósito.

## Limitações conhecidas
- Cookies de sessão ficam só na memória do download: após reiniciar o app, um download protegido precisa ser reenviado pelo navegador.
- Mídia com DRM não é suportada e não é contornada.
- O instalador não assina nem instala navegadores ou extensões automaticamente.
