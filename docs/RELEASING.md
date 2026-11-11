# Publicação e atualização do DevSAK

## Preparação no GitHub

O repositório `DiegoLima1402/DevSAK` precisa permanecer público para a consulta
anônima. Não é necessário criar tokens, secrets ou servidor. Não inclua tokens no
aplicativo. Atualmente não existe uma Release estável publicada.

As cópias anteriores à 1.3.3 não têm atualizador. Seus usuários devem instalar a
1.3.3 manualmente uma vez. Essa versão passará a detectar as próximas versões.

## Gerar o instalador

1. Atualize `Version`, `AssemblyVersion`, `FileVersion` e `InformationalVersion` em
   `DevSAK/DevSAK.csproj`, além de `DevSAK/app.manifest` e
   `DevSAK/Package.appxmanifest`. Atualize a documentação e crie as notas em
   `DevSAK/Resources/ReleaseNotes/X.Y.Z.md`, preservando as notas históricas.
2. Execute os testes do atualizador:

   ```powershell
   dotnet run --project .\tests\DevSAK.UpdateTests\DevSAK.UpdateTests.csproj
   ```

3. Publique o aplicativo:

   ```powershell
   dotnet publish .\DevSAK\DevSAK.csproj -c Release -p:Platform=x64 -p:PublishProfile=win-x64
   ```

4. Compile `Installer/Installer_Script.iss` com Inno Setup 7. O instalador lê a
   versão diretamente do executável publicado. O arquivo final será
   `Installer/output/DevSAK_Installer_vX.Y.Z.exe`.
5. Confira a versão do arquivo final e teste a instalação em um usuário de teste.

## Publicar a Release

1. Envie o código aprovado para o GitHub.
2. Abra **Releases > Draft a new release** e selecione/crie a tag `vX.Y.Z`, por
   exemplo `v1.3.3`, no commit correspondente.
3. Cole as notas da versão e anexe **DevSAK_Installer_vX.Y.Z.exe**. O nome precisa
   corresponder exatamente à versão da tag. Não use apenas os arquivos automáticos
   de código-fonte do GitHub.
4. Aguarde o upload terminar e publique a Release como estável, marcada como
   **Latest**. Rascunhos e pré-lançamentos não são oferecidos pelo atualizador.
5. Confira se a API retorna o instalador, tamanho e `digest` no formato
   `sha256:<64 caracteres hexadecimais>`:

   ```powershell
   $release = Invoke-RestMethod 'https://api.github.com/repos/DiegoLima1402/DevSAK/releases/latest'
   $release.tag_name
   $release.assets | Select-Object name, size, digest
   ```

Se o digest não estiver disponível, envie novamente o arquivo. O aplicativo
recusa instaladores sem SHA-256 válido. Para corrigir uma versão já distribuída,
publique outra versão superior; não substitua silenciosamente uma Release antiga.

## Comportamento do atualizador

- Consulta `/repos/DiegoLima1402/DevSAK/releases/latest` sem autenticação e com
  timeout de 30 segundos. Sem Releases, o GitHub responde 404 e não há atualização.
- Aceita tags `vX.Y.Z` ou `X.Y.Z`, comparadas numericamente. Não faz downgrade.
- Exige o instalador no próprio repositório, via HTTPS, com nome e SHA-256 válidos.
- Baixa somente após autorização, em `%LocalAppData%\DevSAK\Updates\<id>`.
  Downloads parciais ou inválidos são removidos. Há limite de 30 minutos por
  download. O instalador completo é baixado a cada versão; não há atualização delta.
- Mostra uma notificação automática por versão a cada sessão. O botão manual
  permite reabrir o aviso depois de dispensá-lo.
- A instalação exige a cópia x64 registrada pelo Inno Setup no usuário atual,
  e todas as operações rastreadas precisam estar concluídas, inclusive nas páginas
  das quais o usuário já saiu.
- Antes de executar, confere novamente tamanho, SHA-256 e versão do instalador,
  salva as preferências da ferramenta atual e inicia Setup com
  `/SILENT /SP- /NORESTART /NOCLOSEAPPLICATIONS /DEVSAKUPDATE=1` e o diretório atual.
- Setup aguarda até 30 segundos para todas as instâncias liberarem o mutex
  `Local\DevSAK.Running`. Não encerra à força operações de outras instâncias.
  A atualização reabre o aplicativo depois da instalação.
- Erros do instalador continuam visíveis; seu log fica em `install.log`, junto ao
  instalador baixado. O aplicativo permanece instalado no mesmo diretório e os
  dados em AppData são preservados.

SHA-256 verifica integridade usando os metadados recebidos por HTTPS do GitHub.
Isso não substitui uma assinatura Authenticode do publicador. A assinatura digital
do instalador pode ser incorporada ao processo de publicação quando houver um
certificado de assinatura; ela não é exigida por esta implementação.

## Validação de ponta a ponta antes de distribuir

Em uma VM ou usuário de teste, instale a versão com atualizador e disponibilize uma
Release estável superior. Verifique: aviso, consentimento, download e cancelamento;
instalação recusada durante ZIP/SMTP/certificados/MySQL; preservação de tema,
perfis e configurações; encerramento e reabertura com a nova versão; falha de rede;
outra instância aberta e arquivo adulterado. Esse ensaio exige uma Release real
superior e não é substituído pelos testes automatizados offline.
