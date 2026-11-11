# Restauração MySQL

O restore prepara cópias temporárias UTF-8 antes de executar o SQL. Os arquivos originais são preservados. Por padrão ignora USE e CREATE/DROP DATABASE (incluindo SCHEMA); isso não isola o script nem altera referências explícitas a outros bancos.

Analisar backup lista os SQL de arquivos compactados e conta comandos relevantes. É possível selecionar e ordenar os arquivos. Alterar a origem limpa a seleção. Configurações ficam nas preferências; seleção de arquivos vale para a sessão. A análise não é uma simulação de execução nem uma validação de compatibilidade com o servidor.

Opções avançadas: remover DEFINER, ignorar GTID_PURGED e bloqueios, remapear identificadores qualificados da origem exata, escolher UTF-8/Latin-1/UTF-16 LE, continuar após erros e salvar relatório em AppData/DevSAK. O remapeamento também pode atingir aliases com o nome de origem; revise esses scripts. DEFINER removido altera o definidor dos objetos. Modos SQL NO_BACKSLASH_ESCAPES exigem revisão manual e são rejeitados pelo preparador.

Chaves estrangeiras: quando a opção está ativa, comandos SET do arquivo que referenciem FOREIGN_KEY_CHECKS são omitidos para manter o controle da sessão no DevSAK. Reativar a verificação não valida retroativamente dados importados.

Continuar após erros usa --force; erros reportados pelo cliente resultam em Concluído com erros. Cancelar não desfaz alterações já executadas. Cada arquivo usa uma nova sessão.

Testes: dotnet run --project tests/DevSAK.MySqlTests. Os testes cobrem filtros, literais, comentários executáveis, rotinas com DELIMITER, FK e remapeamento, além dos argumentos de backup. Não substituem um restore real em banco de teste.
