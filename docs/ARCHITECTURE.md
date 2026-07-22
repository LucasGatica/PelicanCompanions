# Arquitetura do Pelican Companions

Este documento descreve a organização atual do ramo `Unreleased`, posterior à
versão 1.5.3. A regra principal é simples: `ModEntry.cs` compõe o mod e registra
integrações; cada arquivo parcial contém apenas um fluxo funcional. O uso de
`partial` mantém o contrato exigido pelo SMAPI sem voltar a concentrar milhares
de linhas em um único arquivo.

## Módulos

| Arquivo | Responsabilidade |
| --- | --- |
| `ModEntry.cs` | Constantes, estado compartilhado, bootstrap e registro de eventos. |
| `ModEntry.LifecycleMultiplayer.cs` | Ciclo de save/sessão, input, ticks e mensagens multiplayer. |
| `ModEntry.Recruitment.cs` | Recrutar, dispensar, esperar, retomar e ownership. |
| `ModEntry.FollowingNavigation.cs` | Formação, trilha, pathfinding, recall, tiles seguros e recuperação. |
| `CompanionPathFindController.cs` | Controller sem o atalho de teleporte off-screen do pathfinding vanilla. |
| `CompanionBehaviorPatches.cs` | Guards Harmony para IA de pet, spouse, chegada em casa e bedtime. |
| `ModEntry.Tasks.cs` | Comandos, short-task queue e ações locais. |
| `ModEntry.Tasks.Planning.cs` | Seleção, previews, diretivas e reservas de trabalho. |
| `ModEntry.Tasks.Execution.cs` | Revalidação, pathing e commit das tarefas pendentes. |
| `ModEntry.Fishing.cs` | Uso da vara equipada, contexto de água, planejamento de margem, sessão, captura e visual de pesca. |
| `ModEntry.TaskDrops.cs` | Vínculo transitório entre árvore em queda, companion e lote de drops. |
| `CompanionTaskDropPatches.cs` | Prefix/postfix estreito no tick final da árvore rastreada. |
| `CompanionCareTasks.cs` | Harvest e cuidado de animais. |
| `ModEntry.ManagementUiBridge.cs` | Queries e comandos consumidos pelo HUD/painel. |
| `ModEntry.ActionWheel.cs` | Input por mouse/teclado/controle, paginação e composição das ações contextuais. |
| `ModEntry.ContextCommands.cs` | Classificação de recursos e atribuição contextual individual/em grupo. |
| `ModEntry.GroundCommands.cs` | Validação de chão vazio e ordem transitória de deslocar e esperar. |
| `ModEntry.WorkAreas.cs` | Ordens circulares persistentes de trabalho, preview, especialidade e conclusão. |
| `ModEntry.Routines.cs` | Edição CAS, executor horário, presets de área, conclusão e modo de agenda original. |
| `ModEntry.WorkAnimations.cs` | Movimento visual de ferramenta/mão e feedback cosmético de sucesso/falha. |
| `ModEntry.InventoryProgressionHud.cs` | Inventários, XP, loot e avisos. |
| `ModEntry.Equipment.cs` | Quatro slots owner/NPC, migração de ferramentas e troca CAS/atômica pela toolbar. |
| `ModEntry.ChestLogistics.cs` | UI de baú-destino, GUID, resolução entre mapas/interiores e depósito transacional. |
| `ModEntry.Hats.cs` | Troca, persistência materializada e desenho dos chapéus cosméticos. |
| `ModEntry.Communication.cs` | Fila priorizada, cooldown coletivo, histórico anti-repetição e expressões de pets. |
| `ModEntry.DialogueWorld.cs` | Seleção de perfis/condições de diálogo, ambiente e controle de agenda. |
| `ModEntry.Persistence.cs` | Normalização, migração e serialização do save. |
| `ModEntry.ConfigMenu.cs` | Integração com Generic Mod Config Menu e tradução. |
| `Core/CommandReplayGuard.cs` | Janela limitada de idempotência por jogador. |
| `Core/CompanionActionWheelHitTest.cs` | Hit-test puro de 1–6 setores, limites e separadores da roda. |
| `Core/CompanionWorkAreaPolicy.cs` | Geometria circular, raio 3–20, especialidades e validação do estado salvo. |
| `Core/CompanionProfilePolicy.cs` | Criação, migração e vínculo do perfil permanente de progressão por NPC. |
| `Core/CompanionEquipmentPolicy.cs` | Chave normalizada owner/NPC e limites de upgrade/água do regador. |
| `Core/CompanionRoutinePolicy.cs` | Grade 06–25, codec CAS, blocos contíguos, idempotência e presets normalizados. |
| `Core/CompanionDialoguePolicy.cs` | Contrato puro que separa fala de NPC e expressão silenciosa de pet. |
| `Core/CompanionDialogueSelectionPolicy.cs` | Seleção ponderada de falas elegíveis sem repetir o histórico recente. |
| `Core/CompanionDialogueScheduler.cs` | Fila limitada por owner, deduplicação, prioridade, TTL e cooldown coletivo. |
| `Core/RecruitmentContextPolicy.cs` | Regra pura de recrutamento no mesmo mapa, sem limite de distância. |
| `Core/FollowNavigationPolicy.cs` | Política pura de reset de recall, probes tardios e orçamento de rotas. |
| `Core/TaskNavigationPolicy.cs` | Reuso de stand e orçamento de criação/recuperação de rotas de tarefa. |
| `Core/TaskPlanningPolicy.cs` | Seleção prioritária e round-robin do orçamento de planejamento autônomo. |
| `Core/FishingWaterBodyPolicy.cs` | Descoberta cardinal estável do corpo de água, margens, profundidade e escolha da boia. |
| `Core/FishingSessionPolicy.cs` | Relógio, XP, alcance, qualidade e bônus das três skills de Pesca. |
| `Core/ContextCommandPolicy.cs` | Geometria pura do alcance de stands adjacentes para recursos contextuais. |
| `Core/GroundCommandPolicy.cs` | Regras puras de contexto e membros para comandos de chão vazio. |
| `Core/CompanionSkillTreePolicy.cs` | Estado puro compartilhado entre a árvore de habilidades e a validação autoritativa. |
| `Core/CompanionItemRoutingPolicy.cs` | Ordem pura dos destinos de item: companion, fallback configurado e mundo. |
| `Core/CompanionChestRoutingPolicy.cs` | Precedência individual/default, validade de GUID e rejeição de identidade ambígua. |
| `Core/SavedItemStackIdentity.cs` | Fingerprint determinístico de pilhas serializadas. |
| `Core/CompanionStateCopy.cs` | Cópias profundas para saves e snapshots imutáveis. |
| `CompanionOperationsModels.cs` | Perfis operacionais owner/NPC para ferramentas, rotina e logística individual. |
| `CompanionQuickHud.cs` | HUD de consulta e atalhos rápidos. |
| `CompanionActionWheel.cs` | Layout radial, páginas, foco espacial, hover e desenho das ações contextuais. |
| `CompanionPanelMenu*.cs` | Shell/input/layout, conteúdo das abas e helpers de desenho do menu completo. |

