# Roteiro de QA manual

O validador automatizado compila o mod, executa a suíte de regressão e verifica
os arquivos JSON, mas não substitui um teste dentro do Stardew Valley. Execute
este roteiro antes de publicar uma nova versão.

## Save e ciclo de vida

- Abrir um save antigo com companions em Following, Waiting e trabalhando.
- Confirmar que nenhuma activity `working`/target fantasma sobrevive sem tarefa.
- Salvar durante uma tarefa, voltar ao título e carregar novamente.
- Reduzir os slots de inventário no GMCM e confirmar que o excedente vai para o
  inventário do grupo/overflow, sem sumir.
- Carregar sem um content pack que criou item e NPC; salvar, reativar o pack e
  confirmar que item, owner, XP, skills, diretivas e posição reaparecem.
- Testar item colorido/flavored, salvar/recarregar e confirmar cor/parent.
- Simular schema futuro ou member duplicado/inválido e confirmar que o mod fica
  inerte, não altera mundo/inventários e não sobrescreve o dado original.
- Forçar erro de item customizado no meio de withdraw/recompensa e confirmar que
  cada pilha existe uma única vez, com o remainder preservado ou solto no mundo.
- Desconectar durante evento com dismiss automático, salvar/fechar antes do fim
  do evento e confirmar no reload que o dismiss continua commitado e a agenda é
  restaurada assim que o estado do jogo ficar seguro.
- Atualizar um save schema 1–7 com companion ativo e confirmar que um NPC com
  velocidade customizada recupera `speed` e `addedSpeed` ao ser dispensado.
- Carregar com um NPC customizado ausente, disponibilizá-lo mais tarde na mesma
  sessão, dispensá-lo e confirmar captura/restauração da velocidade antes do
  primeiro controller do mod.
- Atualizar um save schema 8 para 9 com uma ordem de área ativa e histórico de
  diálogo. Centro/mapa/raio/especialidade e até quatro chaves recentes por NPC
  devem sobreviver; path, target, reserva, preview, fala enfileirada e animação
  não podem reaparecer como runtime antigo.
- Atualizar um save schema 9 para 10 e confirmar que a lista cosmética nasce
  vazia sem alterar companions, áreas, itens ou histórico já persistidos.
- Em um save schema 10, subir XP, comprar skills e registrar loot recente;
  atualizar para schema 13, dispensar, salvar/recarregar e recrutar o mesmo NPC.
  Nível, XP, pontos, skills e histórico devem voltar, mas owner, cargo, modo e
  área antiga não podem ser herdados pelo novo membro.
- Dispensar um NPC, recrutá-lo com outro farmer e depois voltar ao primeiro.
  A progressão permanente deve ser a mesma para o NPC, enquanto ferramentas,
  rotina e baú continuam isolados por owner/NPC.
- Atualizar um save schema 12 para 13 e confirmar que perfis operacionais e
  logística nascem sem alterar perfil permanente, cargo, áreas ou owner. Uma
  vara antiga no cargo deve migrar para o slot Fishing Rod; duplicatas devem ir
  para recovery/overflow, nunca permanecer misturadas ao loot.
- Atualizar um save schema 13 para 14 contendo ordens e presets circulares de
  rotina. Mesmo sem `RegionKind` no JSON antigo, mapa, centro e raio devem
  continuar circulares até substituição explícita. Uma especialidade sem preset
  deve continuar ausente e seu bloco pausado; a migração nunca pode criá-la como
  Área livre. Salvar/recarregar novamente deve conservar os dois estados.
- Atualizar schema 14 para 15 e confirmar que cada perfil owner/NPC recebe os
  defaults compatíveis `Guardar madeira=true`, `Guardar minérios=true` e
  `Manter comida=false`. Alterar cada filtro, salvar/recarregar,
  dispensar/recrutar com o mesmo owner e confirmar persistência; outro owner
  deve manter regras independentes.
- Repetir a migração com o content pack de uma ferramenta legado ausente e
  restaurá-lo depois: a ferramenta deve permanecer em recovery e voltar apenas
  ao inventário do owner original, nunca ao squad ou a outro farmer.
- Salvar no meio de uma área ativa, recarregar e confirmar que o NPC volta ao
  mapa/centro salvo e replaneja apenas recursos ainda existentes. Um payload de
  área inválido deve ser limpo sem apagar o companion ou seu inventário.

## Follow, mapas e recall

- Testar Behind, Compact e Adaptive caminhando e parado.
- Passar por portas, escadas, pontes estreitas e mudanças de mapa.
- Separar o companion por uma região desconectada do mesmo mapa e aguardar as
  tentativas de recovery; confirmar estado `stuck` sem teleporte. Em seguida,
  usar Recall e confirmar fallback para um tile alcançável e seguro.
- Usar Wait, sair do mapa, iniciar novo dia, retornar e usar Resume.
- Usar Recall durante uma tarefa e enquanto o companion está preso.
- Manter autonomia/diretivas ativas durante Recall e trocar o owner de mapa; o
  companion não pode aceitar nova tarefa nem encerrar fora do raio de 1,5 tile.
- Separar owner/companion por uma cerca com distância de 2 tiles; Recall deve
  recuperar em vez de oscilar na borda.
- Dispensar o NPC depois de várias trocas de mapa e conferir sua agenda vanilla.
- Recrutar e dispensar um cônjuge no pátio de sábado; antes de 21:30 ele deve
  voltar ao pátio com animação, e à noite deve voltar corretamente à cama.
- Dormir com um cônjuge/pet recrutado e confirmar que, ao iniciar o dia, a rotina
  vanilla é capturada antes de follow reassumir, sem warp ou controller residual.
- Recrutar um pet dormindo, entrar em outro mapa e empurrá-lo; ele deve continuar
  com sprite 32x32 e não alternar para Sleep nem criar trajetória fora do follow.
- Recrutar, comandar e dispensar gato, cachorro e tartaruga; eles devem funcionar
  como companions sem exibir balões de fala no recrutamento, em tarefas, no idle
  ou ao serem dispensados.
- Com 1, 3 e 12 companions, caminhar por um mapa grande e verificar no profiler
  que a manutenção normal não repete limpeza de rota, reflexão ou BFS completa
  a cada cinco ticks.
- Com 1, 3 e 12 companions em mapa grande, pressionar repetidamente
  `X > Seguir jogador`
  em NPCs próximos e distantes; a roda deve confirmar sem pico na frame e a rota
  deve começar em um tick posterior.

## Tarefas

- Para Water, Harvest, Forage e Pet: confirmar que o mundo só muda depois de o
  companion alcançar uma posição adjacente e válida.
- Trocar de mapa, ferramenta ou target enquanto Lumber/Mine está a caminho.
- Proteger uma flor próxima a Bee House e confirmar o motivo mostrado.
- Ativar dois companions com o mesmo foco e confirmar que não escolhem o mesmo
  target nem o mesmo tile de trabalho.
- Abrir um menu por mais de 15 segundos durante uma tarefa; ela não deve expirar
  só pelo período pausado.
- Usar conteúdo customizado que rejeita uma ação e confirmar que os outros
  companions continuam processando.
