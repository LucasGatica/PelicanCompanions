# Roteiro de QA manual

O validador automatizado compila o mod, executa 26 testes de regressão e verifica
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

## Interface

- Passar o cursor pelo corpo/cabeça de dois NPCs sobrepostos e confirmar que X
  seleciona o NPC visualmente sob o mouse, sem escolher outro perto do farmer.
- Em companion próprio, testar Perfil, Trabalhar, Parar, Dispensar e Seguir. O
  perfil deve abrir níveis, skills e inventário; Parar deve guardar a posição;
  Seguir deve cancelar tarefa/diretivas e retornar ao owner; Dispensar deve
  exigir confirmação e não alterar o grupo ao cancelar.
- Em NPC não recrutado, confirmar que aparece apenas Recrutar e que amizade,
  distância e lotação continuam revalidadas no clique. Em companion de outro
  jogador, nenhuma ação mutável deve abrir.
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
- Pressionar X sobre chão vazio, seguro e alcançável com 1, 2, 3 e mais de 3
  companions recrutados. Conferir Dispensar todos e no máximo três opções
  Mandar NPC, ordenadas de modo estável, sem o quarto nome vazar para a roda.
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
- Alternar o dock entre esquerda/direita e confirmar que avisos/debug usam o
  lado oposto sem sobreposição.
- Testar em 1920x1080, 1280x720, 800x600 e no menor viewport suportado.
- Testar todas as abas em split-screen horizontal (~640x360) e em viewports
  lógicos aproximados de 512x288 e 426x240 (UI scale 125%/150%); confirmar
  comandos, diretivas, nove skills e slots visíveis/clicáveis.
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
