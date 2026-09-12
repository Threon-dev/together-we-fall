# Структура

> Дерево `Assets/_Project/` з поясненням, що де лежить.

```
Assets/_Project/
├── Code/
│   ├── Bootstrap/GameBootstrap.cs        єдина точка Initialize(), без static і DI
│   ├── Config/                           ScriptableObject-конфіги: Enemy,
│   │                                     Spawn, Pathfinding, Separation,
│   │                                     DungeonGeneration, Audio, Vfx, Loot,
│   │                                     LootTable, Item, Character,
│   │                                     SkillDefinition, SkillModifier,
│   │                                     StatusEffect, ElementReactionRule/Table
│   ├── Shared/                           PlayerPositions*, SimulationSettings*
│   ├── Player/                           InputReader, Motor, MoveIntent,
│   │   │                                 PositionPublisher, ActionPublisher
│   │   ├── CharacterPortrait.cs          камера-дитина гравця → RenderTexture
│   │   ├── Components/PlayerCharacter.cs сутність гравця в ECS
│   │   ├── Components/PlayerResources.cs Mana — Current і нічого більше
│   │   └── Systems/                      PlayerCharacterRegistry,
│   │                                     PlayerResource (веде пули за листом)
│   ├── CameraRig/TopDownCameraRig.cs     fixed yaw 45°, pitch 55°, зум
│   ├── Enemies/{Components,Authoring,Systems}/
│   ├── Spawning/{Components,Authoring,Systems}/
│   ├── Interaction/                      запит на дію → результат дії
│   │   ├── Components/InteractionComponents.cs
│   │   └── Systems/                      Registry, Resolve
│   ├── Loot/
│   │   ├── Components/                   LootComponents, LootBlobs
│   │   ├── Authoring/                    Chest, LootItem, LootDatabase
│   │   └── Systems/                      ChestInteraction, LootTable, ItemPickup
│   ├── Inventory/                        grid-сітка: контейнери й розміщення
│   │   ├── Components/                   InventoryGrid/Cell, ItemGridPlacement,
│   │   │                                 ItemStored, Placement Request/Result
│   │   ├── GridFit.cs                    уся математика прямокутника в сітці
│   │   └── Systems/InventoryPlacementSystem.cs
│   ├── Equipment/
│   │   ├── EquipmentSlots.cs             маска дозволених слотів, дві руки
│   │   ├── Keystones.cs                  KeystoneEffect/Component/Set —
│   │   │                                 правила, які предмет вивертає
│   │   ├── Components/                   Stat*, Equipment*, Socket*, ItemDatabaseBlob
│   │   ├── Authoring/CharacterStatsAuthoring.cs
│   │   └── Systems/                      Equipment, PlayerStats, Socket
│   ├── Combat/
│   │   ├── StatusEffects.cs              ЄДИНЕ місце, де тип статусу щось означає
│   │   ├── Components/DamageComponents.cs  Health, DamageEvent, Dead, DeathFade
│   │   ├── Components/ElementComponents.cs ElementMask
│   │   ├── Components/StatusComponents.cs  ActiveStatusEffect, StatusGate,
│   │   │                                 CrowdControlImmunity/Resistance
│   │   ├── Components/StatusPresentationComponents.cs  StatusVisual
│   │   ├── Components/ElementReactionBlob.cs  таблиця правил і статусів
│   │   ├── Authoring/ElementReactionAuthoring.cs  бейк таблиці реакцій
│   │   ├── Components/DamageMeterComponents.cs  лог ударів по манекену
│   │   └── Systems/                      DamageResolution, DeathReaction, DeathFade,
│   │                                     ElementReaction, StatusTick, DamageMeter
│   │                                     (перші дві анонсують ще й TriggerEvent)
│   ├── Audio/
│   │   ├── Components/AudioComponents.cs AudioCue, AudioEvent — шов до звуку
│   │   ├── AudioSourcePool.cs            фіксований пул AudioSource
│   │   ├── AudioPresenter.cs             читає чергу, три бюджети, грає
│   │   └── Systems/AudioEventRegistrySystem.cs
│   ├── Curtain/
│   │   ├── Components/CurtainComponents.cs  CurtainState, CurtainRequest
│   │   ├── CurtainPresenter.cs           uGUI-прямокутник поверх усього
│   │   └── Systems/CurtainSystem.cs      веде непрозорість
│   ├── Vfx/
│   │   ├── Components/VfxComponents.cs   VfxEvent — єдиний шов до презентації
│   │   ├── Systems/                      VfxEventRegistry, DamageNumber, StatusTint
│   │   ├── StatusIconPool.cs             пул гліфів статусів над тілами
│   │   ├── VfxPresenter.cs               міст-презентер, лише читає ECS
│   │   ├── VfxLinePool.cs                пул LineRenderer: лінії й кільця
│   │   ├── DamageNumberPool.cs           пул TMP-лейблів на канвасі
│   │   └── HitStopController.cs          єдине місце, що чіпає Time.timeScale
│   ├── Skills/
│   │   ├── Components/                   Skill*, ProjectileSpawn, SkillDatabaseBlob,
│   │   │                                 ElementZone (+ ZoneSpawn, пул зон),
│   │   │                                 TriggerComponents (подія + кулдаун)
│   │   ├── Authoring/                    SkillDatabase, SkillProjectile, ElementZone
│   │   ├── EnemyTargets.cs               пошук цілей, спільний для стадій
│   │   ├── GemSockets.cs                 усе, що читає отвори й геми
│   │   ├── SkillModifiers.cs             фаза супорта, виведена з виду
│   │   ├── SkillConditions.cs            умова супорта — одне місце, де вона так/ні
│   │   └── Systems/                      Registry, Loadout, Cast, Projectile,
│   │                                     Area, Hit, ZonePool, ElementZone,
│   │                                     ProjectileZoneOverlap, TriggerEvaluation,
│   │                                     StarterKit
│   ├── Lobby/
│   │   ├── Currency.cs                   гроші: гаманець, оплата, збір монети
│   │   ├── SceneLoadBridge.cs            єдиний, хто кличе LoadScene
│   │   ├── Components/                   NpcService, Vendor*, Crafting*,
│   │   │                                 DungeonPortal, SceneTransition
│   │   ├── Authoring/LobbyNpcAuthoring.cs один бейкер на всі чотири види NPC
│   │   └── Systems/                      NpcInteraction, VendorStock,
│   │                                     VendorTransaction, Crafting,
│   │                                     DungeonPortal
│   ├── UI/Ugui.cs                        хелпер uGUI: Place, Box, Text, спрайти
│   ├── UI/InventoryUI.cs                 uGUI, читає ECS, пише запит
│   ├── UI/LobbyUI.cs                     промпт, крамниця, кузня, портал, метр
│   ├── UI/PlayerHud.cs                   uGUI: колби й бар скілів, лише читає ECS
│   ├── Dungeon/
│   │   ├── DungeonDirector.cs            генерація → геометрія → ECS → бейк navmesh
│   │   ├── DungeonLayoutPublisher.cs     міст GO↔ECS, лише копіює дані
│   │   ├── Generation/                   Layout, Settings, Generator (BSP), RoomTypeAssigner
│   │   ├── World/                        GeometryBuilder, NavMeshBaker, MaterialSet
│   │   ├── Components/DungeonComponents.cs
│   │   └── Systems/                      SpawnPoint, RoomOccupancy, RoomActivation,
│   │                                     DungeonChest
│   ├── Debug/                            DebugHud (OnGUI), DebugSpawnTrigger,
│   │                                     DebugRunStatusProbe, TrainingDummy*
│   └── Editor/
│       ├── SceneBuildUtility.cs          спільне для всіх білдерів сцен
│       ├── SkillContentFactory.cs        стартові скіли, супорти, keystone-персні
│       ├── BuildLibraryFactory.cs        бібліотека скілів і гемів, Riftwood
│       │                                 Staff, тестбед-пара, Grant Test Kit
│       ├── ElementContentFactory.cs      статуси, правила реакцій, гем зони
│       ├── ItemContentFactory.cs         предмети й таблиця луту — спільні
│       │                                 для всіх сцен
│       ├── LobbyContentFactory.cs        NPC, товар торговця, манекени
│       ├── ArenaSceneBuilder.cs          арена для замірів
│       ├── DungeonSceneBuilder.cs        сцена данжу (порожня, поверх — у рантаймі)
│       └── LobbySceneBuilder.cs          хаб: NPC, манекени, портал
├── Data/                                 Enemy/Spawn/Pathfinding/Separation/
│   │                                     DungeonGeneration/Loot/Character/
│   │                                     StarterKit/TreasureLootTable
│   ├── Items/                            спорядження, монета, геми (активні
│   │                                     й супорти), keystone-персні,
│   │                                     Riftwood Staff на 14 отворів
│   │                                     і тестбед-пара з вільними головами
│   ├── Elements/                         StatusEffectDefinition (CC, DoT,
│   │                                     дебафи), правила реакцій
│   │                                     і ElementReactionTable
│   ├── Skills/                           SkillDefinition (разом із двома
│   │                                     вшитими атаками зброї) і SkillModifier
│   ├── Sets/                             ItemSetDefinition — членство сету
│   │                                     й бонуси за порогами
│   └── Vfx/                              SkillVfxSet — cast / projectile / hit
│                                         на скіл
├── UI/                                   PanelSettings + RuntimeTheme.tss (від
│                                          UI Toolkit; ні на що не посилаються)
├── Prefabs/                              Enemy, Chest, LootItem, SkillProjectile,
│                                         ElementZone
└── Scenes/
    ├── Lobby.unity                       хаб: NPC, манекени, портал
    ├── Lobby/SubScene.unity              бейкінг: NPC, манекени, бази
    ├── Arena.unity                       стенд для замірів
    ├── Arena/SubScene.unity              бейкінг: SpawnPoints, WaveSpawner, SimulationSettings
    ├── Arena_NavMesh.asset               забейканий navmesh (тільки для арени)
    ├── Dungeon.unity                     процедурний поверх
    └── Dungeon/SubScene.unity            бейкінг: WaveSpawner, SimulationSettings
```
