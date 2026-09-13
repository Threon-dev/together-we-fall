# Карта систем

> Індекс, не опис логіки: куди йти за конкретною системою.
> Деталі — `docs/features.md` (що працює), `docs/pipeline.md` (порядок систем),
> `docs/structure.md` (дерево файлів).
>
> Шляхи — від кореня репо. Префікс `Code/` = `Assets/_Project/Code/`,
> `Data/` = `Assets/_Project/Data/`.

## Bootstrap / вхід у сцену
- Код: `Code/Bootstrap/GameBootstrap.cs` — єдина точка `Initialize()`; усі презентери
  й UI — `[SerializeField]`, більшість опційні.
- Збірка сцен: `Code/Editor/{Lobby,Dungeon,Arena}SceneBuilder.cs` + спільне
  `Code/Editor/SceneBuildUtility.cs`.

## Гравець: ввід і рух
- Код: `Code/Player/PlayerInputReader.cs` (New Input System), `PlayerMotor.cs`,
  `PlayerMoveIntent.cs`, `PlayerPositionPublisher.cs`, `PlayerActionPublisher.cs`.
- ECS: `Code/Player/Components/PlayerCharacter.cs`,
  `Code/Player/Systems/PlayerCharacterRegistrySystem.cs`.
- Зв'язок: `PlayerActionPublisher` — єдине місце, що перетворює ввід у
  `InteractionRequest` і `SkillCastRequest`; він же питає UI, чи той не забрав клік
  (делегат приходить з `GameBootstrap`).
- Позиції для ECS: `Code/Shared/PlayerPositions.cs` +
  `Code/Shared/PlayerPositionRegistrySystem.cs` (`PlayerPositionsSingleton`).
- Модель і анімації: `Code/Player/PlayerAnimationPresenter.cs` — лише читає:
  швидкість `CharacterController` у локальному просторі → `MoveX`/`MoveZ`;
  з сутності персонажа (за `PlayerId` з `GameBootstrap`) — `CastCue.Count` виріс →
  `Cast` + `CastKind` (= `SkillEffectKind`), `Health.Current` впав при тому ж
  `Max` → `Hit`, `Dead` увімкнений → `Dead`. `CastCue` пише `SkillCastSystem.Cast`
  там, де списує кулдаун (тригерні касти не рахуються).
  Модель — Fina (`Assets/smoky_fox/FinaAnimeGirl`), кліпи — Unarmed з
  `Assets/ExplosiveLLC/RPG Character Mecanim Animation Pack`. Збирає
  `Code/Editor/CharacterContentFactory.cs`: `Art/Characters/PlayerLocomotion.controller`
  (**перезбирається на місці при кожному білді сцени**, GUID той самий) —
  Base: 1D по `WeaponBlend`: Relax (−1, поза боєм — `_relaxDelay` без кастів і
  отриманих ударів, зброя не в руці) / Unarmed / Armed / 2Hand-Sword / 2Hand-Bow;
  сусідні стійки презентер згладжує, дальні — стрибком під draw/sheath,
  у кожній 2D freeform (idle, strafe на 0.5, run на 1) + Death з Any State;
  Upper Body (маска з паку): каст start→end по `CastKind`+`Weapon`, свінги по
  `AttackIndex` (4 на стійку: парні б'ють праворуч, непарні ліворуч — напрям
  кліпу фабрика міряє з кривої `RightHandT.x` і пише в лог), Hit, Draw/Sheath по `Grip`.
  **Сторону свінгу вирішує хост**: `CastCue.Swings` → `CastCue.SweepsRight` →
  `CastContext`/`DelayedStrike` → `VfxEvent.SweepRight`; `VfxPresenter`
  віддзеркалює cast-ефект (−X scale), коли це не збігається з `SkillVfxSet._sweepsRight`.
- Зброя на тілі: той самий `PlayerAnimationPresenter` читає `EquippedItem[MainHand]`
  → модель зі списку `_weaponModels` (пише `SceneBuildUtility.WriteWeaponModels` з
  кожного `ItemDefinition._model`). Надягнута — на спині (`Chest`); каст із піхов
  грає Draw замість свінгу й за `_drawSwapDelay` переносить у `RightHand`;
  `_sheathDelay` без кастів — назад. Хват рахується з кісток пальців, не з офсетів.
  Drawn/sheathed — **лише презентація**, у симуляцію не йде.