- Testar Manual, Mimicking e Autonomous separadamente.
- Confirmar que tarefas automáticas não inundam o HUD com mensagens repetidas.
- Em mapa grande e carregado, usar `X > Trabalhar` com raio 20 e 1, 3 e 12
  companions. O clique, o planejamento seguinte e o trajeto não podem congelar;
  novas rotas devem surgir em pequenos lotes sem impedir os outros trabalhadores.
- Repetir Trabalhar por 30 segundos sem alvo válido e com o alvo mais próximo
  bloqueado/reservado. Não pode haver pico periódico de BFS nem starvation; um
  próximo alvo alcançável deve ser escolhido quando existir.
- Alternar Wood/Mining/Clear Area no painel e no HUD enquanto o NPC trabalha.
  Preview, cancelamento, Follow/Wait e troca de mapa não podem ressuscitar uma
  tarefa antiga ou criar path no frame do clique.
- Sem baú atribuído e com inventário individual vazio, coletar forage e destruir
  árvore/pedra com um companion. Os itens coletáveis devem aparecer no cargo
  daquele NPC; debris
  antigo e fragmentos cosméticos devem continuar no chão. Conferir separadamente
  os drops do tronco ao terminar a animação de queda e os drops do toco; repetir
  com mogno, mushroom/mystic tree e `ChopItems` customizados. Com o inventário
  individual cheio, o remainder deve ir ao inventário do grupo quando habilitado,
  ao player quando desabilitado e ao mundo somente quando o destino seguinte
  também estiver cheio, sem duplicação ou perda. Arqueologia e itens essenciais
  devem conservar a coleta/side effects vanilla.

## Rotina por horário

- Abrir `F9 > Rotina` em viewport normal/baixa, UI scale 100%/150% e controle.
  Confirmar seis abas, vinte células 06h–01h, oito atividades, toggles de
  ativação/repetição, conclusão, Área livre, Área delimitada, atalho 06–18 e
  salvamento alcançáveis. Com Follow, Wait, Rotina original ou Depositar
  selecionado, os dois controles de área devem ficar inativos e explicar que é
  preciso selecionar Regar, Madeira, Minerar ou Limpar.
- Partindo de `Rotina inativa`, pintar uma célula e repetir com `Turno 06–18`.
  Cada edição deve mudar o draft para `Rotina ativa`; salvar e fechar o painel
  deve aplicar imediatamente o bloco da hora atual.
- Para o mesmo NPC, escolher Área livre em Regar/Minerar e quadrados diferentes
  em Madeira/Limpar. Alternar a atividade selecionada deve mostrar o escopo
  correspondente sem copiar a escolha entre especialidades.
- Salvar uma rotina cujo bloco atual seja Minerar, fechar/reabrir o painel e
  clicar diretamente em Área livre, sem selecionar Minerar outra vez. O controle
  deve indicar `Área livre · Minerar`, salvar o preset de mineração e iniciar o
  bloco. Trocar para outro NPC com Regar no horário atual deve mudar o foco para
  Regar, sem herdar a seleção de mineração do primeiro NPC.
- Abrir Área delimitada na Fazenda, Floresta e dentro de um shed. A tela deve
  fazer fade no mapa atual, ocultar HUD/farmer e mostrar o quadrado sem custo,
  catálogo, colocação de construção ou warp real do farmer. Mover a câmera
  com borda do mouse/teclas de movimento e mover o cursor com D-pad/analógico;
  roda, `+`/`−` e LB/RB devem alterar o lado de um tile por vez.
- No seletor, confirmar por clique no mapa/botão, Enter/Espaço e `A`. Deve voltar
  ao mesmo painel, companion, aba, atividade e draft, restaurando location,
  tile/facing, viewport, HUD e farmer. Reabrir antes de salvar deve começar no
  quadrado confirmado e manter as demais edições do draft. Cancelar por botão,
  clique direito, Escape/menu e `B`/Back deve fazer a mesma restauração sem
  alterar o preset nem perder outras células não salvas; só Salvar rotina deve
  persistir a escolha.
- Testar lados 3 e 41, insistindo em `−`/`+` nos limites, e posicionar o cursor
  nos quatro cantos. O quadrado deve permanecer inteiro no mapa. Preparar alvos
  em `(minX,minY)` e `(minX+size-1,minY+size-1)`, que devem entrar, e imediatamente
  antes/depois de cada borda, que devem ficar fora; apenas o stand adjacente do
  NPC pode usar o padding de um tile. Repetir numa fazenda customizada, inclusive
  quando sua menor dimensão limitar o máximo abaixo de 41.
- Com Área livre, colocar alvos compatíveis perto dos quatro extremos e bem além
  de raio 20. Todos devem ser elegíveis no mapa onde o escopo foi salvo; alvos
  em outra location devem ficar fora. Repetir separadamente na fazenda, estufa,
  shed, Floresta e Fazenda da Ilha.
- Salvar Regar na Fazenda e Madeira na Floresta em blocos consecutivos, deixando
  owner em Town. Na troca de horário o host deve remover a área anterior,
  transferir o NPC para um tile seguro dentro da nova região e consumir pelo
  menos dois alvos sem farmer observando o mapa. Entrar depois na Floresta deve
  mostrar NPC, recursos, loot e XP coerentes, sem snap tardio ou repetição.
- Repetir com uma árvore começando a cair imediatamente antes da troca de bloco.
  Mesmo após o cancelamento da tarefa antiga, a queda e os drops devem terminar
  no mapa vazio; nenhuma outra árvore ou terrain feature deve ser simulada.
- No mesmo cenário remoto, substituir o bloco por área manual, tarefa contextual
  e `Mover e esperar`. Essas ordens não podem receber a conclusão off-screen
  exclusiva de `routine-*`; devem permanecer pausadas até o mapa voltar a ser
  observado.
- Agendar trabalho sem preset, com área vazia, task toggle desligado e modo da
  especialidade Disabled. Sem preset deve aguardar sem buscar por toda a fazenda;
  escolher explicitamente Área livre ou confirmar Área delimitada ainda no mesmo
  bloco deve iniciá-lo. Área sem targets aplica a conclusão uma vez; toggle/modo
  desligado deve pausar e identificar `tasks disabled`, não ferramenta ausente,
  e retomar quando reativado, sem loop de warp.
- Carregar um preset circular de schema 13 e também criar uma área pela roda
  manual. Ambos devem continuar círculos: tile exatamente no raio entra e a
  diagonal do quadrado envolvente fica fora. Abrir/salvar a rotina sem trocar o
  escopo não pode convertê-los; escolher Área livre ou confirmar Área delimitada
  substitui apenas a especialidade selecionada. Uma nova área manual não pode
  sobrescrever depois essa escolha explícita.
- Num preset circular legado com raio 20, reduzir `CompanionWorkRadius` para 3 e
  deixar o próximo bloco/reload recriá-lo. A área ativa deve usar raio 3;
  aumentar a configuração não deve fazê-la crescer sozinha. Área livre e Área
  delimitada não devem ser reduzidas por essa opção de raio.
- Enquanto um bloco sem preset aguarda, iniciar quick work, tarefa contextual e
  uma área manual de outra especialidade. O refresh horário não pode apagá-los;
  ao terminar o override, a rotina deve voltar ao retry, ou assumir no próximo
  bloco. Uma área manual da mesma especialidade deve satisfazer o bloco uma vez.