## Estado persistente e estado de runtime

`SavedModState`, `SquadMemberState`, `CompanionProfileState`,
`CompanionOperationalProfileState`, `NpcCosmeticState` e `SavedItemStack`
formam o contrato de save. O estado tem três escopos deliberadamente distintos:

- `CompanionProfileState`, indexado somente pelo nome interno do NPC, guarda XP,
  nível, pontos, skills e loot recente mesmo fora da equipe;
- `SquadMemberState` guarda apenas a associação ativa: owner, cargo, modo,
  posição/diretiva e área de trabalho;
- `CompanionOperationalProfileState`, indexado por owner + NPC, guarda as quatro
  ferramentas, a rotina e o override individual de baú.

Dispensar remove somente `SquadMemberState`: o perfil permanente continua e é
reatribuído ao próximo recrutamento, enquanto cargo/ownership nunca são
recriados a partir dele. A ordem de área fixa e as últimas chaves de diálogo
apresentadas fazem parte de `SquadMemberState`. Filas de trabalho, fila de
comunicação, animações,
controllers, trilhas, caches de reachability, targets de recall, previews e
notificações são transitórios e nunca devem ser tratados como uma fonte
persistente de verdade. `PendingNpcRestores` é a exceção intencional: ele guarda
apenas os dados mínimos necessários para devolver uma agenda vanilla depois de
um dismiss confirmado durante evento/festival.