- Мечі: `Code/Editor/WeaponContentFactory.cs` (викликає
  `ItemContentFactory.CreateOrLoadTreasureTable` на кожному білді, адитивно) —
  предмети в `Data/Items/Weapons/Swords/`, grip-префаби в `Art/Weapons/`
  (руків'я в нулі, лезо +Z, ширина +Y; міряється з меша, пишеться один раз),
  URP-матеріали в `Art/Weapons/Materials/`.
- Луки: той самий `WeaponContentFactory.CreateBows` — пак `Dimasjk Studio/.../Bows & Quivers
  Part 1` (лук + тятива в один grip-префаб, колчан, стріла `Arrow_01`); предмети в
  `Data/Items/Weapons/Bows/` (`HuntersBow` отримує лише тіло/колчан/іконку,
  `ElderwoodBow` — новий). `ItemDefinition._stance` (`WeaponStance.Bow`) і `_backModel`
  (колчан) → `WeaponModel.Stance` = 3 / `Back`. Лук — у **лівій** руці, колчан весь час
  на спині. Вшита атака `WeaponArrow` (`SkillContentFactory.CreateArrowAttack`, у
  стартовому списку скілів) з сетом `VfxWeaponArrow`, де снаряд — модель стріли.
  Аніматор: стійка `2Hand-Bow` (біг у паку — `Run-Foward`), Projectile при луці —
  `Bow Release` → `Bow Hold` поки `Aiming`, закляття правою рукою, `Attack1..6` як удари.
  URP-копії матеріалів — `Art/Characters/Fina/` (оригінали — built-in шейдери
  Unity-chan, в URP розові); ці не перезбираються — видалити, щоб згенерувати.

## Вороги та рух
- ECS: `Code/Enemies/Components/EnemyAuthoritativeComponents.cs` — `EnemyTag`,
  `MovementData`, `ChaseTarget`, `PathProgress`, `PathPoint`, `NeedsRepath`.
- Системи: `Code/Enemies/Systems/` — `ChaseTarget`, `RepathRequest`, `Pathfinding`
  (єдина `SystemBase`, бо NavMesh — головний потік), `FollowPath`, `Separation`,
  `StatusMovement`, `NavMeshProjection`, `MovementApply` (**єдина, що рухає**).
- Пул: `Code/Enemies/EnemyPool.cs` + `Code/Spawning/Systems/EnemyPoolSystem.cs`.
- Налаштування: `Code/Shared/SimulationSettings.cs` ← `Data/Config/PathfindingConfig.asset`,
  `Data/Config/SeparationConfig.asset` через `Code/Shared/SimulationSettingsAuthoring.cs`.

## Спавн і хвилі
- ECS: `Code/Spawning/Components/SpawningComponents.cs` — `SpawnPoint`,
  `WaveSpawnerConfig`, `WaveSpawnOrder`, `WaveSpawnerState`.
- Логіка: `Code/Spawning/Systems/WaveSpawnSystem.cs`.
- Зв'язок: у данжі точки спавну не авторяться руками — їх ставить
  `Code/Dungeon/Systems/DungeonSpawnPointSystem.cs` з розкладки, а хвилю вмикає
  `Code/Dungeon/Systems/RoomActivationSystem.cs`.

## Данж: генерація і світ
- Вхід: `Code/Dungeon/DungeonDirector.cs` — генерація → геометрія → ECS → бейк
  navmesh. **Сід народжується тут** (`ResolveSeed()`).
- Генерація (чиста, без Unity): `Code/Dungeon/Generation/` — `DungeonGenerator.cs`
  (BSP), `DungeonLayout.cs` (`DungeonCellKind`, `DungeonRoomType`, `DungeonRoom`,
  `DungeonCorridor`, `DungeonRoomLink`), `DungeonRoomTypeAssigner.cs`,
  `DungeonGenerationSettings.cs`.
- Світ: `Code/Dungeon/World/` — `DungeonGeometryBuilder.cs`, `DungeonNavMeshBaker.cs`,
  `DungeonMaterialSet.cs`.
- Міст GO↔ECS: `Code/Dungeon/DungeonLayoutPublisher.cs` (лише копіює).
- ECS: `Code/Dungeon/Components/DungeonComponents.cs` — `DungeonRunState`,
  `DungeonRunPhase`, `DungeonRoomPhase`, `DungeonGrid`, `DungeonRoomElement`.
- Системи: `Code/Dungeon/Systems/` — `DungeonSpawnPoint`, `RoomOccupancy`,
  `RoomActivation`, `DungeonChest`.
- Дані: `Data/Config/DungeonGenerationConfig.asset`.

## Взаємодія (підійшов і натиснув)
- ECS: `Code/Interaction/Components/InteractionComponents.cs` — `InteractionRequest`
  (запит **без цілі**), `InteractableTag` (enableable = «ще можна»),
  `InteractionRadius`, `InteractionTriggered` (результат).
- Системи: `Code/Interaction/Systems/InteractionRegistrySystem.cs` (черга),
  `InteractionResolveSystem.cs` (хост обирає ціль).
- Споживачі `InteractionTriggered`: `ChestInteractionSystem`, `ItemPickupSystem`,
  `NpcInteractionSystem`.

## Лут
- ECS: `Code/Loot/Components/LootComponents.cs` — `ItemInstance`, `ItemRarity`,
  `ItemRiskState`, `ChestState`/`ChestPhase`, `LootTableId`, `LootRollRequest`,
  `LootRandom`, `RarityColor`.
- Блоби: `Code/Loot/Components/LootBlobs.cs` — `LootEntryBlob`, `LootTableBlob`,
  `LootDatabaseBlob`.
- Системи: `Code/Loot/Systems/` — `LootTableSystem` (кидок),
  `ChestInteractionSystem`, `ItemPickupSystem`, `LootItemPoolSystem`.
- Пул дропів: `Code/Loot/LootItemPool.cs` (`ItemDrop`).
- **Бейк обох баз одразу** — лут і предмети:
  `Code/Loot/Authoring/LootDatabaseAuthoring.cs` (`BuildItemDatabase`/`BuildItem`,
  `BuildLootDatabase`/`BuildTable`). Тобто `ItemDatabase` приходить
  **з Loot-бейкера**, а не з Equipment, хоч тип і лежить в Equipment.
- Дані: `Data/Items/**/*.asset` (Weapons/<тип>, Armour, Jewellery, Currency, Gems) (`Code/Config/ItemDefinition.cs`),
  `Data/Config/TreasureLootTable.asset` (`Code/Config/LootTable.cs`), `Data/Config/LootConfig.asset`.

## Предмети: база даних
- Блоб: `Code/Equipment/Components/ItemDatabaseBlob.cs` — `ItemBlob`, `AffixBlob`,
  `ItemDatabaseBlob`, компонент `ItemDatabase`.
  **Структуру з `BlobArray` не копіювати** — тільки `ref ItemBlob`.
- Читають майже всі: Equipment, Inventory, Skills, Vendor/Crafting, UI, `Currency`.

## Інвентар (grid у стилі PoE)
- ECS: `Code/Inventory/Components/InventoryComponents.cs` — `InventoryGridComponent`,
  `InventoryCell`, `ItemGridPlacement`, `ItemStored`, `InventoryPlacementMode`,
  `InventoryPlacementRequest`/`InventoryPlacementResult`, `CarriedBag`,
  `ContainerOwner`, `CharacterInventorySize`.
- Математика сітки: `Code/Inventory/GridFit.cs` — `TryGetFootprint`, `Fits`,
  `FindFirstFit`, `Occupy`, `Clear`, `Reset`, `CanRotate`.
- Логіка: `Code/Inventory/Systems/InventoryPlacementSystem.cs`.
- UI: `Code/UI/InventoryUI.cs`.

## Екіпіровка і стати
- ECS: `Code/Equipment/Components/EquipmentComponents.cs` — `EquipmentSlot`,
  `EquippedItem`, `EquipRequest`/`EquipRequestKind`, `EquipResult`/`EquipStatus`.
- Стати: `Code/Equipment/Components/StatComponents.cs` — `StatKind`, `ModifierKind`,
  `StatBlock` (**завжди `StatBlock.Zero()`**), `CharacterBaseStats`, `PlayerStats`,
  `StatsDirty`.
- Правила слотів: `Code/Equipment/EquipmentSlots.cs` (маска дозволених, дві руки).
- Кейстоуни: `Code/Equipment/Keystones.cs` — `KeystoneEffect`, `KeystoneComponent`,
  `KeystoneSet`.
- Системи: `Code/Equipment/Systems/` — `EquipmentSystem`, `PlayerStatsSystem`,
  `SocketSystem`, `SetBonusEvaluationSystem`.
- База персонажа: `Code/Equipment/Authoring/CharacterStatsAuthoring.cs` ←
  `Data/Config/CharacterConfig.asset` (стати, розмір сумки) **і**
  `Data/Config/StarterKitConfig.asset` (стартовий набір, окреме поле `_starterKit`).
- Стартовий набір: `Code/Config/StarterKitConfig.cs` (`StarterKitEntry`,
  `StarterKitPlacement`) → буфер `StarterItem` (`Worn`-прапорець на запис) →
  `Code/Skills/Systems/StarterKitSystem.cs` (`TryWear` бере перший вільний
  дозволений слот через `EquipmentSlots.TryFirstFree`). Заповнюють асет
  `BuildLibraryFactory.GrantTestKit` і `LobbyContentFactory.GrantStarterCoins`
  через спільний `SceneBuildUtility.AppendKitEntry`.

## Сети екіпіровки
- Дані: `Code/Config/ItemSetDefinition.cs` (`SetBonusThreshold`),
  ассети — `Data/Sets/*.asset`.
- Блоб: `Code/Equipment/Components/ItemSetComponents.cs` — `SetThresholdBlob`,
  `ItemSetBlob`, `ItemSetDatabaseBlob`, компонент `ItemSetDatabase`; рантайм —
  буфер `ActiveSetBonusStatus` на персонажі.
  **`ItemSetBlob` не копіювати** — усередині два `BlobArray`, тільки `ref`.
- Правила: `Code/Equipment/ItemSets.cs` — `SetIndexOf`, `Evaluate`,
  `TryHighestReached`, `EquippedCount`. Один файл на три читачі: система
  оцінки, стат-фолд, UI.
- Система: `Code/Equipment/Systems/SetBonusEvaluationSystem.cs` — на тому ж
  `StatsDirty`, **до** `PlayerStatsSystem` і після всіх, хто прапорець
  піднімає; сам не опускає.
- **Бейк — з Loot-бейкера**, разом з `ItemDatabase`:
  `Code/Loot/Authoring/LootDatabaseAuthoring.cs` (`BuildSetDatabase`/`BuildSet`,
  поле `_sets`). Афікси порогу складаються у два `StatBlock` уже при бейку.
  Сети вписуються в бейкер автоматично: `SceneBuildUtility.WriteSets` знаходить
  **усі** асети `ItemSetDefinition` у проєкті.
- Хто ще читає: `PlayerStatsSystem` (стати + keystone),
  `GemSockets.GatherSupports` (параметр `wearer` → «невидимий» супорт на всі
  скіли; **не** передається з `TriggerEvaluationSystem` — тригер це факт про
  сокет), `ItemTooltip.SetLines`, `InventoryUI.AddSetRow`, `LobbyUI`.
- Демо-контент: `ItemContentFactory.CreateOrLoadDemoSet()` →
  `Data/Sets/WarlordsRegalia.asset`.

## Геми й сокети
- ECS: `Code/Equipment/Components/SocketComponents.cs` — **`GemKind`**, `GearSocket`,
  `SocketRequest`/`SocketRequestKind`, `SocketResult`/`SocketStatus`.
- Гем несе **до двох** модифікаторів (`_gemSupport` + `_gemSupportSecond` на
  `ItemDefinition`, `GemSupport`/`GemSupportSecond`/`HasSupportSecond` у
  `ItemBlob`). Тому `MaxSupportsPerGroup` = 12: це модифікатори, не геми.
- Уся логіка читання отворів: `Code/Skills/GemSockets.cs` — `TryDescribeGem`,
  `TryReadSocket`, `TryResolveActive`, `GatherSupports`, `TryGetTrigger`,
  `ArmDefaultAttack`, `IsWorn`, `Rebuild`, `RerollWelded`.
- Зв'язок: гем — це звичайний предмет (`Data/Items/Gems/{Active,Support}/`); який скіл він дає,
  лежить у `ItemBlob`, а сам скіл — у `SkillDatabase`. Тобто
  **Skills → `GemSockets` → `GearSocket` + `ItemDatabase` → `SkillDatabase`**.
- Змінювати вміст отворів — через `SocketSystem`, не правкою буфера.

## Скіли: дані й каст
- Блоб: `Code/Skills/Components/SkillDatabaseBlob.cs` — `SkillBlob`,
  `SkillModifierBlob`, `SkillDatabaseBlob`, `ResolvedSkill`, компонент `SkillDatabase`.
- Бейк: `Code/Skills/Authoring/SkillDatabaseAuthoring.cs`
  (`BuildDatabase`/`BuildSkill`).
- ECS: `Code/Skills/Components/SkillComponents.cs` — `SkillEffectKind`,
  `SkillModifierKind`, `SkillSlot`, `SkillCastRequest`, `PendingCast`, `PendingHit`,
  `PendingArea`, `SkillProjectile`, `SkillPrefabs`, `SkillBudgetSettings`,
  `ProjectileActive`/`ProjectileSpent`.
- Супорти й умови: `Code/Skills/SkillModifiers.cs` (`SkillModifierPhase`),
  `Code/Skills/SkillConditions.cs` (`ModifierConditionType`, `CastConditions`,
  `CrowdRadius`).
- **Пасив (активний гем, який кастує лише тригер)** — `GemSockets.IsPassiveActive`
  / `FirstActiveSocket` / `HasPassiveActive`. Читають троє: `SkillCastSystem`
  (відмова кнопці), `TriggerEvaluationSystem` (що саме стріляти),
  `SocketSystem.BindBar` + панель (відмова в прив'язці).
- **Чи діє гем там, де стоїть** — `SkillModifiers.AppliesTo` / `WhyInert` /
  `NeedsCompanion` (+ `SkillDatabase.ShapeOf`). Одна таблиця на два читачі:
  клітинка сокета в `InventoryUI` і тултіп в `ItemTooltip`.
- **Новий вид супорта — це чотири місця**: `SkillModifierKind` у
  `SkillComponents.cs`, гілка в `Fold`/`Accumulate` у `SkillDatabaseBlob.cs`,
  фаза в `SkillModifiers.PhaseOf` (Cast — це default, решту вписувати) і рядок
  у `ItemTooltip.DescribeSupport`. Плюс поле на `Config/SkillModifier.cs`, якщо
  число не вміщається у `Value`/`SecondaryValue`.
- Крит: стати `CritChance`/`CritMultiplier` у `StatComponents.cs`; шанс і
  множник їдуть з фолду крізь `SkillProjectile`/`PendingArea`/`ElementZone` у
  `PendingHit`, **кидок і подія `OnCrit` — у `SkillHitSystem.Apply`** (раз на
  удар, з тілом, по якому влучило). Видно через `DamageEvent.Crit` →
  `DamageFeedback.Crit` → `Emphasis` у `DamageNumberSystem`.
- Мітка від зони присутністю (а не пульсом) — `ElementReactionSystem`:
  `GatherZones` + `ReactJob.StandingInZones` / `NeedsRenewing`. Там же єдиний
  `Apply`, через який проходить кожен статус.
- Добивання (`CullingStrike`) вирішує `DamageResolutionSystem`, мана за
  вбивство (`ManaOnKill`) — `DeathReactionSystem.GrantMana`. Обидва їдуть із
  касту як поля на `PendingHit`/`PendingArea`/`SkillProjectile`→`DamageEvent`.
- Тригери: `Code/Skills/Components/TriggerComponents.cs` — `TriggerConditionType`,
  `TriggerEvent`, `TriggerCooldown`; логіка —
  `Code/Skills/Systems/TriggerEvaluationSystem.cs`. Події шлють
  `DamageResolutionSystem` і `DeathReactionSystem`.
- **Відкладений удар (MeleeArc)**: `SkillCastSystem` списує кулдаун і ману на
  натисканні, а сам ефект (урон **і** cast-VFX) кладе в буфер `DelayedStrike` на
  персонажі — `min(Cooldown × SwingStrikeShare, MaxStrikeDelay)`. `TickStrikes`
  відлічує, `Land` випускає з поточної позиції гравця. Затримку бачить презентер
  через `CastCue.StrikeDelay` і темпує свінг (`_swingImpact`).
- **Снаряди й стіни**: `Code/Skills/WallQuery.cs` — PhysX-рейкаст по колайдерах
  рівня (стіни данжу, перешкоди арени; маска без шару `Character`, тригери й
  нормалі вгору — підлога — ігноруються). `SkillProjectileSystem.FindWalls` на
  головному потоці перед джобою → `NativeHashMap<Entity, float3>`; джоба ставить
  снаряд на стіну, спершу шукає тіло цього боку, інакше `Impact(Entity.Null)`:
  burst і on-impact тригер спрацьовують, розвилки нема, `SkillHit`-VFX у точці.
  Дуло підрізає `SkillCastSystem.Muzzle`. Unity Physics (ECS) у проєкті досі нема —
  це вбудований PhysX.
- Пошук цілей: `Code/Skills/EnemyTargets.cs`.
- Зони: `Code/Skills/Components/ElementZone.cs` (+ `ZoneSpawn`),
  `Systems/ElementZoneSystem.cs`, `ZonePoolSystem.cs`, `ProjectileZoneOverlapSystem.cs`.
- Системи: `Code/Skills/Systems/` — `SkillRegistry`, `SkillLoadout`, `SkillCast`,
  `SkillProjectile`, `ProjectilePool`, `SkillArea`, `SkillHit`, `StarterKit`.
- Дані: `Data/Skills/{Active,Supports}/*.asset` (`Code/Config/SkillDefinition.cs`,
  `Code/Config/SkillModifier.cs`).

## Бій: урон і смерть
- ECS: `Code/Combat/Components/DamageComponents.cs` — `DamageType`, `Health`,
  `DamageEvent`, `Dead`, `DamageFeedback`, `DeathFade`, `CombatTally`.
- Системи: `Code/Combat/Systems/` — `DamageResolutionSystem` (єдине місце, де
  здоров'я зменшується), `DeathReactionSystem`, `DeathFadeSystem`.
- Метр урону по манекену: `Code/Combat/Components/DamageMeterComponents.cs` +
  `Systems/DamageMeterSystem.cs`; показує `Code/UI/LobbyUI.cs`.

## Статуси, елементи, реакції
- Семантика статусу — **одне місце**: `Code/Combat/StatusEffects.cs`
  (`StatusEffectType`, `StatusCategory`, `StatusModifierTarget`, `BlocksMovement`,
  `BlocksCasting`, `TintOf`, `GlyphOf` тощо).
- ECS: `Code/Combat/Components/StatusComponents.cs` — `ActiveStatusEffect`,
  `StatusGate`, `CrowdControlImmunity`/`CrowdControlResistance`;
  `StatusPresentationComponents.cs` — `StatusVisual`;
  `ElementComponents.cs` — `ElementMask`.
- Реакції: `Code/Combat/Components/ElementReactionBlob.cs`, бейк —
  `Code/Combat/Authoring/ElementReactionAuthoring.cs`, логіка —
  `Systems/ElementReactionSystem.cs`; тік — `Systems/StatusTickSystem.cs`.
- Дані: `Data/Elements/Statuses/*.asset` (`Code/Config/StatusEffectDefinition.cs`),
  `Data/Elements/Reactions/*.asset` + `Data/Elements/ElementReactionTable.asset`.

## Ресурси гравця
- ECS: `Code/Player/Components/PlayerResources.cs` (`Mana`; здоров'я — `Health`
  з Combat), `Code/Player/Systems/PlayerResourceSystem.cs`.
- UI: `Code/UI/PlayerHud.cs` (колби + бар скілів).

## Лоббі: NPC, торговець, кузня, портал
- ECS: `Code/Lobby/Components/LobbyComponents.cs` — `NpcServiceType`, `NpcService`,
  `NpcSessionOpened`, `DungeonPortal`, `LobbyReadyPlayer`, `PortalReadyRequest`,
  `SceneTransition`/`SceneTransitionPhase`.
- Торговець: `Code/Lobby/Components/VendorComponents.cs` (`VendorComponent`,
  `VendorStockEntry`, `VendorTransactionRequest`/`VendorTransactionResult`),
  `Systems/VendorStockSystem.cs`, `Systems/VendorTransactionSystem.cs`.
- Кузня: `Code/Lobby/Components/CraftingComponents.cs` (`CraftOperation`,
  `CraftingStation`, `CraftRequest`/`CraftResult`), `Systems/CraftingSystem.cs`.
- Гроші: `Code/Lobby/Currency.cs` — `BasePrice`, `ValueOf`, `Balance`, `TryPay`,
  `Grant`, **`TryCollect`**. Баланс — це `Wallet` на персонажі
  (`Code/Player/Components/PlayerResources.cs`), **не** обхід сумки. Монета
  лишається предметом (`Data/Items/Currency/GoldSliver.asset`), поки лежить на підлозі;
  `ItemPickupSystem` і `StarterKitSystem` переганяють її в гаманець через
  `TryCollect`.
- Портал і вихід зі сцени: `Systems/DungeonPortalSystem.cs` +
  `Code/Lobby/SceneLoadBridge.cs` (**єдиний, хто кличе `LoadScene`**).
- Авторинг: `Code/Lobby/Authoring/LobbyNpcAuthoring.cs` (один бейкер на всі види NPC);
  контент — `Code/Editor/LobbyContentFactory.cs`.
- UI: `Code/UI/LobbyUI.cs`.

## UI-екрани
- `Code/UI/InventoryUI.cs` — сітка, екіпіровка, отвори; читає ECS, пише запит.
  Слоти екіпіровки — квадрати навколо портрета (`Doll` — таблиця «яка рейка,
  який рядок»; `DollCorner` — арифметика по ній). Дві рейки по п'ять — ліва
  й права від портрета, зброя першою в кожній. Стати — під портретом.
  Під час драгу всі цілі, куди можна кинути, мають рамку-кандидата
  (`SlotAccepts` / `SocketAccepts` / `BarAccepts` — ті самі, що й для наведення).
- **Поки панель тримає ввід, персонаж не обертається.** `GameBootstrap`
  будує `CreateUiCaptureProbe()` один раз і віддає його і
  `PlayerActionPublisher` (глушить касти), і `PlayerMotor` (пропускає
  `FaceAim`). Ходьба не глушиться.
- Портрет персонажа: `Code/Player/CharacterPortrait.cs` — камера-дитина
  гравця, рендерить у `RenderTexture` **лише шар `Character`** (шар додає
  `SceneBuildUtility.EnsureLayer` при збірці сцени, гравцеві ставить
  `CreatePlayer`). Вмикається лише поки панель відкрита; `SetAspect` не дає
  розтягнути людину під форму рамки.
- `Code/UI/LobbyUI.cs` — промпт NPC, крамниця, кузня, портал, метр урону.
- `Code/UI/PlayerHud.cs` — колби й бар скілів.
- Тултіп: `Code/UI/ItemTooltip.cs` (`ItemTooltip.Text` — текст про предмет) +
  `Code/UI/TooltipView.cs` (малювання). Обидві панелі беруть ту саму пару.
- Іконки предметів: `ItemDefinition._icon`. Панель тримає не визначення, а пари
  `InventoryUI._icons` (`ItemId` → `Sprite`), які пише `SceneBuildUtility.CreateInventoryUI`
  з усіх ассетів з іконкою — **нова іконка видна лише після перезбирання сцени**.
  Малюються в сумці, на привиді драгу й у сокеті (літера фази — у кутку); без іконки — назва.
  Прямим посиланням, не Addressables — як і VFX-сети: Addressables ще ніде не підключені.
  Спрайти гемів — `Assets/Gem Pack Complete/128 Full Content/Pack-ElementsGEMS.png`:
  активні — ограновані камені кольору стихії, супорти — квадратні в оправі.
- **Усе на uGUI + TextMeshPro.** UI Toolkit у проєкті більше немає ніде:
  кожен екран тримає `Canvas` у `[SerializeField]` замість `UIDocument`.
  Збирається кодом — ні `.uxml`, ні префабів панелей.
- Хелпер: `Code/UI/Ugui.cs` — **перша зупинка, коли треба щось намалювати**:
  - `Node`/`Box`/`Text`/`Button`/`Border`/`Clear`/`Fade` — елементи;
  - `Place` (чотири краї, як `position: absolute`), `TopLeft` (пікселі від
    лівого-верхнього, y вниз — цим думає сітка), `Point`/`Contains`
    (екранна точка → локальні пікселі; заміна `WorldToLocal`/`worldBound`);
  - `Column`/`Row`/`Fit`/`Size` — flexbox через `LayoutGroup` +
    `ContentSizeFitter` + `LayoutElement`;
  - `On(rect, EventTriggerType, handler)` — заміна `RegisterCallback<T>`;
  - генерація закруглених 9-slice спрайтів (`Rounded`).
- Canvas на екран, не спільний: `SceneBuildUtility.CreateUiCanvas` —
  масштаб 1200×800 по ширині, як було в `PanelSettings`. Порядок:
  Vfx `-2`, HUD `-1`, лоббі `0`, інвентар `1`, завіса `10`.
  `raycasts: false` не додає `GraphicRaycaster` — і це заміна
  `pickingMode = Ignore` на всьому дереві одразу.
- `EventSystem` + `InputSystemUIInputModule` створює
  `SceneBuildUtility.EnsureEventSystem` — один на сцену, без нього панелі
  малюються, але нічого не клікається.
- **Драг інвентаря більше не на подіях.** `PointerDown` лише починає його
  (`Ugui.On`), а далі `InventoryUI.StepDrag` щокадру читає
  `PlayerInputReader.PointerPosition` / `WasCastPressed(1)` /
  `WasCastReleased(0)`. Причина — у `docs/pitfalls.md`: у uGUI drag-події
  починаються тільки після кількох пікселів руху.
- Залишки від UI Toolkit, які нікуди не підключені:
  `Assets/_Project/UI/RuntimePanelSettings.asset`, `RuntimeTheme.tss`.

## VFX і презентація
- Шов: `Code/Vfx/Components/VfxComponents.cs` — `VfxEventKind`, `VfxEvent`
  (+ `VfxId` для авторованих сетів).
- Презентер: `Code/Vfx/VfxPresenter.cs` (лише читає ECS) + пули
  `Code/Vfx/VfxLinePool.cs`, `VfxParticlePool.cs`, `DamageNumberPool.cs`,
  `StatusIconPool.cs`. Hit-stop прибрано — `Time.timeScale` у проєкті
  не чіпає ніхто.
- **Авторовані ефекти на скіл**: `Code/Config/SkillVfxSet.cs` (cast /
  projectile / hit + scale, lift, **yaw** — поворот cast-ефекту поверх напряму
  касту; `Slash/*` з Casual RPG VFX авторовані назад → 180) ← ассети `Data/Vfx/*.asset`; `SkillDefinition._vfx`
  → `SkillBlob.VfxId` → `ResolvedSkill.VfxId` →
  `SkillProjectile.VfxId` / `PendingHit.VfxId` → `VfxEvent.VfxId`.
  Каст оголошує `SkillCastSystem` (`CastVfxPoint`), удар — `SkillHitSystem.Apply`,
  трейл тягне `VfxPresenter.DrawTrails` запитом `ProjectileActive` (події на це
  немає й не буде — снаряд це прапорець у пулі). **Сам снаряд без меша**
  (`SceneBuildUtility.CreateProjectilePrefab`): вигляд — лише сет. Колір
  підхопленого елемента (`URPMaterialPropertyBaseColor` на сутності) `DrawTrails`
  кладе на `MeshRenderer` слідувача через `VfxParticlePool.Tint`; частинки не фарбуються.
  Контент і список для презентера: `Code/Editor/SkillVfxContentFactory.cs`
  (шлях до паку — одна константа `Pack`).
- Системи: `Code/Vfx/Systems/` — `VfxEventRegistry`, `DamageNumber`, `StatusTint`.
- Дані: `Data/Config/VfxConfig.asset`.

## Звук
- Шов: `Code/Audio/Components/AudioComponents.cs` — `AudioCue`, `AudioEvent`.
- `Code/Audio/AudioPresenter.cs` (бюджети, дистанція від камери),
  `AudioSourcePool.cs`, `Systems/AudioEventRegistrySystem.cs`.
- Дані: `Data/Config/AudioConfig.asset`.

## Завіса і переходи між сценами
- `Code/Curtain/Components/CurtainComponents.cs` — `CurtainReason`, `CurtainState`,
  `CurtainRequest`; `Systems/CurtainSystem.cs`; `Code/Curtain/CurtainPresenter.cs`.
- Зв'язок: запит ставить `DungeonPortalSystem`, сцену вантажить `SceneLoadBridge`.

## Дебаг
- `Code/Debug/DebugHud.cs` (OnGUI), `DebugSpawnTrigger.cs`, `DebugRunStatusProbe.cs`.
- Манекени: `Code/Debug/Components/TrainingDummy.cs`, `DummyPatrol.cs`,
  `Authoring/TrainingDummyAuthoring.cs`, `Systems/TrainingDummySystem.cs`,
  `Systems/DummyPatrolSystem.cs`.

## Конфіги й контент-фабрики
- Усі ScriptableObject-типи: `Code/Config/` — Enemy, Spawn, Pathfinding, Separation,
  DungeonGeneration, Audio, Vfx, Loot, LootTable, Item, Character, SkillDefinition,
  SkillModifier, StatusEffect, ElementReactionRule/Table.
- Ассети: `Data/` — `Config/`, `Items/`, `Skills/`, `Elements/`, `Sets/`, `Vfx/`.
  **Фабрики шукають ассет за іменем будь-де під `Data/`** (`SceneBuildUtility.LoadConfig`,
  ним же користується `CreateOrLoadConfig`); папка у фабриці — лише куди ляже новий.
  Тож перекладати ассети по підпапках руками можна, дубля при білді не буде.
- Генерація контенту з редактора: `Code/Editor/` — `ItemContentFactory.cs`,
  `SkillContentFactory.cs`, `ElementContentFactory.cs`, `LobbyContentFactory.cs`,
  `BuildLibraryFactory.cs` (меню `Tools → Together We Fall → Grant Library Test Kit`).
- Префаби: `Assets/_Project/Prefabs/` — Enemy, Chest, LootItem, SkillProjectile,
  ElementZone.

## Чого немає
Save/Load, попапів і модальних діалогів, меню пауз чи налаштувань — **жодного файла**.
Причини по підсистемах — `docs/not-implemented.md`.