- Criar um bloco contínuo de trabalho por várias horas, concluí-lo cedo, salvar
  e recarregar ainda dentro do bloco. A área não pode ser recriada a cada dez
  minutos nem no reload; o próximo bloco distinto deve executar normalmente.
- Testar conclusão Follow, Wait e Rotina original. No último caso, confirmar
  agenda/rota/comportamento vanilla e ausência de disputa com o controller do
  mod; no bloco seguinte, Follow/Wait/trabalho deve readquirir controle limpo.
- Durante um bloco Follow e após conclusão Follow, deixar alvos disponíveis para
  todos os modos autônomos configurados. O NPC deve continuar seguindo; uma
  diretiva manual explícita ainda pode substituí-lo até terminar ou a próxima
  troca de bloco começar.
- Desativar uma rotina ativa enquanto ela trabalha ou está em Rotina original:
  a área/agenda anterior deve terminar pelo comportamento de conclusão, sem NPC
  preso. Com Repetir diariamente desligado, dormir e confirmar que a rotina é
  desabilitada no próximo dia.
- No host e farmhand, abrir o mesmo NPC, editar grades divergentes e salvar em
  ordem invertida. Só o primeiro token CAS deve ser aceito; o segundo recebe
  conflito e não pode apagar presets, revisão ou conclusão do host.
- Repetir o CAS fazendo o farmhand escolher Área livre e Área delimitada. O host
  deve aceitar apenas o preset ainda atual, revalidar que a location persistente
  ainda existe e que a geometria de `FarmWide`, círculo ou quadrado cabe no mapa
  escolhido (inclusive interiores), e replicar o mesmo escopo/limites aos demais
  clientes. Desconectar/reconectar e salvar/recarregar no host deve preservar a
  escolha por especialidade.
- Agendar Depositar com baú vazio, parcial, cheio, bloqueado e ausente. O bloco
  deve repetir enquanto houver cargo depositável e concluir sem drop quando
  tudo couber. Slots de ferramenta são ignorados; ferramenta legada deve ter
  sido migrada para equipamento/recovery antes e não pode prender o bloco.

## Baús de destino

- Abrir um baú normal colocado no mundo e confirmar o painel lateral em
  pergaminho vanilla com `Não usar este baú`, `Padrão do grupo` e cada companion
  do jogador. A seleção explícita deve usar verde/check; companions herdando o
  default devem mostrar `Padrão` em dourado, e overrides para outro destino,
  `Outro baú` em azul/cinza, sem alterar a semântica do clique individual.
  Quando não houver 236 px livres à
  esquerda ou à direita do `ItemGrabMenu` (incluindo viewport 426x240), confirmar
  o overlay compacto com no máximo dois companions por página e Ant./Próx.
  exibidos somente quando houver mais de uma página e limitados corretamente na
  primeira/última; o indicador `atual/total` deve permanecer numericamente
  legível, e a última página deve manter a mesma altura das demais. O fallback
  compacto também deve entrar
  quando a altura faria as linhas laterais ficarem menores que 24 px. Testar 1/3/12 companions,
  viewport baixa e UI scale 150%: painel e botões devem ficar dentro da viewport;
  cliques em botões, navegação, indicador ou área vazia do overlay nunca podem
  fechar/interagir com os slots vanilla abaixo. O painel é mouse-only nesta
  versão e não deve roubar D-pad/A do menu vanilla.
- Marcar `Padrão do grupo` depois que alguns companions apontam para outros baús: os
  overrides antigos devem ser limpos e todos devem usar o novo default. Depois,
  marcar um companion em outro baú e confirmar que somente esse override novo
  precede o default; `Não usar este baú` deve remover todas as referências ao
  baú aberto.
- Confirmar que geladeira, presente, Junimo/global, special chest e baú que não
  esteja colocado em `location.Objects` não exibem nem aceitam a atribuição.
- Atribuir um baú, movê-lo para outro tile e depois para um interior de celeiro/
  galpão. Loot e depósito da rotina devem reencontrá-lo pelo GUID e atualizar a
  localização conhecida. Remover o `modData`, apagar o baú ou duplicar o mesmo
  GUID em dois baús deve falhar fechado e continuar pelo fallback atual, sem
  escolher arbitrariamente um deles.
- Repetir como farmhand e atrasar/reordenar o comando após trocar de mapa,
  mover/substituir o baú ou trocar ownership do companion. O host deve rejeitar
  owner/mapa/tile/GUID obsoleto sem modificar a logística de outro jogador.
- Em um baú remoto ainda sem GUID, substituir o objeto entre a primeira fase e
  o ACK. A fase 1 só pode criar/confirmar identidade; a atribuição deve esperar
  o GUID aparecer no mesmo objeto ainda aberto. ACK inválido/erro deve cancelar
  a pendência sem gerar um segundo timeout ou vincular o substituto.
- Enquanto essa confirmação estiver pendente, o cabeçalho deve exibir
  `Confirmando este baú...`, destacar a escolha em dourado e bloquear novos
  cliques de atribuição; paginação ainda pode funcionar e os slots vanilla não
  podem receber o clique por baixo.
- Gerar loot com o baú vazio, quase cheio, cheio e bloqueado por outro menu.
  O baú deve ser tentado antes do inventário do NPC; qualquer remainder deve
  seguir companion, squad/player e mundo exatamente uma vez. Forçar exceção de
  item customizado após merge parcial e confirmar que a quantidade total antes/
  depois é idêntica, sem duplicação nem perda.
- Encher o cargo do companion e executar o bloco de rotina Depositar. Itens que
  couberem devem sair do cargo; remainder, item customizado indisponível e
  qualquer item sem destino devem permanecer para novo retry. Nenhum dos quatro
  slots de ferramenta pode entrar no baú, no cargo ou em Retirar tudo.
- Depois de um depósito parcial e completo, forçar falha de `WriteSaveData` e
  recarregar. O CargoJournal salvo com o baú vanilla deve manter exatamente o
  remainder no companion, sem ressuscitar no cargo o que já entrou no baú.
- Repetir o corte de save dispensando um companion com cargo (destino squad),
  retirando um item do squad para o farmer e recrutando/coletando com um NPC que
  não existe no payload anterior. Cargo, squad e recovery devem somar a mesma
  quantidade; entrada sem member deve ir a recovery em vez de desaparecer.

## Equipamento operacional

- Em `F9 > Inventário`, confirmar os quatro slots rotulados: Machado, Picareta,
  Regador e Vara de pesca. Com a ferramenta correspondente selecionada na barra,
  clicar equipa; outra correspondente troca diretamente e devolve a anterior à
  mesma célula; uma célula vazia retira. Tipo incompatível, encantamento e vara
  com isca/apetrecho devem falhar sem mover nenhum item.
- Equipar upgrades diferentes de machado/picareta e confirmar que o efeito
  base usa exatamente o nível equipado, não a ferramenta atual do farmer; o
  bônus explícito de skill do companion é aplicado depois desse nível base.
  Sem o slot correto, tarefa manual, diretiva, autonomia e rotina devem parar
  com motivo legível e sem criar ferramenta virtual.
