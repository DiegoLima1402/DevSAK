# Convenções da interface — 1.3.4

## Cards das ferramentas

MySQL Backup / Restore e Testar SMTP são as referências visuais. O mesmo
padrão é aplicado ao Compactador ZIP e às abas Exportar PEM/Validar Certificado:

- Título dentro do card, no início do conteúdo, em 18 px e SemiBold.
- Ícone de 18 px da fonte Segoe Fluent Icons, na cor ApplicationAccentBrush,
  à esquerda do título; espaçamento horizontal de 10 px.
- Fundo ApplicationTileBrush, borda ApplicationBorderBrush de 1 px,
  cantos arredondados de 12 px e padding de 24 px.
- Cards de Console de Saída/Log exibem o título dentro do card sem ícone.
- Campos, botões e indicadores usam os recursos de tema claro/escuro existentes.

No ZIP, os cinco cards são Configurações do pacote, Opções de Upload, Filtro
por data, Regras de exclusão e Execução. O filtro começa na primeira linha
da coluna de regras, sem espaço vazio acima, alinhado aos cards das pontas.
O resumo do pacote é uma seção interna de Execução, sem card separado.

Em certificados, o padrão cobre Entrada, Saída, Execução e Detalhes do
Certificado, mantendo o indicador de validade junto ao cabeçalho dos detalhes.
O card de Log mantém o título sem ícone.

## Progresso

App.xaml define o estilo implícito de ProgressBar para toda a aplicação:
Height e MinHeight de 8 px, CornerRadius de 4 px e recurso
ProgressBarTrackHeight de 8 px. ZIP, certificados, MySQL, SMTP e atualização
usam esse padrão. Preserve cores, valores, estados indeterminados e bindings
de cada ferramenta; evite alturas locais que sobrescrevam a espessura comum.

## Diálogos e adaptação

O diálogo de falha de upload acomoda Tentar Novamente, Selecionar Outro
Destino e Fechar, com largura mínima desejada de 680 px e máxima de 720 px,
limitando a mínima ao espaço da janela menos 48 px.

Na edição de FTP, o conteúdo reserva 24 px à direita para que a rolagem não
cubra os campos. A listagem de pastas fica oculta até a conexão funcionar e
é invalidada ao alterar os dados da conexão. O botão de senha alterna entre
mostrar e ocultar com um clique; o modo passivo tem tooltip explicativo.

O modo de troca de destino após falha de upload mostra somente os cards de
upload e execução. Cancelar restaura a composição normal da ferramenta.

## Verificação visual antes da release

Confira temas claro e escuro, janela ampla e reduzida, títulos dentro dos
cards, alinhamento superior do filtro por data, botões dos diálogos sem corte,
rolagem do editor FTP e barras de progresso determinadas/indeterminadas.
