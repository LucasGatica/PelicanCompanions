# Arquitetura do Pelican Companions

Este documento descreve a organização mantida na versão 1.5.0. A regra
principal é simples: `ModEntry.cs` compõe o mod e registra integrações; cada
arquivo parcial contém apenas um fluxo funcional. O uso de `partial` mantém o
contrato exigido pelo SMAPI sem voltar a concentrar milhares de linhas em um
único arquivo.

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
| `ModEntry.ActionWheel.cs` | Input, hit-test visual de NPC e composição das ações contextuais. |
| `ModEntry.ContextCommands.cs` | Classificação de recursos e atribuição contextual individual/em grupo. |
| `ModEntry.GroundCommands.cs` | Validação de chão vazio e ordem transitória de deslocar e esperar. |
| `ModEntry.InventoryProgressionHud.cs` | Inventários, XP, loot e avisos. |
| `ModEntry.DialogueWorld.cs` | Diálogo, condições do mundo e controle de agenda. |
| `ModEntry.Persistence.cs` | Normalização, migração e serialização do save. |
| `ModEntry.ConfigMenu.cs` | Integração com Generic Mod Config Menu e tradução. |
| `Core/CommandReplayGuard.cs` | Janela limitada de idempotência por jogador. |
| `Core/CompanionActionWheelHitTest.cs` | Hit-test puro de 1–6 setores, limites e separadores da roda. |
| `Core/SavedItemStackIdentity.cs` | Fingerprint determinístico de pilhas serializadas. |
| `Core/CompanionStateCopy.cs` | Cópias profundas para saves e snapshots imutáveis. |
| `CompanionQuickHud.cs` | HUD de consulta e atalhos rápidos. |
| `CompanionActionWheel.cs` | Layout radial variável, hover e desenho das ações contextuais. |
| `CompanionPanelMenu*.cs` | Shell/input/layout, conteúdo das abas e helpers de desenho do menu completo. |

## Estado persistente e estado de runtime

`SavedModState`, `SquadMemberState` e `SavedItemStack` formam o contrato de
save. Filas de trabalho, controllers, trilhas, caches de reachability, targets
de recall e notificações são transitórios e nunca devem ser tratados como uma
fonte persistente de verdade. `PendingNpcRestores` é a exceção intencional: ele
guarda apenas os dados mínimos necessários para devolver uma agenda vanilla
depois de um dismiss confirmado durante evento/festival.

`MovingToWait` também usa a fila transitória: a ordem reserva o tile, desloca o
NPC e só então grava `Mode = Waiting` e a posição realmente alcançada. Salvar no
meio do trajeto não serializa uma intenção incompleta; no reload, o companion
volta ao estado persistente verdadeiro de Following. Depois da chegada, porém,
o estado Waiting e sua posição são persistidos normalmente entre reloads e dias.

O baseline diário de posição, chave de agenda, comportamento de pet, atividade
de pátio e velocidade original é capturado somente depois que
`NPC.OnDayStarted`/`marriageDuties`
termina. Quando o SMAPI sinaliza `DayStarted` antes do fechamento das telas
noturnas, a captura e a retomada do controle ficam pendentes até o primeiro tick
seguro; tarefas e follow não rodam nesse intervalo.

Ao carregar um save, o mod:

1. limpa todas as coleções ligadas à sessão anterior;
2. normaliza níveis, skills e inventários;
3. rejeita entradas ambíguas/duplicadas antes de aceitar estado parcial;
4. remove activity/target transitórios sem uma fila correspondente;
5. preserva itens e membros de NPCs customizados temporariamente indisponíveis;
6. restaura apenas posições explicitamente salvas para `Waiting`/disconnect;
7. readquire o controle de agenda dos companions disponíveis.

O schema de save da versão 1.5.0 é `8`. `SavedItemStack` preserva `modData`,
ID qualificado, quantidade, qualidade, cor e parent preservado. Saves com schema
mais novo ou dados ambíguos não são carregados nem sobrescritos; o mod entra em
modo inerte nessa sessão para não alterar mundo/itens sem estado confiável. O
schema de config é `6`.

## Autoridade multiplayer

Somente o host processa follow, pathfinding, filas, autonomia, agendas, XP,
inventários e mutações do mundo. Farmhands enviam `SquadActionMessage` com um
`CommandId`; o host deduplica, revalida ownership/alvo/ferramenta e publica um
`SavedModState` revisionado. Comandos de configuração enviam o estado desejado,
em vez de uma inversão dependente de timing. Retiradas unitárias carregam o
fingerprint completo da pilha. Resultados e rejeições voltam apenas ao jogador
solicitante.

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

## Invariantes de comportamento

- Um NPC só pode ter um owner.
- Um owner não pode ultrapassar `MaxSquadSize`.
- Todo target e todo tile de trabalho ocupado são reservados por um único companion.
- Um target contextual pode ser compartilhado apenas pelos membros do mesmo
  cohort explícito; cada membro ainda exige um stand tile exclusivo.
- A atribuição contextual prepara target e stands antes do commit; uma falha de
  preparação não cancela a tarefa anterior do companion.
- Uma ordem de chão vazio prepara e reserva o destino antes de substituir a
  ordem anterior, revalida owner/mapa/distância/segurança no host e segue a
  sequência transitória `MovingToWait -> Waiting`; nunca teleporta para concluir.
- `F8` não cancela `MovingToWait`. Falha, timeout, troca de mapa ou bloqueio do
  destino libera a reserva e deixa o NPC esperando na posição em que parou.
- Tarefas contextuais guardam a instância runtime do recurso e a revalidam até o
  commit no mundo, impedindo agir sobre um substituto no mesmo tile.
- Destinos de formação são únicos dentro de cada atualização de follow.
- Uma ação revalida mapa, owner, target, distância e segurança imediatamente
  antes de alterar o mundo.
- Controllers de follow não substituem uma tarefa pendente.
- Recall cancela tarefas/diretivas antes de iniciar o retorno.
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

`scripts/validate.sh` restaura e compila os projetos, executa os 18 testes do
runner sem dependências em `tests/PelicanCompanions.Tests`, valida o JSON e exige
paridade de chaves/tokens entre inglês e português. O runner cobre contratos
puros; comportamento de NPC, mapa e multiplayer ainda exige `MANUAL_QA.md`.