- Equipar um regador parcialmente cheio, regar até zero e confirmar a redução
  persistente a cada tile. Com smart refill desativado, vazio deve interromper
  novas tarefas; retirar para uma célula vazia, encher em água e reequipar deve
  retomar com o valor correto.
- Dispensar/recrutar com o mesmo owner e confirmar os quatro slots preservados.
  Recrutar o mesmo NPC com outro farmer deve mostrar slots independentes; voltar
  ao primeiro owner deve restaurar somente o perfil dele.
- Repetir equipar/trocar/retirar como farmhand e atrasar dois pedidos. O host
  deve validar owner, índice, fingerprint da célula e fingerprint do slot
  anterior, recusando estado obsoleto sem perda ou duplicação.
- Depois de uma troca e depois de consumir água, forçar falha em `WriteSaveData`
  sem impedir o save vanilla. Ao recarregar, toolbar e quatro slots devem ser
  reconciliados pelo checkpoint de `Farmer.modData`, sem perda ou duplicação.
- Simular também o primeiro commit schema 13 sem key de EquipmentJournal: a
  toolbar vanilla é a autoridade e todos os slots daquele owner devem carregar
  vazios, em vez de duplicar uma ferramenta presente só no payload adiantado.

## Inventário direto e filtros

- Abrir `F9 > Inventário` ao lado do baú atribuído e testar as seis direções
  jogador↔companion, jogador↔baú e companion↔baú. No mouse, arrastar e soltar
  sobre o painel de destino; no controle, focar a pilha, apertar A, escolher o
  painel e apertar A novamente. B/Escape deve cancelar sem mover nada.
- Trocar de aba ou de companion durante o drag e confirmar cancelamento
  imediato. Alterar a pilha fonte antes do commit, substituir/mover o baú ou
  mudar seu GUID/mapa/tile deve produzir pedido stale sem tocar na pilha nova.
- Encher parcialmente o destino, transferir uma pilha maior e confirmar que só
  o espaço disponível é commitado e o remainder volta ao índice original.
  Repetir com destino totalmente cheio e confirmar soma constante.
- Transferir ferramenta encantada, vara com acessórios e item de mod entre
  jogador e baú; a mesma instância e todo estado custom devem sobreviver.
  Tentar enviá-los ao cargo deve ser recusado. Objeto stackable comum, colorido,
  preservado e com `modData` deve entrar no cargo e sobreviver a save/reload.
- Marcar/desmarcar Guardar madeira, Guardar minérios e Manter comida. Depósito
  automático, rotina e comando em lote devem respeitar os três; drag manual
  deve continuar permitido. Cobrir madeira/lei, pedra/carvão/minério/barra,
  gema/mineral e alimento positivo ou negativo.
- Em `426×240` e `512×288`, confirmar que os painéis sempre aparecem. Rolar cada
  painel com wheel/Page Up/Page Down/triggers até alcançar o primeiro e o último
  slot; o intervalo do título, foco, hitbox e índice transferido devem coincidir.
- Com dois farmhands, segurar o mesmo baú aberto, iniciar drags e dispensar o
  companion enquanto um pedido aguarda mutex. O host deve serializar os leases,
  revalidar member/owner/baú no callback e liberar o lock em sucesso, falha e
  exceção, sem perda ou duplicação.

## Assistência inteligente

- Testar smart refill em Disabled, Farm only e Any safe outdoor water. Cobrir
  raios 3 e 40, água no limite, lago/fonte válida, interior, fonte inacessível,
  tile reservado e mudança da opção enquanto o NPC caminha.
- Esvaziar o regador no meio de uma área/diretiva. O companion deve mostrar
  Reabastecendo, caminhar por rota normal, usar somente
  `CanRefillWateringCanOnTile`, restaurar a capacidade correta do upgrade e
  retomar o mesmo cultivo ainda seco. Se o cultivo deixou de ser válido, deve
  replanejar sem regar um substituto.
- Ativar Smart tool swap com slot ausente/inadequado e disponibilizar várias
  ferramentas compatíveis. O host deve escolher uma utilizável, trocar pela
  célula exata do owner e não substituir uma ferramenta já utilizável nem
  aceitar encantamento/acessório/estado inseguro. Desativado não deve tocar na
  toolbar.
- Testar Smart deposit em Disabled, When full e After every task com baú cheio,
  distante no mundo, movido, bloqueado e depois liberado. O estado deve alternar
  entre Depositando carga e Depósito bloqueado corretamente; filtros ficam no
  cargo e o trabalho só retoma depois do callback seguro.
- Repetir como farmhand e confirmar que modos/radius vêm das regras do host, não
  do config local do cliente.

## Painel geral da equipe

- Abrir a sexta aba Equipe com 0, 1, 3 e 12 companions, em mapas iguais e
  diferentes. Cada card deve mostrar mapa, atividade/alvo, ferramenta
  relevante, água, cargo e motivo somente enquanto estiver bloqueado; uma falha
  antiga já resolvida não pode manter o card vermelho.
- Cobrir scroll, hover e foco em mouse/teclado/controle e viewports compactos.
  Clicar em um card deve abrir Geral no companion correto.
- Parar todos deve cancelar task/controller/recall de cada companion do owner e
  deixá-los em Waiting sem apagar diretivas, filtros ou rotinas.
- Depositar tudo deve respeitar baú/filtros de cada membro, reportar vazio,
  iniciado, parcial e completo sem confundir mutex pendente com sucesso já
  commitado. Aplicar rotinas deve retomar a rotina própria de cada NPC sem copiar
  grids nem repetir bloco concluído.
- Repetir os três comandos como farmhand; companions de outro owner não podem
  mudar e replay do mesmo `CommandId` deve continuar idempotente.

## Vida e personalidade

- Deixar companions realmente parados e confirmar look/emote/jump/shake perto
  do intervalo configurado. Movimento, follow controller, tarefa, recall,
  animação de trabalho, menu/evento e owner em outro mapa devem reiniciar ou
  suspender o relógio ocioso.
- Trocar mapa e clima (chuva, tempestade, neve e chuva verde) e confirmar uma
  reação atrasada por grupo, não uma rajada por NPC. Falas devem usar prioridade
  ambiente e respeitar cooldown do owner.
- Colocar dois ou mais companions do mesmo owner a até três tiles. Confirmar
  facing mútuo, alternância justa de pares, cooldown individual/par e no máximo
  um evento social por owner no mesmo passe. Owners diferentes são isolados.
- Testar NPCs com e sem linhas específicas, pets com expressões ligadas e
  desligadas e multiplayer. Cada cliente deve receber uma única expressão
  host-authoritative; voltar ao título e abrir outro save não pode herdar
  deadlines, local, clima ou cooldowns.

## Pesca dirigida

- Selecionar uma vara de pesca sem encantamento, isca nem apetrecho na barra, abrir
  `F9 > Inventário` no perfil de um NPC e clicar no slot Vara de pesca. A vara
  deve sair da célula exata do player e ocupar o slot operacional persistente
  desse owner/NPC. Item que não seja vara, vara encantada, vara com qualquer
  acessório — inclusive o segundo apetrecho da Advanced Iridium Rod — ou
  estado customizado não serializável deve ser recusado sem consumir, duplicar
  ou alterar o item; o slot não pode virar uma porta para itens genéricos.
