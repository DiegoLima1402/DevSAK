# Publicação e atualização do DevSAK

## Preparação no GitHub

O repositório `DiegoLima1402/DevSAK` precisa permanecer público para a consulta
anônima. Não é necessário criar tokens, secrets ou servidor. Não inclua tokens no
aplicativo.

As cópias anteriores à 1.3.3 não têm atualizador. Seus usuários devem instalar
manualmente uma versão 1.3.3 ou posterior para passar a detectar atualizações.
Para o ensaio de atualização para 1.3.4, mantenha uma cópia instalada na 1.3.3.

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

### Versão 1.3.4

As notas prontas para o aplicativo e para o GitHub estão em
`DevSAK/Resources/ReleaseNotes/1.3.4.md`. O instalador esperado é
`Installer/output/DevSAK_Installer_v1.3.4.exe`. Gere-o com o código desta versão;
não reutilize um instalador anterior renomeado.

Antes do commit, execute os testes de ZIP e upload descritos em
[ZIP_UPLOAD.md](ZIP_UPLOAD.md), além dos testes do atualizador. Revise o diff
e inclua os novos arquivos de código, testes e documentação. A pasta `scratch/`
contém arquivos de trabalho e não faz parte da release; instaladores e saídas
de build também não devem entrar no commit.

A versão 1.3.4 inclui também o padrão de cards e progresso descrito em
[INTERFACE.md](INTERFACE.md). Refaça o publish e o instalador depois dos últimos
ajustes visuais; uma saída antiga da pasta publish não inclui essas alterações.

Após gerar o instalador, faça o commit e push na branch `master`:

```powershell
git add DevSAK Installer README.md docs tests
git diff --cached --stat
git commit -m "feat: prepara release 1.3.4 com uploads ZIP e interface padronizada" -m "Adiciona perfis ZIP, StorageTo, File.Kiwi, destinos globais FTP/FTPS, reenvio e exclusão após upload. Padroniza cards e barras de progresso; atualiza instalador, documentação, testes e notas da versão."
git push origin master
```

Somente depois de o commit estar no GitHub, crie a tag e a release. Pelo
GitHub CLI autenticado (`gh`), na raiz do repositório:

```powershell
git tag -a v1.3.4 -m "DevSAK 1.3.4"
git push origin v1.3.4
gh release create v1.3.4 ./Installer/output/DevSAK_Installer_v1.3.4.exe --repo DiegoLima1402/DevSAK --verify-tag --title "DevSAK 1.3.4" --notes-file ./DevSAK/Resources/ReleaseNotes/1.3.4.md --latest
```

Sem `gh`, crie a mesma tag e use a interface Releases do GitHub, conforme o
procedimento abaixo. A publicação depende do commit correto e do instalador
correspondente; preparar os arquivos locais não publica uma release.

1. Envie o código aprovado para o GitHub.
2. Abra **Releases > Draft a new release** e selecione/crie a tag `vX.Y.Z`, por
   exemplo `v1.3.4`, no commit correspondente.
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

### Testar uma instalação 1.3.3 com a release 1.3.4

1. Use a cópia x64 instalada pelo Inno Setup no usuário Windows atual,
   confirmando que a versão exibida é 1.3.3. Não use o executável de bin/publish
   nem instale a 1.3.4 por cima antes de testar a detecção.
2. Após o commit/push, publique `v1.3.4` como release estável/Latest com o asset
   `DevSAK_Installer_v1.3.4.exe`. Confira tamanho e digest pela API acima.
   Criar apenas a tag, fazer push ou salvar um rascunho não publica a atualização.
3. Na cópia instalada 1.3.3, clique em Verificar atualizações. O aplicativo
   também consulta ao abrir e a cada quatro horas; a ação manual evita esperar.
4. Abra Ver atualização e autorize Baixar atualização. O próprio DevSAK baixa
   o instalador, mostra o progresso e confere tamanho/SHA-256. O navegador não
   é aberto. O instalador não faz esse download do pacote principal.
5. Conclua as operações e clique em Instalar e reiniciar. O DevSAK salva as
   configurações, executa o instalador e encerra. Setup aguarda a liberação das
   instâncias, atualiza no diretório instalado e reabre o aplicativo.
6. Confirme a versão 1.3.4, as notas da versão e a preservação de configurações,
   perfis e destinos FTP. Se falhar, consulte o alerta e
   `%LocalAppData%\DevSAK\Updates\<id>\install.log` quando o instalador tiver iniciado.

Uma cópia 1.3.4 não detecta outra release 1.3.4. Para um ensaio posterior com
versão instalada 1.3.4, publique uma versão superior. Não renomeie instaladores
antigos nem substitua silenciosamente os assets de uma release distribuída.

Em uma VM ou usuário de teste, instale a versão com atualizador e disponibilize uma
Release estável superior. Verifique: aviso, consentimento, download e cancelamento;
instalação recusada durante ZIP/SMTP/certificados/MySQL; preservação de tema,
perfis e configurações; encerramento e reabertura com a nova versão; falha de rede;
outra instância aberta e arquivo adulterado. Esse ensaio exige uma Release real
superior e não é substituído pelos testes automatizados offline.
