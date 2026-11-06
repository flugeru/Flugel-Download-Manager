# Flugel Download Manager browser bridge

Original, optional Chrome/Edge Manifest V3 extension and .NET 10 native host. No external packages, proprietary assets, compatibility claims, cookie access, request interception, automatic download cancellation, or credential forwarding. Only a user-selected HTTP(S) link is submitted. URLs can contain private query parameters: they are passed to desktop but never included in host replies or extension status text. Windows process arguments can be inspected by software running as the same user; do not submit secret-bearing URLs if that exposure is unacceptable.

**Not live browser-tested.** Host framing/parser checks and mocked extension checks do not establish browser installation, service-worker lifecycle behavior, desktop confirmation, or actual downloads. See `RESULTS.md` for executed checks.

## Build and publish (no registration)

Run from repository root using .NET 10 SDK. Keep the two published folders as siblings:

```powershell
dotnet build app/Flugel.NativeHost/Flugel.NativeHost.csproj -c Release
dotnet run --project app/Flugel.NativeHost/Flugel.NativeHost.csproj -c Release -- --self-test
node browser/checks.cjs app/Flugel.NativeHost/bin/Release/net10.0/Flugel.NativeHost.exe
dotnet publish app/Flugel.NativeHost/Flugel.NativeHost.csproj -c Release -r win-x64 --self-contained false -o publish/Flugel.NativeHost
dotnet publish app/Flugel.Desktop/Flugel.Desktop.csproj -c Release -r win-x64 --self-contained false -o publish/Flugel.Desktop
```

```text
publish/
  Flugel.NativeHost/Flugel.NativeHost.exe
  Flugel.Desktop/FlugelDownloadManager.exe
```

Desktop executable naming and `--url URL` confirmation handling belong to desktop implementation. Host uses only this fixed sibling path; it never searches PATH, invokes a shell, or accepts an executable path from browser messages. `ProcessStartInfo.ArgumentList` passes URL as one argument with `UseShellExecute=false`. On Windows, host clears `HANDLE_FLAG_INHERIT` on original stdin/stdout/stderr handles before process creation; redirection alone does not prevent original pipe handles being inherited. Child standard streams are also redirected/closed so GUI cannot corrupt native messaging stdout. Host does not wait for desktop exit or verify that a confirmation dialog appeared.

Real-desktop regression (Windows PowerShell, Node installed, both published folders present):

```powershell
.\browser\Check-DesktopLaunch.ps1 -HostPath .\artifacts\Flugel.NativeHost\Flugel.NativeHost.exe -DataDirectory C:\YourFreshTestDirectory
```

Directory must not exist. Check uses explicit host `--self-test-desktop <directory>` mode, which runs same framed parser and real desktop launch with added `--data-dir <directory>`. This mode accepts no executable override. It sends valid local URL but never confirms download; checks host reply/EOF independently of real desktop lifetime, then uses UIA to cancel confirmation and close only matching isolated desktop PID. Test data retained. Do not use default profile for this check. Browser invocation does not enable this mode.

Official release is framework-dependent: host requires .NET 10 runtime and desktop requires .NET 10 Windows Desktop Runtime. To package, include both complete published folders, `browser/extension`, both registration PowerShell scripts and this guide. Publish into fresh directories; publishing framework-dependent output over old self-contained output does not remove stale runtime files. Optional self-contained builds require explicit `--self-contained true` into a different fresh directory and all emitted runtime/support files. No proprietary assets required. Do not bundle installed browser profiles, credentials, or generated registration manifests.

## Explicit opt-in setup

These steps are manual; builds and checks do not perform them. Registration modifies only current user's exact host leaf keys. No elevation is needed. Scripts support `-WhatIf` and prompt for confirmation.

1. Review `browser/extension` and scripts. Publish both applications into the sibling layout above, preferably under a stable location writable only by your account/administrators. Replacing host binaries changes what the browser executes.
2. If you choose to install, open `chrome://extensions` or `edge://extensions`, enable developer mode, and select **Load unpacked** for `browser/extension`. This is your explicit browser setting change; no script does it.
3. Copy the actual extension ID shown by that browser. It must be exactly 32 lowercase letters `a` through `p`. Do not use a sample or wildcard origin. IDs can differ across browsers or after moving the unpacked folder.
4. Preview registration, replacing the ID and host path. Then repeat without `-WhatIf` and confirm if you want registration:

   ```powershell
   .\browser\Register-NativeHost.ps1 -Browser Chrome -ExtensionId '<actual-32-letter-id>' -HostPath 'C:\Your Publish Folder\Flugel.NativeHost\Flugel.NativeHost.exe' -WhatIf
   ```

   For Edge, use `-Browser Edge` and Edge's actual ID. Register browsers separately. Do not bypass local script policy without reviewing your organization's security rules.