As quatro ferramentas ficam em slots de `CompanionEquipmentState`, fora de
`SquadMemberState.Inventory`. Machado, picareta, regador e vara nunca passam por
cargo, retirar tudo ou roteamento de baú. A ordem de pesca, a margem/boia
escolhidas, a reserva, o controller e o horário da próxima captura pertencem
apenas a `PendingCompanionTask`: salvar ou recarregar nunca serializa uma sessão
de pesca pela metade.

Cada troca de slot e cada consumo de água também grava um checkpoint completo
dos equipamentos daquele owner em `Farmer.modData`. Esse checkpoint participa
do mesmo save vanilla que a toolbar e prevalece no próximo load quando o payload
separado do mod ficou para trás, mantendo os dois lados da transferência no
mesmo commit lógico. Ferramentas de schema antigo cujo provider está ausente
recebem uma marca interna owner/NPC no recovery; quando voltam a materializar,
são devolvidas somente ao inventário daquele owner e a marca não chega ao item.
Como o checkpoint é completo, um payload schema 13 sem key correspondente usa
um loadout vazio para aquele farmer; saves schema 12 ou anteriores ainda passam
primeiro pela migração de ferramentas legadas.

Separadamente, `CargoJournal` captura o cargo de todos os membros ativos, o
inventário compartilhado e o overflow bruto no `modData` do host imediatamente
antes de cada save. Essa cópia é a autoridade de ownership do commit vanilla e
é aplicada ao DTO bruto antes de migração e normalização. Entradas cujo member
ainda não existe no payload anterior são desviadas para recovery. Assim, a
mutação de um baú/farmer/mundo e a remoção ou remainder das lojas do mod não
divergem quando existe um checkpoint válido do mesmo commit vanilla.

`NpcCosmeticState` fica no nível superior do save, indexado pelo nome interno
do NPC. O slot de chapéu aparece na aba de inventário, mas não integra
`SquadMemberState.Inventory`: dispensar um companion transfere seus itens
carregados e remove o membro, sem tocar no cosmético. O renderer consulta esse
estado mesmo para NPCs fora da equipe, e snapshots replicam a mesma lista.
Enquanto equipado, o chapéu pertence ao NPC; o owner do recrutamento corrente
pode trocá-lo ou retirá-lo quando o NPC voltar à equipe.
Nos quatro ciclos vanilla de caminhada, o renderer mede uma vez a diferença da
primeira linha opaca estável entre o frame-base e o frame de passo da própria
textura e converte esse delta para o zoom/escala do mundo. Pixels isolados de
cabelo não mascaram o movimento do restante da cabeça. O cache por
textura/frame acompanha o bob real de villagers, crianças e NPCs customizados
sem inventar movimento para sprites estáticos.

Ferramentas, rotina e override individual de baú ficam em
`CompanionOperationalProfileState`, indexado por owner e nome interno do NPC;
o perfil continua existindo após dismiss, inclusive órfão, e nunca é herdado
por outro farmer que recrute o mesmo NPC. O default de baú fica em
`CompanionOwnerLogisticsState`. A referência persistida
guarda mapa/tile apenas como pista rápida e um GUID como identidade. O resolver
exige uma única ocorrência desse GUID entre locations e interiores; ausência ou
duplicidade mantém o item no fluxo de fallback.

A rotina normaliza exatamente vinte células, de 06:00 a 25:00. O executor usa
o início do bloco contíguo, o dia e a revisão como chave persistida: Follow,
Wait e agenda original são aplicados uma vez; trabalho concluído também grava a
conclusão, impedindo reativação no mesmo bloco após tick ou reload. Uma edição
da grade ativa o draft; o commit zera a execução, incrementa a revisão e
preserva os presets que pertencem ao host. Enquanto estiver ativa, a rotina
precede a autonomia configurada, embora uma diretiva manual explícita em
andamento ainda possa assumir o membro até terminar ou o próximo bloco começar.
Rotina sem repetição guarda o dia de ativação e é
desabilitada no próximo `DayStarted`.

