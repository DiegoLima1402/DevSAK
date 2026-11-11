# DevSAK

![Windows](https://img.shields.io/badge/Windows-Desktop-0078D4?style=flat-square&logo=windows)
![WinUI 3](https://img.shields.io/badge/WinUI%203-Windows%20App%20SDK-C81D25?style=flat-square)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)
![C#](https://img.shields.io/badge/C%23-Desktop-239120?style=flat-square&logo=csharp)

**DevSAK** (*Developer Swiss Army Knife*) é uma suíte desktop para Windows com utilitários do dia a dia de desenvolvimento: compactação ZIP, certificados, backup/restauração MySQL e teste SMTP.

Construído em **C#**, **.NET 8** e **WinUI 3**, com tema claro/escuro, interface em Mica e ferramentas CLI embarcadas. Versão atual do projeto: **1.3.3**.

---

## Ferramentas

| Ferramenta | O que faz |
| --- | --- |
| **Compactador (ZIP)** | Gera `.zip` a partir de um diretório, com nome manual ou timestamp, filtro por data, listas de ignore e progresso com cancelamento. |
| **Ferramentas de certificado** | Exporta PEM (`.pfx`, `.p12`, `.cer`, `.crt`) e valida certificados (Subject, Issuer, validade, thumbprint, SAN/DNS, etc.). |
| **MySQL Backup / Restore** | Perfis de conexão, backup configurável com `mysqldump`, restore de `.sql`/arquivos compactados, opção de compactar o backup em ZIP e criação de banco quando aplicável. |
| **Testar SMTP** | Envia mensagem de teste via MailKit (SSL/STARTTLS, autenticação) e exibe o resultado no console. |

---

## Interface

Dashboard com cards, ícones Fluent e alternância de tema. O tema escolhido é gravado em `%AppData%\DevSAK` e restaurado na próxima execução.

| Tema claro | Tema escuro |
| --- | --- |
| ![Dashboard em tema claro](docs/screenshots/home-light.png) | ![Dashboard em tema escuro](docs/screenshots/home-dark.png) |

| Compactador ZIP | Certificados |
| --- | --- |
| ![Compactador ZIP](docs/screenshots/tool_zip.png) | ![Ferramentas de certificado](docs/screenshots/too_certificate.png) |

| MySQL Backup / Restore | Teste SMTP |
| --- | --- |
| ![MySQL Backup / Restore](docs/screenshots/tool_mysql_restore_backup.png) | ![Teste SMTP](docs/screenshots/tool_smtp_test.png) |

Após atualizar para uma versão nova, o DevSAK pode exibir as **notas de versão** uma vez (conteúdo em `Resources/ReleaseNotes`). A última versão exibida fica salva localmente.

---

## Tecnologias

Stack efetivamente usada no projeto:

- **C# / .NET 8** (`net8.0-windows10.0.19041.0`), Windows 10 1809 ou posterior
- **WinUI 3** e **Windows App SDK** (`Microsoft.WindowsAppSDK`)
- **XAML** (páginas, temas e janela principal)
- **CommunityToolkit.WinUI.Controls.Segmented**
- **MailKit / MimeKit** (SMTP) e **BouncyCastle** (exportação PEM)
- **MySqlConnector** (operações auxiliares MySQL)
- **System.IO.Compression** (ZIP nativo)
- **System.Text.Json** e **System.Security.Cryptography.ProtectedData** (configurações e senhas no escopo do usuário Windows)
- **Process** para executar `mysqldump` / `mysql` / `7z` embarcados

---

## Estrutura

```text
DevSAK.slnx
README.md
docs/                      # Logos e screenshots da documentação
Installer/                 # Script e arquivos de apoio do Inno Setup
DevSAK/
├─ Assets/                 # Ícone, logos e imagens do app
├─ Converters/
├─ Models/
├─ Resources/ReleaseNotes/ # Notas de versão empacotadas com o app
├─ Services/
├─ Themes/
├─ Tools/
│  ├─ 7Zip/                # 7z.exe, 7z.dll, License.txt
│  └─ MySql/               # Cliente MySQL (mysql, mysqldump, mysqlpump, …)
├─ ViewModels/
├─ Views/
├─ Properties/PublishProfiles/   # Perfil de publicação win-x64
├─ App.xaml
├─ MainWindow.xaml
└─ DevSAK.csproj
```

`Tools` faz parte do runtime: os binários são copiados para a saída do build e entram no `publish` de produção.

---

## Desenvolvimento (Visual Studio)

Pré-requisitos:

- Windows 10 ou 11 (x64 recomendado)
- Visual Studio 2022 com suporte a desenvolvimento Windows / WinUI 3 / Windows App SDK
- SDK do .NET 8

1. Clone o repositório.
2. Abra `DevSAK.slnx`.
3. Restaure os pacotes NuGet (o Visual Studio faz isso ao abrir a solução).
4. Selecione a plataforma **x64** e a configuração **Debug**.
5. Use o perfil **DevSAK (Unpackaged)** para executar o projeto diretamente (há também **DevSAK (Package)** para o fluxo MSIX de desenvolvimento).
6. Compile e inicie a depuração.

Pela linha de comando:

```powershell
dotnet build .\DevSAK.slnx -p:Platform=x64
dotnet run --project .\DevSAK\DevSAK.csproj -p:Platform=x64
```

---

## Produção (self-contained)

A distribuição de produção é **self-contained** e **unpackaged**:

- Configuração: **Release**
- Arquitetura: **x64**
- Runtime: **win-x64**
- `SelfContained=true`
- `WindowsAppSDKSelfContained=true`

O payload final fica em:

```text
DevSAK/bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/publish/
```

Essa pasta `publish` é o que o instalador empacota. O diretório `publish` em si **não** deve aparecer como subpasta na instalação (`…\DevSAK\DevSAK.exe`, não `…\DevSAK\publish\…`).

Self-contained, neste projeto, significa que o `publish` inclui o runtime do **.NET 8** e os arquivos do **Windows App SDK**. O computador de destino **não precisa do SDK do .NET**. O instalador ainda pode instalar o **WebView2 Runtime** (dependência do Windows App SDK) se ele ainda não estiver no Windows.

### Publicar pelo Visual Studio

1. Abra `DevSAK.slnx`.
2. Selecione **Release** e **x64**.
3. Clique com o botão direito no projeto **DevSAK** → **Publish**.
4. Use o perfil **win-x64** (`Properties/PublishProfiles/win-x64.pubxml`).
5. Publique. Confira a pasta `publish` indicada acima.

### Publicar pela linha de comando

Use o perfil do projeto (ele aplica self-contained, Windows App SDK embarcado e a limpeza de pastas de idioma extras):

```powershell
dotnet publish .\DevSAK\DevSAK.csproj -c Release -p:Platform=x64 -p:PublishProfile=win-x64
```

---

## Instalador

O diretório `Installer/` contém o script Inno Setup e os arquivos de apoio (`LICENSE.txt`, `INFO_BEFORE.txt`, `INFO_AFTER.txt`, ícone, bootstrapper do WebView2).

- O setup instala o **conteúdo** de `publish` para o usuário atual (`%LocalAppData%\Programs\DevSAK`).
- A saída gerada (`Installer/output/`, por exemplo `DevSAK_Installer_v1.3.3.exe`) não deve ser commitada (está no `.gitignore`).
- Detalhes para quem mantém o instalador: `Installer/README.md`.

---

## Atualizações automáticas

A partir da versão 1.3.3, o aplicativo consulta as Releases públicas do GitHub ao
abrir e a cada quatro horas enquanto estiver aberto. O botão **Verificar
atualizações** permite consultar manualmente. O aviso mostra versão, tamanho e
notas da Release; o usuário autoriza o download, acompanha o progresso e pode
cancelá-lo. Depois, **Instalar e reiniciar** aplica a atualização quando todas as
ferramentas estiverem ociosas, preservando as configurações em AppData.

O atualizador aceita apenas Releases estáveis com um instalador oficial x64 e
SHA-256 válido, conferido após o download e novamente antes da execução. A
instalação automática funciona na cópia instalada por usuário com Inno Setup;
builds de desenvolvimento, versões portáteis, x86, ARM64 e MSIX precisam de
atualização manual. Falhas nas consultas automáticas não interrompem o uso.

Para publicar uma versão, siga [docs/RELEASING.md](docs/RELEASING.md).

## Configurações locais

Gravadas em `%AppData%\DevSAK` (fora da pasta de instalação):

| Arquivo | Uso |
| --- | --- |
| `appsettings.json` | Tema e última versão das notas de release |
| `AppZipSettings.json` | Compactador ZIP |
| `SmtpTestSettings.json` | Teste SMTP |
| `MySqlConnections.json` | Perfis MySQL |
| `MySqlBackupRestoreSettings.json` | Preferências de backup/restore |

Senhas das ferramentas usam DPAPI (`ProtectedData`) no escopo do usuário Windows.

---

## Componentes de terceiros

O DevSAK distribui utilitários de terceiros em `DevSAK/Tools`. Eles permanecem sob as respectivas licenças:

- **MySQL** — cliente CLI em `Tools/MySql`
- **7-Zip** — `Tools/7Zip` (`License.txt` incluso); usado na extração de compactados que não são `.zip` nativo

A licença do DevSAK está em `Installer/LICENSE.txt`. Bibliotecas NuGet (MailKit, BouncyCastle, Windows App SDK, etc.) seguem os termos de cada pacote.

---

## Licença

Uso restrito conforme `Installer/LICENSE.txt`. Componentes de terceiros não são relicenciados pelo DevSAK.