- Com companions locais com e sem vara, pressionar `X` sobre água. A roda deve
  oferecer Mandar todos pescar e os nomes somente dos companions que equiparam uma
  vara; sem nenhum elegível, deve avisar que falta vara, sem transformar água em
  destino de espera. Confirmar também paginação e seleção por controle.
- Repetir em tiles com `NoFishing`, camada Buildings sobre água e em uma location
  cujo `canFishHere()` esteja falso. A roda não deve abrir. Em Railroad/Ilha e
  outros locais com recompensas especiais, a pesca do NPC não pode iniciar mail,
  quest, noz dourada, colar, flag de equipe nem outro side effect de captura única.
- Preparar dois lagos próximos, porém separados por terra, e escolher um deles.
  O NPC deve caminhar sem teleporte até a margem segura alcançável mais próxima
  do corpo de água cardinalmente conectado que foi clicado, nunca até a margem
  do outro lago. Bloquear a primeira margem durante o trajeto deve provocar
  replanejamento seguro dentro do mesmo corpo de água.
- Mandar vários companions com vara ao mesmo lago. Cada um deve reservar um tile
  de margem distinto, olhar para a água e exibir vara, linha e boia sem colisão.
  Se houver menos margens seguras do que trabalhadores, o envio deve ser parcial
  e nenhuma reserva pode vazar após falha ou cancelamento.
- Deixar a sessão rodar e conferir que somente peixes regulares não lendários
  entram primeiro no baú atribuído ou, sem ele, no cargo do NPC; cada peixe
  concede 8 XP pela progressão comum e a pesca continua entre capturas. Com os
  destinos de overflow cheios,
  o peixe deve cair de forma segura no mundo, sem perda ou duplicação.
- Manter o NPC pescando até 26:00/fim do dia e confirmar encerramento e liberação
  de rota/reserva. Repetir enviando outro comando, como Parar, Seguir jogador, Trabalhar
  ou outro alvo, e também desligando tarefas com `F8`: a pesca deve terminar
  imediatamente e o novo comando prevalecer. Retirar a vara durante a sessão
  também deve cancelar com aviso seguro.
- Salvar/recarregar durante a pesca e avançar o dia. A vara e os peixes já
  guardados devem persistir respectivamente no slot e no destino em que foram
  roteados, mas caminho,
  margem reservada, boia,
  relógio da próxima captura e sessão incompleta não podem reaparecer como
  runtime antigo.
- Na quarta linha da árvore, aprender em sequência Pescador paciente, Arremesso
  em águas profundas e Puxada dupla. Confirmar respectivamente intervalo de
  captura de 60 para 50 minutos do jogo; um tile adicional de arremesso e um
  nível adicional de qualidade; e 25% de chance de um peixe extra, com custo e
  pré-requisitos bloqueando os tiers seguintes até a compra anterior.

## Chapéus cosméticos

- Selecionar um chapéu vanilla na barra, abrir `F9 > Inventário` e clicar no
  slot dourado de chapéu. O item deve sair da barra e aparecer alinhado à cabeça
  do NPC nas quatro direções, inclusive enquanto ele caminha.
- Observar separadamente frames pares/ímpares ao caminhar nas quatro direções:
  o chapéu deve acompanhar a subida/descida interna do sprite. Repetir com
  George (sem bob), Jas/Vincent e Krobus (bob dependente da direção) para
  confirmar que o renderer não aplica um deslocamento fixo inexistente.
- Com outro chapéu selecionado, clicar novamente e confirmar troca direta: o
  anterior ocupa exatamente o slot da barra e o novo fica equipado, inclusive
  com todos os demais slots cheios.
- Selecionar um slot vazio e clicar no chapéu equipado. Ele deve voltar ao
  inventário; ferramenta ou item que não seja chapéu deve apenas produzir aviso.
- Equipar, usar Dismiss e Dismiss All, trocar de mapa, passar o dia e
  salvar/recarregar. O NPC deve continuar usando o chapéu fora da equipe e após
  ser recrutado novamente; ele nunca deve migrar para overflow/inventário do
  grupo durante o dismiss.
- Repetir como farmhand. O host deve validar owner, índice, fingerprint do item
  selecionado e fingerprint do chapéu anterior; atrasar/reordenar dois pedidos
  não pode remover ou substituir um cosmético mais novo. Todos os jogadores
  devem ver o mesmo cosmético antes e depois do dismiss.
- Repetir com chapéu customizado; desativar seu content pack no reload deve
  esconder o desenho sem apagar o registro. Reativar o pack deve restaurá-lo.
- Pets devem recusar este slot genérico (eles conservam o sistema de chapéus
  próprio do jogo) sem consumir o item selecionado.

## Áreas de trabalho e animações

- Em chão vazio seguro, abrir `X > Trabalhar`, escolher Madeira, Mineração, Regar
  e Limpar área e, em cada caso, testar Mandar todos e um NPC específico. Não deve
  existir uma etapa para escolher o raio nem os botões Área livre/Área delimitada:
  esse fluxo manual continua sempre circular. Madeira não pode escolher pedras,
  Mineração não pode escolher árvores, Regar deve escolher apenas terra seca e
  Limpar deve processar madeira e mineração; modo
  correspondente desativado deve produzir feedback sem gravar uma ordem impossível.
- Para Regar, preparar terra seca/molhada dentro e fora da borda, deixar um
  sprinkler molhar o alvo durante o caminho e confirmar replanejamento seguro.
  Deixar também três tiles secos alcançáveis e confirmar que o NPC visita três
  coordenadas distintas, sem voltar ao tile que acabou de regar; depois do
  terceiro, a área deve concluir sem novos movimentos de rega.
  Trocar o owner de mapa não pode interromper a área nem consumir stamina ou o
  regador do farmer; cada tile concluído deve consumir uma unidade somente do
  regador equipado no perfil do companion. Repetir como farmhand e confirmar a
  mutação pelo host.
- Ajustar `CompanionWorkRadius` primeiro para 3 e depois para 20. A roda deve
  pular direto da especialidade para a escolha dos companions e usar exatamente
  esse máximo do host. A marca deve mostrar centro e borda circular, incluir o
  tile exatamente no raio e excluir diagonais que caberiam apenas no quadrado
  envolvente. O preview deve sumir sem deixar overlay permanente; uma ordem
  rejeitada pelo host deve removê-lo assim que o feedback chegar ao farmhand.
- Preparar recursos logo dentro e logo fora da borda. O NPC pode ocupar um stand
  adjacente até um tile fora do círculo, mas nunca selecionar o recurso externo.
  Repetir nas quatro direções e em diagonal.
- Com dois companions na mesma área, reservar ou bloquear temporariamente os
  únicos stands. O estado deve indicar área bloqueada e tentar novamente depois;
  não pode anunciar conclusão enquanto ainda houver recurso compatível. Remover
  de fato o último recurso deve concluir uma única vez e deixar o NPC Waiting.