Cada ordem manual de área atualiza somente o preset da sua especialidade. Ao
ativar um bloco de trabalho, o host revalida location, alvo, modo/toggle e tile
seguro antes de criar uma ordem `routine-*`; o raio materializado nunca supera
o máximo configurado no host, mesmo se o preset for mais antigo. Preset ausente mantém o bloco em
retry. A primeira tentativa reivindica dia/bloco/revisão, permitindo que um
override explícito termine sem ser apagado pelo refresh; área exaurida aplica
Follow, Wait ou agenda original. Quando o override acaba, o refresh compara o
modo durável e restaura o estado passivo/conclusão somente se ele divergir.
`OriginalRoutine` mantém o member recrutado e os dados
owner-scoped, mas o exclui de `CompanionBehaviorPatches` e dos locks periódicos
até outro bloco readquirir o controle.

`MovingToWait` também usa a fila transitória: a ordem reserva o tile, desloca o
NPC e só então grava `Mode = Waiting` e a posição realmente alcançada. Salvar no
meio do trajeto não serializa uma intenção incompleta; no reload, o companion
volta ao estado persistente verdadeiro de Following. Depois da chegada, porém,
o estado Waiting e sua posição são persistidos normalmente entre reloads e dias.

Uma área de trabalho é uma intenção persistente: `orderId`, mapa, centro, raio e
especialidade sobrevivem a save/reload. A tarefa e a reserva do alvo atual não
sobrevivem; elas são replanejadas no host dentro do mesmo círculo. Um Wait
explícito pausa a ordem sem apagá-la, enquanto Follow, Recall, outra ordem direta
ou conclusão limpa a área pelos fluxos autoritativos correspondentes.
O fluxo da roda escolhe especialidade e um/todos os companions. Não existe uma
etapa de raio: toda nova ordem usa automaticamente o máximo configurado e
replicado pelo host. Uma ordem já salva mantém seu círculo original até ser
substituída, embora continue sendo reduzida caso ultrapasse um novo máximo menor.
Um snapshot substitui o estado de gameplay do cliente, mas preserva previews e
animações cosméticas em andamento.

O baseline diário de posição, chave de agenda, comportamento de pet, atividade
de pátio e velocidade original é capturado somente depois que
`NPC.OnDayStarted`/`marriageDuties`
termina. Quando o SMAPI sinaliza `DayStarted` antes do fechamento das telas
noturnas, a captura e a retomada do controle ficam pendentes até o primeiro tick
seguro; tarefas e follow não rodam nesse intervalo.

Ao carregar um save, o mod:

1. limpa todas as coleções ligadas à sessão anterior;
2. normaliza níveis, skills, inventários, áreas fixas e histórico de diálogo;
3. rejeita entradas ambíguas/duplicadas antes de aceitar estado parcial;
4. remove activity/target transitórios sem uma fila correspondente;
5. preserva itens e membros de NPCs customizados temporariamente indisponíveis;
6. restaura apenas posições explicitamente salvas para `Waiting`/disconnect;
7. readquire o controle de agenda dos companions disponíveis.

O schema de save do ramo atual é `13`. `SavedItemStack` preserva `modData`,
ID qualificado, quantidade, qualidade, cor, parent preservado e, quando é
ferramenta, upgrade e água restante do regador. Saves com schema
mais novo ou dados ambíguos não são carregados nem sobrescritos; o mod entra em
modo inerte nessa sessão para não alterar mundo/itens sem estado confiável. O
schema de config é `8`.

## Autoridade multiplayer

Somente o host processa follow, pathfinding, filas, autonomia, agendas, XP,
inventários e mutações do mundo. Farmhands enviam `SquadActionMessage` com um
`CommandId`; o host deduplica, revalida ownership/alvo/ferramenta e publica um
`SavedModState` revisionado. Comandos de configuração enviam o estado desejado,
em vez de uma inversão dependente de timing. Retiradas unitárias carregam o
fingerprint completo da pilha. Resultados e rejeições voltam apenas ao jogador
solicitante.

