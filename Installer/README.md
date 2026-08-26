# Pasta Installer

Esta pasta reúne os arquivos de apoio do instalador do DevSAK (Inno Setup). Ela é independente do código da aplicação, dos assets de runtime e das ferramentas embarcadas.

O script Inno Setup atual é `Installer_Script.iss`. Ele empacota o conteúdo da pasta de publicação `win-x64\publish` diretamente em `{app}`, sem criar uma subpasta `publish`.

## Conteúdo

| Arquivo / pasta | Uso no instalador |
| --- | --- |
| `LICENSE.txt` | Página de licença exibida durante a instalação |
| `INFO_BEFORE.txt` | Informações exibidas **antes** da instalação |
| `INFO_AFTER.txt` | Informações exibidas **depois** da instalação |
| `Assets/` | Ícones e imagens **exclusivos do instalador** (wizard, ícone de setup). Inclui `setup_icon.ico` |
| `Installer_Script.iss` | Script do Inno Setup |

Textos visíveis ao usuário (`LICENSE.txt`, `INFO_BEFORE.txt` e `INFO_AFTER.txt`) estão em português (PT-BR).

## O que não deve ser colocado aqui

- Arquivos de runtime da aplicação (executável, DLLs, `.xaml`, temas, ViewModels etc.).
- Cópia manual de `DevSAK/Assets` (ícones, splash e logos usados pelo app).
- Cópia de `DevSAK/Tools/MySql` ou `DevSAK/Tools/7Zip`, salvo se o fluxo final do instalador exigir explicitamente essa duplicação.
- Código-fonte, documentação geral do produto ou capturas de tela do app.

Esses conteúdos devem continuar nos respectivos locais:

```text
DevSAK/Assets/      # assets de runtime / MSIX
DevSAK/Tools/       # ferramentas embarcadas (MySQL e 7-Zip)
DevSAK/Resources/   # recursos da aplicação (notas de versão)
docs/               # documentação e screenshots do repositório
```

## Empacotamento

O instalador deverá empacotar os arquivos gerados pelo **Release/publish** da aplicação, e não uma cópia manual desta pasta.

Fluxo previsto:

1. Publicar o projeto WinUI (por exemplo, `dotnet publish` em Release, plataforma x64).
2. Apontar o script Inno Setup para a pasta de saída dessa publicação.
3. Incluir `LICENSE.txt`, `INFO_BEFORE.txt` e `INFO_AFTER.txt` desta pasta nas páginas correspondentes do assistente.
4. Usar `Installer/Assets/` apenas para gráficos do wizard, quando existirem.

As ferramentas em `Tools/MySql` e `Tools/7Zip` já são copiadas para a saída do build pelo `.csproj`. O instalador deve aproveitar essa saída, sem duplicá-las aqui.

## Licenças de terceiros a preservar no empacotamento

Não altere nem substitua arquivos de licença de terceiros. O `LICENSE.txt` do DevSAK apenas informa que esses componentes seguem as próprias licenças.

### Já presentes no repositório (preservar)

| Origem | Arquivo | Observação |
| --- | --- | --- |
| `DevSAK/Tools/7Zip/License.txt` | Licença do 7-Zip (LGPL e avisos relacionados) | A própria licença exige que redistribuições em forma binária reproduzam as informações de licença. **Inclua este arquivo** junto com `7z.exe` e `7z.dll` no pacote instalado. |

### Não encontrados no repositório

| Origem | Situação |
| --- | --- |
| `DevSAK/Tools/MySql/` | Não há `LICENSE`, `NOTICE` nem arquivo equivalente nesta pasta. Se licenças oficiais dos utilitários MySQL forem adicionadas depois, elas também devem ser preservadas no empacotamento. |

### Dependências NuGet (não copiar para esta pasta)

As licenças das bibliotecas ficam nos pacotes NuGet restaurados, não no código do DevSAK. Não é necessário duplicá-las em `Installer/`.

Referência das dependências diretas atuais do projeto:

- **MailKit / MimeKit** — usadas para SMTP; o MailKit puxa **BouncyCastle** de forma transitiva.
- **MySqlConnector**
- **CommunityToolkit.WinUI.Controls.Segmented**
- **Microsoft.WindowsAppSDK** e **Microsoft.Windows.SDK.BuildTools**
- **System.Security.Cryptography.ProtectedData**

O instalador não precisa copiar os textos completos dessas licenças para `Installer/`, a menos que uma obrigação específica de redistribuição seja identificada no fluxo final de publicação. O `LICENSE.txt` do DevSAK já reserva os direitos dos respectivos titulares.

## Script Inno Setup

O script atual é `Installer_Script.iss`. Ele empacota o **conteúdo** da pasta `win-x64\publish` em `{app}` (`%LocalAppData%\Programs\DevSAK`), sem criar uma subpasta `publish`.

O payload de produção deve ser gerado com o perfil `DevSAK/Properties/PublishProfiles/win-x64.pubxml` (`SelfContained` + `WindowsAppSDKSelfContained`), para que a instalação não dependa do SDK do .NET nem de um runtime instalado à parte.

Configuração atual:

- instalação **apenas para o usuário atual** (`PrivilegesRequired=lowest`)
- `LicenseFile` → `Installer\LICENSE.txt`
- `InfoBeforeFile` → `Installer\INFO_BEFORE.txt`
- `InfoAfterFile` → `Installer\INFO_AFTER.txt`
- `SetupIconFile` → `Installer\Assets\setup_icon.ico`
- origem dos arquivos instalados → conteúdo de `DevSAK\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish`
- exclusão de `*.pdb`, `*.appxrecipe` e `createdump.exe`
- pastas de idioma extras do Windows App SDK removidas no publish (mantém `pt-BR` e inglês de fallback)
- WebView2 Runtime instalado sob demanda via `Installer\Redist\MicrosoftEdgeWebview2Setup.exe`
- saída `DevSAK_Installer_v{versão}.exe`
- `AppVersion` visível como versão semântica (`1.3.1`), lida automaticamente de `DevSAK.exe`
- `AppId` estável para upgrades no mesmo diretório
- arquivos antigos em `{app}` são substituídos na atualização; dados em AppData não são apagados
- instâncias em execução de `DevSAK.exe` são encerradas antes de substituir arquivos

Compile o script somente depois de publicar o Release x64 / win-x64 com o perfil acima. A saída do instalador fica em `Installer\output\`.