- Trocar o owner de mapa durante a ordem: workers e centro devem permanecer no
  mapa marcado. Usar Wait deve pausar sem apagar a área; Resume deve retomá-la.
  `F8` deve pausar/retomar o planejamento, enquanto Follow, Recall ou uma ordem
  direta incompatível deve cancelar a área explicitamente.
- Salvar/recarregar e avançar o dia com área ativa. Confirmar o mesmo mapa,
  centro, raio e especialidade, sem ressuscitar target já removido nem duplicar
  reserva. Desconectar/reconectar o owner deve preservar a intenção com o estado
  de estacionamento seguro esperado. Em mapa customizado, forçar uma falha de
  posicionamento, liberar depois um tile seguro no mesmo mapa e confirmar que o
  retry periódico recupera a área sem exigir troca de location. Repetir o reload
  com `F8` desligado: o status deve continuar pausado até reativar tarefas. Ainda
  durante uma falha de posicionamento, remover o último recurso compatível e
  confirmar que a área conclui em vez de tentar recovery para sempre.
- Para Lumber, Mine e Water, observar que o NPC chega ao stand, olha para o
  target e só então mostra Axe, Pickaxe ou Watering Can. Para Gather, Harvest e
  Pet, deve aparecer uma ação de mão legível; nenhuma animação pode começar
  enquanto o companion ainda está caminhando, e o alvo não pode mudar ou sumir
  antes de o movimento visível chegar ao impacto. Comparar vários golpes de
  Axe/Pickaxe: o swing deve ocupar o cooldown existente, sem somar uma espera
  extra à cadência anterior.
- Forçar sucesso e falha em cada família de tarefa. Sucesso deve mostrar
  impacto/check e reação positiva; falha deve mostrar pergunta/tremor sem
  consumir recurso ou conceder item/XP. Troca de mapa, menu e reload devem
  limpar efeitos transitórios sem deixar ferramenta desenhada no NPC.

## Comunicação contextual e pets

- Recrutar três NPCs, reduzir `DialogueCooldownSeconds` e colocar
  `CommunicationGroupCooldownSeconds` em um valor observável. Disparar idle,
  tarefa, falha e conclusão em sequência: nunca deve haver duas falas dentro do
  intervalo coletivo, e milestone/comando deve sair antes de tarefa/ambiental.
- Enfileirar repetidamente a mesma categoria e provocar eventos depois do TTL.
  Pedidos equivalentes devem ser deduplicados, eventos vencidos não devem falar
  tarde e chatter não pode crescer sem limite nem inundar balões/HUD.
- Com um perfil de teste contendo pelo menos três IDs de fala, observar várias
  emissões e recarregar o save. Enquanto houver alternativa, a mesma linha não
  deve repetir no grupo nem no mesmo speaker; o último speaker de idle deve ser
  evitado quando outro companion for elegível.
- Criar uma linha específica para um NPC e fallbacks `All_Villager`/`Generic`.
  Confirmar prioridade do nome exato e condições isoladas/combinadas para
  amizade, spouse, manhã/noite, estação, sol/chuva/neve, interior/exterior,
  Farm/Mine, tarefa, manual, sucesso/falha e item. Tokens visíveis devem refletir
  o owner e o contexto reais, nunca `Game1.player` de outra tela. Se a linha
  exata estiver recente, `All_Villager` ainda deve preceder `Generic`, mesmo que
  este tenha mais condições. Em co-op com locales diferentes, cada cliente deve
  traduzir localmente; se não tiver a chave, deve conservar o texto do host.
- Cancelar o recrutamento pelo atalho dedicado, como host e farmhand. A recusa
  deve passar pelo scheduler do host e produzir fala do NPC ou expressão
  silenciosa do pet; confirmar deve continuar revalidando elegibilidade no host.
  Pela roda do `X`, selecionar Recrutar deve enviar a tentativa imediatamente,
  sem abrir uma segunda tela de confirmação.
- Recrutar gato, cachorro e tartaruga e provocar Recruit, Idle, sucesso, falha,
  recusa e Dismiss. Não pode aparecer texto acima do pet; devem surgir somente
  emote, som e pulo/tremor adequados. Desativar `EnablePetExpressions` deve
  silenciar também esses efeitos sem afetar comandos ou tarefas.
- Desativar `EnableCommunication`: NPCs comuns deixam de falar espontaneamente,
  pets continuam obedecendo sua opção separada e nenhuma fila antiga deve
  despejar várias falas ao reativar.

## Interface

- Passar o cursor pelo corpo/cabeça de dois NPCs sobrepostos e confirmar que X
  seleciona o NPC visualmente sob o mouse, sem escolher outro perto do farmer.
- Em companion próprio, testar Perfil, Trabalhar, Seguir rotina, Parar,
  Dispensar e Seguir jogador. O
  perfil deve abrir níveis, skills e inventário; Parar deve guardar a posição;
  Seguir jogador deve cancelar tarefa/diretivas e retornar ao owner; Dispensar deve
  exigir confirmação e não alterar o grupo ao cancelar.
- Durante blocos Follow/Wait/Rotina original e de trabalho, criar separadamente
  quick work, recall, tarefa contextual, diretiva e área manual. `X > Seguir
  rotina` deve cancelar cada override e aplicar imediatamente a atividade da
  hora. Se o bloco de trabalho/depósito já terminou, deve preservar a conclusão
  sem executá-lo de novo. Com rotina inativa ou one-shot expirada, deve avisar e
  manter a ordem manual atual intacta.
- Em inglês e PT-BR, confirmar que Perfil, Trabalhar, Seguir rotina, Parar,
  Dispensar, Seguir jogador e
  a dica inferior aparecem completos; repetir com Mandar NPC de nome longo e
  conferir a quebra em até três linhas, sempre com o mesmo tamanho de fonte e
  sem sobreposição entre setores.
- Em NPC não recrutado no mesmo mapa, confirmar que aparece apenas Recrutar e
  que a ação funciona tanto ao lado quanto do outro lado da área visível, sem
  depender da distância do jogador. Selecionar Recrutar deve executar direto,
  sem confirmação adicional. Amizade, suporte do NPC e lotação continuam
  revalidados no commit; um NPC de outro mapa deve ser rejeitado. Em companion
  de outro jogador, nenhuma ação mutável deve abrir.
- Centro, fora do círculo, separadores, botão direito, Escape e um segundo X
  devem cancelar sem executar ação no mundo.
- Apontar para o tronco ou a copa de árvore adulta não-tapped, pedra quebrável e cultura
  madura de colheita manual; conferir Mandar todos e até três opções Mandar NPC.
  Tile vazio, árvore jovem/frutífera/tapped, boulder, cultivo imaturo ou de foice
  não devem ser tratados como alvo seguro.
- Na borda do alcance, deixar o recurso a quatro tiles do farmer e manter livre
  somente o stand cardinal a três tiles: `X` deve abrir e a ordem deve concluir.
  A cinco tiles, sem stand adjacente dentro do raio, o recurso deve ser rejeitado.
- Mandar um companion ocupado/esperando e confirmar substituição da ordem e
  retomada do follow. Madeira/mineração devem usar apenas o machado/picareta do
  slot correspondente; sem a ferramenta, falhar com motivo legível e sem criar
  uma ferramenta virtual.