Equipar chapéu também passa pelo host: o pedido identifica o slot real do
farmer, o fingerprint do item selecionado e o fingerprint do estado cosmético
mostrado pelo cliente. O host revalida owner, tipo `Hat`, conteúdo do slot e
chapéu anterior; uma troca substitui diretamente esse slot pelo chapéu antigo.
Assim, até inventário cheio mantém a operação atômica, sem duplicação ou perda,
e pedidos atrasados não alteram um cosmético mais novo.

`SetCompanionEquipment` também é autoritativo. O host revalida owner, índice,
fingerprint da célula selecionada, fingerprint do slot anterior, tipo exato,
pilha unitária e estado serializável sem attachment/encantamento. A ferramenta
anterior substitui diretamente a célula selecionada e a nova vai ao perfil
operacional; célula vazia faz o caminho inverso. Nenhuma delas passa por cargo
ou ganha uma rota genérica de depósito.

`SetWorkArea` também é autoritativo: o host deriva o raio máximo da própria
configuração e revalida owner, mapa, centro seguro, especialidade, modos
habilitados e membros elegíveis antes de persistir a ordem. Fala, emotes e
animações de trabalho são mensagens cosméticas do host para os clientes;
recebê-las nunca cria tarefa, altera o mundo ou muda o save.

`SetRoutine` envia a configuração inteira e o SHA-256 do estado/revisão que o
cliente leu. O host compara antes do commit, decodifica apenas grade/toggles/
conclusão, preserva presets e execução operacionais, e rejeita uma edição stale.
Assim duas telas nunca intercalam células nem sobrescrevem silenciosamente uma
rotina mais nova.

Snapshots são preparados fora do estado visível: todas as entradas são
validadas, clonadas, normalizadas e os itens são materializados antes do commit.
Uma exceção não apaga o último snapshot válido nem consome a revisão recebida.
`ReadSaveData`/`WriteSaveData` nunca são chamados por um farmhand.

Em split-screen local, o modelo autoritativo continua único e compartilhado no
computador host; eventos de lifecycle das telas secundárias não podem resetá-lo
nem aplicar snapshots. Cada tela registra o bloqueio de menu/evento do seu owner,
e feedback de comando é entregue à tela destinatária. Estado puramente visual do
quick HUD e a roda radial usam `PerScreen<T>`.
Comandos espaciais incluem o mapa de origem e pedidos adiados têm validade
limitada, evitando que coordenadas antigas atinjam outro mapa.

As regras de simulação do host são replicadas separadamente das preferências
locais de apresentação. Harvest de crop para owner remoto permanece desativado:
a API vanilla usa o jogador local implícito e creditaria o farmer errado.

Itens materializados pelo mod usam uma única rota autoritativa: baú-destino
efetivo, inventário do companion, inventário compartilhado quando habilitado
(ou owner quando não), e
por fim drop no mundo. Lumber e Mining continuam delegando a mutação à API
vanilla. Mining fotografa as referências de `Debris` antes da chamada síncrona;
uma árvore que entra em queda fica rastreada por identidade até o `tickUpdate`
exato que cria os drops do tronco. Em ambos os casos somente `OBJECT`/`RESOURCE`
novos e não essenciais são convertidos. Drops anteriores, chunks cosméticos,
arqueologia com side effects vanilla e debris com cardinalidade ambígua não são
capturados.

Uma captura de pesca chama diretamente o seletor estático e data-driven
`GameLocation.GetFishFromLocationData`, em vez do `getFish` virtual: overrides de
location podem consumir mail, quest ou recompensa única antes de devolver um
item que depois seria filtrado. O resultado aceita somente um objeto da categoria
peixe que não tenha tags lendária ou de família lendária. Cada captura segue a mesma
rota de inventário/fallback/drop e concede 8 XP ao companion; o bônus da terceira
skill é uma segunda captura completa. A árvore passa a ter quatro ramos de três
nós: Pesca encadeia intervalo menor, alcance/qualidade maior e chance de captura
extra.

