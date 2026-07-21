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
- Com 1, 3 e 12 companions em mapa grande, pressionar repetidamente `X > Seguir`
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

## Áreas de trabalho e animações

- Em chão vazio seguro, abrir `X > Trabalhar`, escolher Madeira, Mineração e
  Limpar área, selecionar um dos raios oferecidos e, em cada caso, testar Mandar
  todos e um NPC específico. Madeira não pode escolher pedras, Mineração não
  pode escolher árvores e Limpar deve processar ambos; modo correspondente
  desativado deve produzir feedback sem gravar uma ordem impossível.
- Ajustar `CompanionWorkRadius` primeiro para 3 e depois para 20. A roda deve
  nunca oferecer/acatar valor acima do máximo do host. A marca deve
  mostrar centro e borda circular, incluir o tile exatamente no raio e excluir
  diagonais que caberiam apenas no quadrado envolvente. O preview deve sumir sem
  deixar overlay permanente; uma ordem rejeitada pelo host deve removê-lo assim
  que o feedback chegar ao farmhand.
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
- Cancelar o recrutamento pelo atalho e pela roda, como host e farmhand. A recusa
  deve passar pelo scheduler do host e produzir fala do NPC ou expressão
  silenciosa do pet; confirmar deve continuar revalidando elegibilidade no host.
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
- Em companion próprio, testar Perfil, Trabalhar, Parar, Dispensar e Seguir. O
  perfil deve abrir níveis, skills e inventário; Parar deve guardar a posição;
  Seguir deve cancelar tarefa/diretivas e retornar ao owner; Dispensar deve
  exigir confirmação e não alterar o grupo ao cancelar.
- Em inglês e PT-BR, confirmar que Perfil, Trabalhar, Parar, Dispensar, Seguir e
  a dica inferior aparecem completos; repetir com Mandar NPC de nome longo e
  conferir a quebra em até três linhas, sempre com o mesmo tamanho de fonte e
  sem sobreposição entre setores.
- Em NPC não recrutado no mesmo mapa, confirmar que aparece apenas Recrutar e
  que a ação funciona tanto ao lado quanto do outro lado da área visível, sem
  depender da distância do jogador. A roda deve pedir confirmação. Amizade,
  suporte do NPC e lotação continuam revalidados no commit; um NPC de outro mapa
  deve ser rejeitado. Em companion de outro jogador, nenhuma ação mutável deve
  abrir.
- Centro, fora do círculo, separadores, botão direito, Escape e um segundo X
  devem cancelar sem executar ação no mundo.
- Apontar para o tronco ou a copa de árvore adulta não-tapped, pedra quebrável e cultura
  madura de colheita manual; conferir Mandar todos e até três opções Mandar NPC.
  Tile vazio, árvore jovem/frutífera/tapped, boulder, cultivo imaturo ou de foice
  não devem ser tratados como alvo seguro.
- Mandar um companion ocupado/esperando e confirmar substituição da ordem,
  retomada do follow e uso de ferramenta básica sem exigir Axe/Pickaxe equipada.
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
  devem aparecer; água, bloqueios, outro mapa e rota definitivamente impossível
  continuam rejeitados.
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
- Tentar abrir a roda de chão sobre água, parede, objeto, crop maduro ou imaturo,
  grama, sapling, árvore jovem/frutífera/tapped ou sua copa, resource clump,
  porta, tile de Action/TouchAction e warp. Nenhum deles pode virar um destino
  de espera vazio; NPC e recurso contextual continuam tendo prioridade.
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
  comandos, diretivas, nove skills e slots visíveis/clicáveis, sem texto ou badge
  vazando para o ramo vizinho.
- Na árvore de habilidades, conferir os três ramos, cada um progredindo da
  esquerda para a direita, e os conectores seguindo os pré-requisitos. No layout
  amplo, conferir também o progresso por ramo e o inspetor lateral persistente
  do nó apontado/focado, com nome, ramo/tier, custo, pontos, estado, descrição e
  ação sem elipse indevida ou sobreposição. Em layouts estreitos intermediários,
  conferir o card de detalhes abaixo da árvore; no fallback compacto, conferir
  os mesmos dados no tooltip limitado à viewport. Repetir com 0, 1 e 2 pontos e
  com raízes/tier 2/tier 3 aprendidos para distinguir Aprendida, Disponível,
  Bloqueada e Pontos insuficientes.
- Conferir especificamente a captura de 1180x780 em inglês e PT-BR: título,
  roster em duas linhas, nome/status do membro, quatro abas, nomes dos ramos,
  badge de estado e todas as linhas do inspetor devem permanecer dentro de seus
  retângulos. Sombras não podem produzir texto duplicado ou sangrar nas bordas.
- Em 512x288 e 426x240, as abas devem trocar para seus rótulos localizados
  curtos (`Geral`, `Tarefas`, `Hab.`, `Itens` em PT-BR), sem colidir com badges
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

- Repetir Recrutar e as cinco ações de companion pelo wheel como farmhand;
  ownership e feedback devem continuar host-authoritative.
- Repetir Mandar NPC/Mandar todos para árvore e pedra como farmhand, trocar de
  mapa antes do host processar e confirmar rejeição stale. Cultura de farmhand
  deve explicar a limitação e não creditar item/XP ao host.
- Como farmhand, abrir a roda em chão vazio e testar Mandar NPC e Dispensar
  todos (cancelar/confirmar). O host deve revalidar owner, NPC, mapa, tile,
  segurança, reachability e reserva; comando atrasado/repetido ou
  mudança de mapa antes do processamento deve falhar sem movimento, dismiss ou
  feedback no HUD do jogador errado.
- Como farmhand, executar Trabalhar > Madeira/Mineração/Limpar > um/todos com
  raio 3 e 20. O host deve revalidar owner, mapa, centro seguro, raio,
  especialidade, modos e membros; replay, raio forjado, mapa stale ou companion
  de outro owner não pode criar área nem cancelar o trabalho anterior.
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
- Confirmar que somente a ferramenta e o mapa do owner são usados.
- Verificar logs de versão diferente e ausência de disputa visível de controller.
- Confirmar que Harvest remoto informa a limitação e não credita itens/XP ao host.