- Desligar tarefas globais, usar Mandar NPC/Mandar todos e confirmar que somente
  a ordem pontual executa; o toggle deve continuar desligado e nenhuma diretiva
  automática deve retomar depois.
- Mandar todos para árvore/pedra: cada NPC deve ocupar tile adjacente distinto,
  contribuir golpes e encerrar sem target-lost quando um deles remover o alvo.
  Em cultura, apenas um colhe e os peers encerram como conclusão do cohort.
- Com mais NPCs que espaços adjacentes seguros, conferir feedback de envio
  parcial e ausência de colisão/reserva vazada.
- Trocar/remover o recurso com a roda aberta e também durante o trajeto do NPC;
  token, instância, mapa ou tile obsoletos devem ser rejeitados sem atingir o
  novo objeto.
- Tentar substituir uma tarefa quando o alvo está reservado por outro worker ou
  não há stand seguro; a ordem anterior deve permanecer intacta.
- Deixar um companion com trabalho ativo em Esperar, escolher Trabalhar e
  confirmar que ele retoma, as tarefas globais são reativadas e não há toggle
  acidental ao escolher Trabalhar novamente.
- Abrir a roda junto às quatro bordas, com UI scale diferente e em split-screen;
  círculo, nome, texto de ajuda e hitboxes devem permanecer dentro do viewport.
- Pressionar X sobre chão vazio, seguro e alcançável com 1, 2, 3, 6 e 12
  companions recrutados. Conferir Trabalhar e Dispensar todos fixos e percorrer
  todas as páginas: cada opção Mandar NPC deve aparecer exatamente uma vez, em
  ordem estável, inclusive o 12º nome e a última página parcial.
- Repetir a roda de chão, recurso e seleção da área sem mouse. Setas/WASD,
  D-pad e ambos os sticks devem mover o foco espacial sem parar em slot vazio;
  A/Enter ativa, B/Escape cancela e shoulders/Page Up/Page Down trocam páginas.
  Repetir com scroll do mouse, voltar da última à primeira página e confirmar
  que a ação global continua acessível em todas elas.
- Repetir em um tile visível a mais de três tiles do farmer e com o companion
  seguindo, trabalhando ou esperando também fora desse raio. A roda e o nome
  devem aparecer; bloqueios, outro mapa e rota definitivamente impossível
  continuam rejeitados. Água deve seguir exclusivamente o fluxo de pesca e não
  herdar o limite de três tiles dos recursos.
- Escolher Mandar NPC com o companion seguindo, trabalhando e esperando. Ele
  deve abandonar a ordem anterior, caminhar sem teleporte até o tile marcado e
  mudar para Waiting somente ao chegar; a posição final deve sobreviver a
  save/reload, troca de mapa e mudança de dia.
- Salvar e recarregar no meio do trajeto. A fila transitória não deve reaparecer
  como trabalho ou Waiting falso: o companion volta ao estado persistente
  Following e pode receber uma nova ordem normalmente.
- Abrir a roda de chão, confirmar Dispensar todos e testar primeiro Cancelar:
  nenhum membro, item ou estado deve mudar. Em seguida aceitar e conferir que
  todos os companions do owner são dispensados, com inventários preservados e
  agendas vanilla restauradas pelo fluxo normal.
- Tentar abrir a roda de chão sobre parede, objeto, crop maduro ou imaturo,
  grama, sapling, árvore jovem/frutífera/tapped ou sua copa, resource clump,
  porta, tile de Action/TouchAction e warp. Nenhum deles pode virar um destino
  de espera vazio; NPC e recurso contextual continuam tendo prioridade. Sobre
  água, somente o contexto de pesca pode abrir, e apenas com companion elegível.
- Com a ordem em trânsito, bloquear/remover a passagem ou o destino e também
  forçar timeout/troca de mapa. O NPC deve parar e ficar Waiting onde chegou,
  emitir o aviso de falha, não teleportar e liberar a reserva para nova ordem.
- Pressionar F8 enquanto o NPC caminha para o ponto: o toggle global pode mudar,
  mas o deslocamento deve continuar e concluir em Waiting. Desconectar o owner
  durante o trajeto deve cancelar a intenção transitória com segurança.
- O mesmo X ou clique usado pela roda de chão não pode falar com NPC, usar
  ferramenta ou abrir menu vanilla por baixo.
- Testar HUD Detailed e Compact com 1, 3, 6 e 12 companions.
- Confirmar que uma configuração anterior migra o dock uma vez para a esquerda,
  com cabeçalho, retratos emoldurados, estados, badges e botões sem cortes.
- Confirmar que nome e status nunca se sobrepõem, que nenhum texto escapa do
  cabeçalho/card e que os badges pixelados continuam nítidos sem depender da
  sombra da fonte.
- Em inglês e PT-BR, conferir o título curto do dock, nomes longos e os estados
  `Indo ao local marcado`, `Aguardando reconexão` e `Indisponível`; truncamento
  horizontal pode ocorrer, mas nunca uma linha cortada verticalmente.
- Confirmar que os dois botões por ícone têm tooltip, realce de hover e alvo de
  clique íntegro, e que o chevron do cabeçalho comunica a abertura do painel.
- Alternar o dock entre esquerda/direita e confirmar que avisos/debug usam o
  lado oposto sem sobreposição.
- Testar em 1920x1080, 1280x720, 800x600 e no menor viewport suportado.
- Testar todas as abas em split-screen horizontal (~640x360) e em viewports
  lógicos aproximados de 512x288 e 426x240 (UI scale 125%/150%); confirmar
  comandos, diretivas, doze skills e slots visíveis/clicáveis, sem texto ou badge
  vazando para o ramo vizinho.
- Nesses tamanhos, abrir o painel F9 e conferir o novo cabeçalho: título
  centralizado, divisor dourado/verde e subtítulo traduzido somente no viewport
  amplo que comporta o cabeçalho expandido. Em 800x600, split-screen e nos
  viewports lógicos menores, o subtítulo deve sumir e o conteúdo deve recuperar
  o espaço sem cortes nem deslocar o botão de fechar para fora da moldura.
- Percorrer roster e todas as abas e confirmar o pergaminho amarelado nativo da
  textura vanilla, sem preenchimento branco opaco, cards internos bem separados
  e barras de estado coerentes em
  cabeçalho, membros, resumo, localização, alvo e equipamentos. Nenhuma moldura
  pode duplicar, vazar, cobrir texto ou reduzir a área clicável nos layouts
  amplo, intermediário e compacto.
- Conferir contraste de texto marrom, texto secundário, badges e acentos de
  estado sobre fundos amarelado/dourado/verde, inclusive hover, seleção e estados
  inativos. A aba ativa deve usar superfície dourada com marca verde; hover e o
  foco por teclado/controle devem permanecer dourados, nítidos e distintos sem
  mover o conteúdo ou esconder badges.
- Em mouse, teclado e controle, validar hover e foco de todos os botões. Ações
  ativas/afirmativas devem usar verde com rótulo branco, ações destrutivas e o
  fechamento devem usar coral com rótulo branco, e botões neutros devem manter
  contraste e realce de hover; cores, texto e hitbox precisam coincidir em todos
  os tamanhos listados acima.
