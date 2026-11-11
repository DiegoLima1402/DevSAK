# Opções de backup MySQL

O padrão não inclui CREATE DATABASE e USE. Marque a opção de banco original para recuperar o comportamento anterior. Referências explícitas ao schema em views e rotinas e cláusulas DEFINER não são reescritas.

Conteúdo: estrutura e dados, somente estrutura ou somente dados das tabelas. Rotinas, eventos e triggers seguem suas opções independentes. DROP TABLE não é emitido no modo somente dados; desativá-lo não resolve conflitos com tabelas existentes.

Todas as tabelas e views são incluídas por padrão. Carregar a lista permite excluir objetos. Trocar banco ou conexão limpa a seleção; recarregar seleciona todos. Rotinas e eventos não são filtrados por tabela. A seleção de tabelas vale para a sessão; as demais opções são salvas nas preferências.

Opções avançadas: bloqueios no restore, nomes de colunas nos INSERTs, INSERTs agrupados, tablespaces e GTID. Charset, fuso UTC, hex-blob e snapshot transacional são preservados. A consistência do snapshot depende de tabelas transacionais e ausência de alterações de DDL durante o dump.

O fallback incompleto para mysqlpump foi removido; mysqldump é obrigatório.

Validação: dotnet run --project tests/DevSAK.MySqlTests verifica 36 combinações e persistência, usando --help do executável embarcado sem conectar ao servidor. Compilar: dotnet build DevSAK.slnx -p:Platform=x64.