5. Right-click an HTTP(S) link and choose **Send link to Flugel Download Manager for confirmation**. Pin extension action if you want badge visibility. `ASK` and action tooltip mean host accepted for desktop confirmation only. `ERR` means invalid input, host/desktop unavailable, or rejected request. Review and confirm URL/destination in desktop before downloading.

Registration writes JSON via `ConvertTo-Json`, not string substitution. Manifest location is `%LOCALAPPDATA%\Flugel Download Manager\NativeMessaging\<Browser>-<ExtensionId>.json`. `allowed_origins` contains exactly `chrome-extension://<ExtensionId>/`; Edge Chromium uses the same origin scheme. The default value under both 32/64-bit HKCU registry views points to that manifest:

- Chrome: `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.flugel.downloadmanager`
- Edge: `HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.flugel.downloadmanager`

Existing different registration or manifest is rejected, not overwritten. A script or registry error may leave a partial registration; inspect the reported error and use the matching unregister command before retrying. No HKLM writes, extension installation, browser policy changes, or broad registry deletion.

## Explicit removal

```powershell
.\browser\Unregister-NativeHost.ps1 -Browser Chrome -ExtensionId '<actual-32-letter-id>' -WhatIf
# Repeat without -WhatIf and confirm to remove that registration.
```

Use `-Browser Edge` separately if registered. Script checks both views, matching manifest path and exact allowed origin; only removes `com.flugel.downloadmanager` leaf keys without extra values/children. Parent keys, binaries, extension installation, and manifest file remain. Remove extension manually through browser UI if desired. Retained matching manifest may be deleted manually after checking no registration refers to it. If manifest is missing/corrupt or registration has extra data, removal fails safely; inspect exact leaf key rather than deleting parent keys.

## Protocol

Host name: `com.flugel.downloadmanager`. Four-byte unsigned **little-endian** byte length, followed by strictly decoded UTF-8 JSON. Windows x64 uses little-endian native byte order. This implementation deliberately restricts requests to **1–65,536 bytes**, tighter than Chromium's own incoming limit.

```json
{"version":1,"action":"enqueue","url":"https://example.test/file.zip"}
```

Exactly these three fields; duplicates, unknown fields, wrong types, wrong version/action, malformed JSON/UTF-8, zero/oversized length, partial header/body, nonabsolute/non-HTTP(S) URLs, embedded userinfo, whitespace/control characters and backslashes are rejected. A clean EOF between frames exits normally; framing/request errors send a small framed rejection and close without resynchronizing. Valid requests can be processed one frame at a time until EOF. No credentials accepted; no logging of URL/query.

Success response:

```json
{"version":1,"status":"accepted-for-confirmation"}
```

Rejection response has `version:1`, `status:"rejected"`, and one of `error:"invalid-frame"`, `"invalid-request"`, or `"desktop-unavailable"`. Success means process creation only; does not claim queued transfer, visible dialog, confirmed destination, or download completion. Stdout carries framed replies only; explicit `--self-test` mode prints test results and is not a native messaging session.

## Public references

Current generic Chrome documentation consulted through Context7 (`/websites/developer_chrome_extensions`):

- Native messaging, host manifest, permissions, framing and Windows registration: https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging
- Context menus and worker-compatible `onClicked`: https://developer.chrome.com/docs/extensions/reference/api/contextMenus
- Service worker install lifecycle: https://developer.chrome.com/docs/extensions/develop/concepts/service-workers/lifecycle
- Microsoft Edge native messaging registration: https://learn.microsoft.com/en-us/microsoft-edge/extensions-chromium/developer-guide/native-messaging

Extension permissions are only `contextMenus` and `nativeMessaging`; no host permissions, content scripts, cookies, downloads, tabs, storage or webRequest permission.