- Na árvore de habilidades, conferir os quatro ramos, cada um progredindo da
  esquerda para a direita, e os conectores seguindo os pré-requisitos. No layout
  amplo, conferir também o progresso por ramo e o inspetor lateral persistente
  do nó apontado/focado, com nome, ramo/tier, custo, pontos, estado, descrição e
  ação sem elipse indevida ou sobreposição. Em layouts estreitos intermediários,
  conferir o card de detalhes abaixo da árvore; no fallback compacto, conferir
  os mesmos dados no tooltip limitado à viewport. Repetir com 0, 1 e 2 pontos e
  com raízes/tier 2/tier 3 aprendidos para distinguir Aprendida, Disponível,
  Bloqueada e Pontos insuficientes.
- Conferir especificamente a captura de 1180x780 em inglês e PT-BR: título,
  roster em duas linhas, nome/status do membro, seis abas, nomes dos ramos,
  badge de estado e todas as linhas do inspetor devem permanecer dentro de seus
  retângulos. Sombras não podem produzir texto duplicado ou sangrar nas bordas.
- Na aba Rotina em 1180x780, confirmar vinte horários em uma grade 5x4, com hora
  proeminente e atividade centralizada, oito ações abreviadas sem reticências e
  espaçamento uniforme entre controles. Nos viewports compactos, os vinte
  horários devem continuar visíveis e clicáveis na grade densa de fallback; em
  células menores que 29 px, mostrar somente `06h`–`01h` em tamanho legível, com
  a atividade comunicada pela cor da célula e pelo tooltip.
- Em 512x288 e 426x240, as abas devem trocar para seus rótulos localizados
  curtos (`Geral`, `Tarefas`, `Hab.`, `Itens`, `Plano`, `Equipe` em PT-BR), sem colidir com badges
  ou com o sublinhado da aba ativa.
- Desativar progressão e confirmar que skills já aprendidas continuam legíveis,
  as restantes ficam Desativadas e nenhum clique gasta ponto. Reativar e aprender
  uma skill disponível; custo, conector, badge da aba e card de detalhes devem
  atualizar sem reabrir o painel.
- Abrir Habilidades por teclado/controle e confirmar foco inicial na primeira
  skill aprendível. No controle, D-pad deve navegar tiers horizontalmente e ramos
  verticalmente; A aprende somente uma skill disponível, shoulders mantêm a troca
  de abas e B fecha.
- Alterar UI scale e redimensionar a janela com o menu aberto.
- Clicar nas bordas e nos espaços entre botões; nenhum clique deve ser engolido.
- Rolar a lista; linhas cortadas não podem desenhar/clicar fora do viewport.
- Navegar por teclado e controle, trocar abas e fechar com o botão de menu.
- Cancelar a confirmação de Dismiss e confirmar que o painel reabre na mesma
  seleção.
- Conferir textos longos em inglês e PT-BR.

## Multiplayer experimental

- Repetir Recrutar e as seis ações de companion pelo wheel como farmhand;
  ownership e feedback devem continuar host-authoritative.
- Repetir Mandar NPC/Mandar todos para árvore e pedra como farmhand, trocar de
  mapa antes do host processar e confirmar rejeição stale. Cultura de farmhand
  deve explicar a limitação e não creditar item/XP ao host.
- Como farmhand, abrir a roda em chão vazio e testar Mandar NPC e Dispensar
  todos (cancelar/confirmar). O host deve revalidar owner, NPC, mapa, tile,
  segurança, reachability e reserva; comando atrasado/repetido ou
  mudança de mapa antes do processamento deve falhar sem movimento, dismiss ou
  feedback no HUD do jogador errado.
- Como farmhand, ajustar `CompanionWorkRadius` do host para 3 e depois para 20 e
  executar Trabalhar > Madeira/Mineração/Limpar > um/todos. O host deve derivar
  o raio da própria configuração, ignorando qualquer índice divergente enviado
  no payload, e revalidar owner, mapa, centro seguro, especialidade, modos e
  membros; replay, mapa stale ou companion de outro owner não pode criar área
  nem cancelar o trabalho anterior.
- Como farmhand, equipar uma vara sem encantamentos ou acessórios no slot Fishing Rod e mandar um/todos pescar.
  O host deve revalidar owner, slot, fingerprint da vara, mapa, corpo de água,
  margem, reserva e presença da vara antes de substituir qualquer ordem. Replay,
  slot ou água stale e companion de outro owner devem falhar sem item, XP,
  captura ou movimento duplicado.
- Observar a mesma tarefa simultaneamente no host e farmhand: cada ação deve
  produzir uma animação e um commit, sem ferramenta/efeito duplicado nem item/XP
  extra. Repetir com fala de NPC e pet: texto/emote/som deve aparecer uma vez em
  cada cliente, e mensagem cosmética enviada por farmhand deve ser ignorada.
- Enquanto uma animação ou a prévia circular estiver visível no farmhand,
  forçar snapshots frequentes. Eles não podem interromper o efeito nem apagar a
  prévia recém-criada.
- Com companions de owners diferentes no mesmo mapa, saturar as duas filas de
  comunicação. O cooldown deve ser compartilhado dentro de cada grupo, não entre
  owners; nenhum texto ou expressão pode ser entregue à tela/mapa incorreto.
- Durante um Mandar NPC remoto, bloquear o destino ou desconectar o owner e
  confirmar liberação da reserva, ausência de teleporte e snapshot coerente de
  Following/Waiting após reconexão.
- Repetir Recruit, Wait, Resume, Recall e Dismiss como host e farmhand.
- Repetir Toggle Tasks, quick work, diretivas, skills, tarefa manual/mimic e
  withdraw unitário/all como farmhand; reconectar e confirmar persistência.
- Manter host e farmhand em mapas diferentes durante tarefas.
- Desconectar/reconectar o owner com e sem `WarpHomeOnDisconnect`.
- Deixar um companion em Waiting antes de desconectar; deve continuar Waiting.
- Repetir o caso Waiting com `WarpHomeOnDisconnect=true`; ele não pode ser
  dispensado por ser uma ordem explícita.
- Em split-screen, entrar/sair com uma tela secundária e salvar: companions,
  inventários e revisão do host não podem ser limpos ou substituídos.
- Adiar um comando espacial durante evento, trocar o farmhand de mapa e confirmar
  que o host rejeita as coordenadas antigas.
- Abrir menu/evento apenas no host e confirmar que o companion do farmhand segue
  simulando; em split-screen, abrir um menu na segunda tela e confirmar que só o
  companion daquele owner pausa e perde a rota antiga.
- Executar comandos como farmhand e segunda tela local: sucesso/erro deve aparecer
  apenas no HUD solicitante, nunca no HUD principal do host.
- Dar duplo clique em toggle/diretiva e numa retirada enquanto outro companion
  muda o estado global; o comando deve ser idempotente e jamais retirar outra
  qualidade/cor/modData da mesma ID.
- Confirmar que cada tarefa usa somente a ferramenta equipada no perfil
  owner/NPC e que o mapa validado pertence ao owner do comando.
- Verificar logs de versão diferente e ausência de disputa visível de controller.
- Confirmar que Harvest remoto informa a limitação e não credita itens/XP ao host.