Diálogo resolve primeiro a categoria elegível do perfil exato, depois arquétipo
e `Generic`. Linhas `Overlay` de perfis inferiores podem acrescentar contexto
compartilhado (estação, clima e amizade) a uma categoria exata; em empate de
especificidade, a fala própria do NPC vence. Fora desse tier de overlays, a
ordem de profiles é estrita antes da especificidade, então `Generic` nunca salta
um fallback anterior. A fila então remove repetições recentes, aplica
peso/prioridade/TTL e respeita um cooldown coletivo por owner.
Pets passam pelo mesmo scheduler, mas `CompanionDialoguePolicy` converte a saída
em expressão sem permitir texto.

## Invariantes de comportamento

- Um NPC só pode ter um owner.
- Um owner não pode ultrapassar `MaxSquadSize`.
- Todo target e todo tile de trabalho ocupado são reservados por um único companion.
- Um target contextual pode ser compartilhado apenas pelos membros do mesmo
  cohort explícito; cada membro ainda exige um stand tile exclusivo.
- Uma área fixa contém apenas targets cuja distância euclidiana ao centro cabe
  no círculo inclusivo de raio 3–20. O stand adjacente pode usar padding de um
  tile, mas o recurso nunca pode ficar fora da área.
- Madeira aceita somente Lumbering, Mineração somente Mining, Regar somente
  Watering e Limpar área aceita madeira e mineração. Alvo bloqueado ou reservado pausa o planejamento; somente a
  ausência real de recursos compatíveis conclui a ordem e deixa o NPC Waiting.
- A área permanece ancorada ao mapa/centro mesmo quando o owner troca de mapa;
  o companion não volta à formação enquanto a ordem estiver ativa.
- A atribuição contextual prepara target e stands antes do commit; uma falha de
  preparação não cancela a tarefa anterior do companion.
- Uma ordem de chão vazio prepara e reserva o destino antes de substituir a
  ordem anterior, revalida owner/mapa/segurança/reachability no host e segue a
  sequência transitória `MovingToWait -> Waiting`; nunca teleporta para concluir.
- O raio de formação não limita comandos de chão: o destino e o companion podem
  estar mais de três tiles do owner, mas precisam estar no mesmo mapa. Uma busca
  A* direcionada rejeita desconexão definitiva sem inundar o mapa; se o limite
  terminar inconclusivo, o controller real tenta a rota e o fluxo falha em
  Waiting no ponto alcançado, sem teleporte.
- `F8` não cancela `MovingToWait`. Falha, timeout, troca de mapa ou bloqueio do
  destino libera a reserva e deixa o NPC esperando na posição em que parou.
- O contexto de pesca exige `canFishHere`, água real e `isTileFishable`, rejeitando
  `NoFishing`, água coberta pela camada Buildings e fish ponds construídos. Depois
  começa no componente cardinal completo da água clicada.
  Cada worker com vara recebe o stand seguro e alcançável de menor distância de
  caminho naquele mesmo corpo, com margens distintas reservadas; bloqueio pode
  replanejar outra margem, mas nunca atravessar para um lago desconectado.
- Pesca dirigida ignora o `FishingMode` configurado e pode ser enviada mesmo com
  o toggle global já desligado. Desligar tarefas com `F8` durante uma sessão é
  tratado como novo comando e a cancela; fora isso, ela continua entre capturas
  até 26:00/`DayEnding` ou até outro comando substituí-la. Perda da vara, água ou
  mapa cancela com segurança, libera rota/reserva e nunca teleporta o NPC.
- Tarefas contextuais guardam a instância runtime do recurso e a revalidam até o
  commit no mundo, impedindo agir sobre um substituto no mesmo tile.
- Destinos de formação são únicos dentro de cada atualização de follow.
- Uma ação revalida mapa, owner, target, distância do stand adjacente e
  segurança imediatamente antes de alterar o mundo.
- Controllers de follow não substituem uma tarefa pendente.
- Planejamento de trabalho usa uma única busca direcionada aos stands viáveis;
  até três companions são planejados por scan em round-robin, com prioridade
  para Work explícito. Execução normal reutiliza o stand reservado sem BFS
  periódica. No máximo duas novas rotas de tarefa são construídas por
  atualização de processamento.
