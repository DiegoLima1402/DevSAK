# Compactador ZIP e uploads — 1.3.4

## Perfis e geração do ZIP

O compactador usa perfis de exclusão para arquivos, pastas e extensões. Regras
anteriores são migradas para GERAL. A seleção e as regras são persistidas;
importação/exportação JSON compartilha apenas o perfil, sem caminhos locais.
O nome automático é calculado ao iniciar, e a interface adapta os editores e
a execução à largura da janela.

Os títulos e ícones ficam dentro dos cards, seguindo MySQL/SMTP. Os cards de
configurações do pacote, filtro por data e execução se alinham pelo topo;
a área de regras não reserva mais uma linha vazia acima do filtro. A barra
de progresso usa o estilo global de 8 px. Veja [INTERFACE.md](INTERFACE.md).

## Destinos

| Opção | Configuração | Resultado |
| --- | --- | --- |
| Não fazer upload | Origem, destino e regras locais | ZIP local |
| StorageTo | Chave opcional em Configurações | Link compartilhável |
| File.Kiwi | Sem chave de API | Link com chave de descriptografia após `#` |
| FTP | Destino global e pasta remota | Caminho remoto confirmado |

O upload inicia após a compactação, reinicia o progresso e mostra
“Enviando arquivo...”. Nos serviços de compartilhamento, Copiar Link copia o
link completo somente após a confirmação. StorageTo suporta PUT simples e
multipart; a chave é validada antes do envio, sem fallback anônimo silencioso
quando inválida. File.Kiwi usa a API v2 com registros RFC 8188/AES-128-GCM,
criptografia local do nome e conteúdo, partes sequenciais e confirmação final.

Referências: [StorageTo](https://storage.to/pt/docs/api),
[File.Kiwi](https://file.kiwi/api).

## Cadastro global FTP/FTPS

O seletor aparece ao escolher FTP. O botão + adiciona um destino; o menu ⋯
edita ou exclui o selecionado. Cada registro guarda descrição, host, porta,
usuário, senha protegida, tipo de conexão, modo de dados e pasta remota.
Quando a descrição estiver vazia, o seletor mostra o host. Editar preserva o
identificador; excluir remove somente a configuração, sem apagar arquivos remotos.

Conectar valida host, usuário, porta e tipo de conexão antes de acessar a rede.
A senha pode ficar vazia quando o servidor permitir. O navegador de pastas só
aparece depois de conectar com sucesso; permite abrir subpastas e subir um nível.
Alterar a conexão invalida a listagem e exige reconectar. É possível salvar a
configuração sem conexão, preservando a pasta configurada (raiz por padrão).

FTP, FTPS explícito (normalmente porta 21) e FTPS implícito (normalmente 990)
são suportados. Certificados FTPS seguem a validação do sistema. SFTP/SSH não
faz parte desta opção. No modo passivo, o cliente inicia as conexões de dados,
o que costuma facilitar o uso com NAT e firewall; o tooltip explica o comportamento.
O botão de senha alterna mostrar/ocultar sem precisar manter o clique pressionado.

Uploads usam a pasta do destino selecionado e pedem confirmação antes de
substituir arquivos remotos. Erros exibem contexto, tipo de exceção, respostas
FTP como 530/550 e causas internas, com senha omitida. Falhas podem deixar
arquivo parcial no servidor; o ZIP local é preservado.

## Falhas, reenvio e exclusão

O diálogo de falha oferece Tentar Novamente (mesmo ZIP e destino), Selecionar
Outro Destino ou Fechar. A troca de destino mostra somente os cards de upload
e execução e muda o botão para Enviar Arquivo. É possível escolher outro
serviço ou editar/cadastrar destinos FTP. O reenvio não refaz a compactação.
Cancelar operação descarta a tentativa pendente, preserva o ZIP e restaura a tela.
O diálogo de falha tem largura ampliada para acomodar o texto completo de
Selecionar Outro Destino, limitada ao espaço disponível na janela.

Excluir o ZIP local após o upload bem-sucedido vem desmarcado por padrão e
só aparece quando há um serviço selecionado. O arquivo só é removido depois
da confirmação de sucesso. Falhas/cancelamentos não removem o ZIP. Uma falha
na exclusão local é informada separadamente, sem repetir um upload já concluído.

Com essa opção marcada, o modal de upload concluído omite Abrir Pasta. Links
StorageTo/File.Kiwi mantêm Copiar Link e Fechar. Para FTP, sem link público,
o modal mostra o caminho remoto e oferece Fechar quando a exclusão após upload está marcada.

## Persistência e serviços reutilizáveis

Os arquivos ficam em `%AppData%\DevSAK`:

| Arquivo | Conteúdo |
| --- | --- |
| AppZipSettings.json | Pacote, perfis, serviço, ID FTP selecionado e exclusão opcional |
| ftp-destinations.json | Cadastro global FTP/FTPS; senha cifrada por DPAPI |
| storageto.key | Chave StorageTo cifrada por DPAPI |
| storageto.visitor | Token persistente para envios anônimos |

FtpDestinationStore e FtpTransferService podem ser usados por outras ferramentas.
O compactador referencia o ID do destino, sem duplicar credenciais. Senhas e
chaves protegidas pertencem ao usuário Windows atual. Se um destino salvo
não existir mais, outro servidor não será escolhido silenciosamente.

## Validação

Execute na raiz do repositório:

```powershell
dotnet run --project tests/DevSAK.AppZipTests/DevSAK.AppZipTests.csproj
dotnet run --project tests/DevSAK.StorageToTests/DevSAK.StorageToTests.csproj
dotnet run --project tests/DevSAK.FileKiwiTests/DevSAK.FileKiwiTests.csproj
dotnet run --project tests/DevSAK.FtpTests/DevSAK.FtpTests.csproj
```

Os testes de APIs HTTP usam respostas simuladas. A criptografia inclui vetor
oficial da RFC 8188 e descriptografia do conteúdo. FTP usa conexões reais em
loopback, com cadastro/DPAPI, navegação, upload, colisões, autenticação, permissões,
perda de conexão e cancelamento. Não é necessário fornecer credenciais reais.
O ensaio visual e uploads em servidores externos devem ser conferidos antes
de distribuir o instalador.
