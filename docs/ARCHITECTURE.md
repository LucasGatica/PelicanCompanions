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
| `CompanionCareTasks.cs` | Harvest e cuidado de animais. |
| `ModEntry.ManagementUiBridge.cs` | Queries e comandos consumidos pelo HUD/painel. |
| `ModEntry.ActionWheel.cs` | Input por mouse/teclado/controle, paginação e composição das ações contextuais. |
| `ModEntry.ContextCommands.cs` | Classificação de recursos e atribuição contextual individual/em grupo. |
| `ModEntry.GroundCommands.cs` | Validação de chão vazio e ordem transitória de deslocar e esperar. |
| `ModEntry.WorkAreas.cs` | Ordens circulares persistentes de trabalho, preview, especialidade e conclusão. |
| `ModEntry.WorkAnimations.cs` | Movimento visual de ferramenta/mão e feedback cosmético de sucesso/falha. |
| `ModEntry.InventoryProgressionHud.cs` | Inventários, XP, loot e avisos. |
| `ModEntry.Communication.cs` | Fila priorizada, cooldown coletivo, histórico anti-repetição e expressões de pets. |
| `ModEntry.DialogueWorld.cs` | Seleção de perfis/condições de diálogo, ambiente e controle de agenda. |
| `ModEntry.Persistence.cs` | Normalização, migração e serialização do save. |
| `ModEntry.ConfigMenu.cs` | Integração com Generic Mod Config Menu e tradução. |
| `Core/CommandReplayGuard.cs` | Janela limitada de idempotência por jogador. |
| `Core/CompanionActionWheelHitTest.cs` | Hit-test puro de 1–6 setores, limites e separadores da roda. |
| `Core/CompanionWorkAreaPolicy.cs` | Geometria circular, raio 3–20, especialidades e validação do estado salvo. |
| `Core/CompanionDialoguePolicy.cs` | Contrato puro que separa fala de NPC e expressão silenciosa de pet. |
| `Core/CompanionDialogueSelectionPolicy.cs` | Seleção ponderada de falas elegíveis sem repetir o histórico recente. |
| `Core/CompanionDialogueScheduler.cs` | Fila limitada por owner, deduplicação, prioridade, TTL e cooldown coletivo. |
| `Core/RecruitmentContextPolicy.cs` | Regra pura de recrutamento no mesmo mapa, sem limite de distância. |
| `Core/FollowNavigationPolicy.cs` | Política pura de reset de recall, probes tardios e orçamento de rotas. |
| `Core/TaskNavigationPolicy.cs` | Reuso de stand e orçamento de criação/recuperação de rotas de tarefa. |
| `Core/TaskPlanningPolicy.cs` | Seleção prioritária e round-robin do orçamento de planejamento autônomo. |
| `Core/GroundCommandPolicy.cs` | Regras puras de contexto e membros para comandos de chão vazio. |
| `Core/CompanionSkillTreePolicy.cs` | Estado puro compartilhado entre a árvore de habilidades e a validação autoritativa. |
| `Core/SavedItemStackIdentity.cs` | Fingerprint determinístico de pilhas serializadas. |
| `Core/CompanionStateCopy.cs` | Cópias profundas para saves e snapshots imutáveis. |
| `CompanionQuickHud.cs` | HUD de consulta e atalhos rápidos. |
| `CompanionActionWheel.cs` | Layout radial, páginas, foco espacial, hover e desenho das ações contextuais. |
| `CompanionPanelMenu*.cs` | Shell/input/layout, conteúdo das abas e helpers de desenho do menu completo. |

## Estado persistente e estado de runtime

`SavedModState`, `SquadMemberState` e `SavedItemStack` formam o contrato de
save. A ordem de área fixa e as últimas chaves de diálogo apresentadas fazem
parte de `SquadMemberState`. Filas de trabalho, fila de comunicação, animações,
controllers, trilhas, caches de reachability, targets de recall, previews e
notificações são transitórios e nunca devem ser tratados como uma fonte
persistente de verdade. `PendingNpcRestores` é a exceção intencional: ele guarda
apenas os dados mínimos necessários para devolver uma agenda vanilla depois de
um dismiss confirmado durante evento/festival.

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
O fluxo da roda escolhe especialidade, um preset de raio limitado pelo máximo
replicado do host e um/todos os companions. Um snapshot substitui o estado de
gameplay do cliente, mas preserva previews e animações cosméticas em andamento.

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

O schema de save do ramo atual é `9`. `SavedItemStack` preserva `modData`,
ID qualificado, quantidade, qualidade, cor e parent preservado. Saves com schema
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

`SetWorkArea` também é autoritativo: o host revalida owner, mapa, centro seguro,
raio, especialidade, modos habilitados e membros elegíveis antes de persistir a
ordem. Fala, emotes e animações de trabalho são mensagens cosméticas do host
para os clientes; recebê-las nunca cria tarefa, altera o mundo ou muda o save.

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
- Madeira aceita somente Lumbering, Mineração somente Mining e Limpar área
  aceita ambos. Alvo bloqueado ou reservado pausa o planejamento; somente a
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
- Tarefas contextuais guardam a instância runtime do recurso e a revalidam até o
  commit no mundo, impedindo agir sobre um substituto no mesmo tile.
- Destinos de formação são únicos dentro de cada atualização de follow.
- Uma ação revalida mapa, owner, target, distância e segurança imediatamente
  antes de alterar o mundo.
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
puros, inclusive roda paginada, agenda de diálogo e política de áreas; animação,
NPC, mapa e multiplayer ainda exigem `MANUAL_QA.md`.