- Recall cancela tarefas/diretivas antes de iniciar o retorno.
- O input de Follow/Recall apenas confirma a mudança de estado; seleção de tile
  e criação de rota acontecem no próximo tick autoritativo do host.
- Formação e trilha não fazem varredura completa de conectividade. Um probe
  direcionado só ocorre após falta real de progresso, respeita cooldown e um
  resultado inconclusivo nunca autoriza reposicionamento.
- Um endpoint rejeitado pelo pathfinder vanilla entra em backoff temporário; a
  formação tenta outro tile sem confundir essa falha de alvo com desconexão do mapa.
- Follow comum nunca reposiciona um NPC no mesmo mapa. Um fallback conservador
  para um tile do componente alcançável do owner só é permitido por Recall
  explícito ou na transferência necessária entre mapas.
- Itens nunca são apagados só porque o content pack que os criou está ausente.
- Um resultado de tarefa materializado tenta baú-destino, cargo, squad/owner e
  mundo nessa ordem; cada destino recebe somente o remainder ainda não commitado.
- Ferramentas entram apenas nos quatro slots operacionais por troca direta com
  a célula selecionada da toolbar; nenhuma ferramenta ou item genérico pode ser
  depositado no cargo do companion.
- Transferências de inventário são commitadas por pilha; se conteúdo customizado
  lançar após mutação parcial, a fonte é decrementada antes de qualquer fallback.
- Um NPC nunca pode existir simultaneamente como member ativo e restore de agenda
  pendente.
- Aquisição de controle de agenda é idempotente; a limpeza pesada não pertence
  ao tick de navegação.
- Opções ainda sem implementação continuam legíveis em configs antigas, mas
  não são anunciadas no GMCM como funcionalidades prontas.
- A roda preserva até três ações globais e pagina os demais slots até expor os
  12 companions. Mouse/scroll, setas/WASD, D-pad/sticks, shoulders, A/Enter e
  B/Escape compartilham o mesmo modelo de seleção e cancelamento modal.
- A comunicação usa uma fila limitada por owner: deduplicação, prioridade e TTL
  acontecem antes do cooldown coletivo. Falas recentes são evitadas no grupo e
  por speaker, e as quatro chaves recentes por NPC sobrevivem ao reload.
- Pets nunca recebem texto acima da cabeça. As mesmas intenções viram
  emote/som/pulo ou tremor, e o host replica exatamente uma expressão visual aos
  clientes. Perfis de NPC têm precedência sobre `All_Villager`/`Generic` e suas
  condições podem combinar amizade, tempo, estação, clima, local, tarefa,
  resultado, falha e item.

## Como adicionar uma tarefa

1. Adicione um `CompanionTaskKind` estável; não reordene valores existentes sem
   uma migração.
2. Separe seleção/validação de target, deslocamento e commit da mutação.
3. Registre uma reserva se o target puder ser disputado.
4. Revalide o target no commit e sempre libere a reserva em cancelamento,
   timeout, troca de mapa e conclusão.
5. Use o `Farmer` dono da tarefa e o `GameLocation` gravado nela; nunca suponha
   que `Game1.player` ou `Game1.currentLocation` representam o owner.
6. Adicione chaves correspondentes em `i18n/default.json` e `i18n/pt-BR.json`.
7. Inclua cenários manuais em `docs/MANUAL_QA.md`.

## Compatibilidade

Não altere `UniqueID`, `SaveKey`, asset key, IDs de skill ou nomes serializados
sem uma migração explícita. Toda chamada de Stardew/SMAPI deve permanecer na
game thread. Mudanças em NPC, mapa, inventário ou save precisam passar pela
autoridade do host. O multiplayer continua marcado como experimental até a
conclusão do QA manual cooperativo, apesar de comandos e snapshots já serem
autoritativos.

## Validação automatizada

`scripts/validate.sh` restaura e compila os projetos, executa a suíte do runner
sem dependências em `tests/PelicanCompanions.Tests`, valida o JSON e exige
paridade de chaves/tokens entre inglês e português. O runner cobre contratos
puros, inclusive roda paginada, agenda de diálogo, política de áreas, corpo de
água e regras da sessão de pesca; animação, NPC, mapa e multiplayer ainda exigem
`MANUAL_QA.md`.
