# Мережа і шви під неї

> Як влаштований мультиплеєр — Netcode for Entities, хост як listen server,
> Relay-код для друга — і які місця ще чекають. Читати перед будь-якою роботою,
> що торкається гравця, запитів клієнта чи детермінізму.

## Як це працює зараз

- **Два світи в процесі хоста.** `ServerWorld` симулює все; `ClientWorld` хоста
  підключений до нього по IPC, як будь-який інший клієнт. У друга — тільки
  `ClientWorld`. Хост в одному світі в NFE 1.14 є
  (`NetCodeConfig.HostWorldMode.SingleWorld`), але свідомо не використаний: так
  хост бачить гру через ті самі репліковані дані, що й друг, і баг «у хоста
  працює, у друга ні» не ховається.
- **Системи логіки — `[WorldSystemFilter(ServerSimulation)]`, усі.** Нова система
  без атрибута запуститься й у клієнтському світі. Винятки — `Server | Client`:
  реєстри черг, у які пишуть клієнтські мости (`PlayerPositionRegistrySystem`,
  `InteractionRegistrySystem`, `SkillRegistrySystem`, `VfxEventRegistrySystem`,
  `AudioEventRegistrySystem`, `CurtainSystem`).
- **Усі GO-мости читають і пишуть `ClientWorld`, і на хості теж.**
  `World.DefaultGameObjectInjectionWorld` виставляють `SessionLauncher.EnterGame`
  і `NetworkBootstrap`. Тому жоден міст не знає про мережу. Єдиний виняток —
  `DungeonLayoutPublisher`: він пише в `ServerWorld`, коли той є.
- **Вхід.** Сцена `Menu` → `SessionLauncher` → Multiplayer Services Sessions API з
  `WithRelayNetwork()`; entities-handler пакета сам створює світи й драйвери.
  Play у Lobby/Dungeon/Arena без меню — `NetworkBootstrap` хостить локально
  (`AutoConnectPort` 7979). Id гравця = `NetworkId`, береться в
  `GameBootstrap.ResolvePlayerId`; перший клієнт свіжого сервера завжди 1.
- **Гравець → хост: потік команд.** `PlayerPositionPublisher` і
  `PlayerActionPublisher` не змінились і пишуть у черги `ClientWorld`.
  `PlayerCommandSendSystem` пакує їх у `PlayerCommand` свого аватара (ghost з
  власником, `AutoCommandTarget`), `PlayerCommandReceiveSystem` на хості
  розпаковує в ті самі буфери `ServerWorld` — з `PlayerId` власника ghost-а, а не
  з команди. **Позиції хост вірить** — розмін для коопу з друзями.
- **Хост → гравці, ghost-и** (усі interpolated): аватар, ворог, снаряд, зона,
  сундук, предмет на підлозі. Реплікуються `LocalTransform` (позиція, поворот,
  масштаб), `URPMaterialPropertyBaseColor` (обидва — варіанти в `GhostVariants.cs`) і enabled-біти
  `EnemyTag`, `Dead`, `DeathFade`, `ProjectileActive`, `ZoneActive`. Пули
  паркуються на `y = -1000`, тож сховані члени пулу — теж ghost-и, які просто
  стоять під підлогою. Тому трансформ — власний варіант з `MaxSmoothingDistance = 10`:
  видача з пулу — це стрибок, і згладжений він виглядав як снаряд, що вилазить з-під
  підлоги або (при екстраполяції) падає на дуло з кілометрової висоти.
- **Хост → гравці, RPC-пересилання черг.** `EventSync` (`VfxEvent` і `AudioEvent`,
  з лімітом на тік; він же тепер чистить серверну чергу VFX), `CurtainSync`,
  `PortalSync` (готовність і фаза переходу — `SceneLoadBridge` чекає на неї без
  змін), `NpcSessionSync` (відкрита сесія: **тип** NPC, не сутність — NPC у двох
  світах мають різні id), `DungeonSeedChannel` (сід).
- **Лист персонажа — теж ghost-и, і кожен клієнт отримує всі.** Персонаж
  (`PlayerCharacterAuthoring`), сумка (`PlayerBagAuthoring`), полиця торговця
  (`VendorShelfAuthoring`) інстансяться з префабів `NetworkPrefabs`; предмети —
  члени лут-пулу, вже ghost-и. `[GhostField]` стоїть лише на тому, що читає UI:
  здоров'я, мана, гаманець, `CastCue`, `PlayerWarp`, `PlayerStats` (`StatBlock` —
  `FixedList`, NFE його реплікує), keystone, частина `ActiveSetBonusStatus`,
  буфери `EquippedItem`/`SkillSlot`/`InventoryCell`/`GearSocket`, `ItemInstance`,
  `ItemDisplayName`, `ItemGridPlacement`. Черги запитів і результатів на
  клієнтській копії **не** реплікуються — вони локальні для екрана.
- **Запити панелей → хост, результати → власнику**: `CharacterRequestSync`.
  Панелі не змінились і кладуть запит у свого персонажа; сутності в запитах
  їдуть як ghost-id (`GhostRef`), торговець і кузня — за видом NPC, бо вони не
  ghost-и. Хост очищає свої буфери результатів сам — раніше це робив UI.
  `VendorShelfLinkSystem` підставляє клієнтському торговцю полицю-ghost.
- **Чужі гравці** — `RemotePlayerPresenter`: копія неактивного тіла з тим самим
  `PlayerAnimationPresenter`. Швидкість для анімації презентер тепер міряє з
  трансформа, а не з `CharacterController`.
- **Статичні сутності SubScene** — NPC, портал, манекени, бази даних — не ghost-и:
  бейкаються в обидва світи самі й вивантажуються зі своєю сценою. Тому те, що в
  них рухається, мусить рухатись в **обох** світах: патруль манекена
  (`DummyPatrolSystem`) — чиста функція від мережевого тіку, хост рахує її на
  `ServerTick`, клієнт — на `InterpolationTick`. Спалах манекена від удару
  (`TrainingDummySystem`, колір пише лише хост) клієнтська копія досі не бачить.
- **Ghost-и живуть сесію, а не сцену.** `GhostSceneDetachSystem` знімає `SceneTag`
  з кожного екземпляра ghost-а, інакше вивантаження SubScene лоббі забирало б
  персонажів, сумки, аватари й пули. Префаби ghost-ів з наступної сцени NetCode
  підхоплює сам — за тим самим типом.

## Шви, що лишаються в силі

- **Позиції гравців** — `PlayerPositionsSingleton` + `DynamicBuffer<PlayerPositionElement>`.
  Системи ворогів читають тільки цей буфер. На хості його наповнює
  `PlayerCommandReceiveSystem`; ціль знімає лише з id, що колись прийшли по
  мережі, бо `AllyDummySystem` публікує туди ж союзника-манекена.
  Від'єднаний гравець лишається в буфері з `IsTargetable = false` — тому
  `DungeonPortalSystem` чекає лише присутніх.
- **Мости GO↔ECS бувають двох видів, і важливий напрямок.** Ті, що **пишуть**
  намір, — `PlayerPositionPublisher`, `PlayerActionPublisher`,
  `DungeonLayoutPublisher`, `DebugSpawnTrigger`, панелі `InventoryUI` і `LobbyUI`.
  Ті, що лише **читають**, — `VfxPresenter`, `AudioPresenter`, `CurtainPresenter`,
  `PlayerHud`, `DebugRunStatusProbe`, `SceneLoadBridge`, `RemotePlayerPresenter`.
  Правило: **жоден не має ігрової логіки.** Новий міст, що пише, — це новий вид
  команди або RPC, і його треба звіряти з тим, що клієнту дозволено стверджувати.
- **Сутність персонажа не публікується мостом, а виводиться** з буфера позицій
  (`PlayerCharacterRegistrySystem`). Гравець, що підключився, отримує персонажа
  на хості сам собою.
- **Контейнер має власника.** `ContainerOwner` перевіряється на кожному запиті
  розміщення; спільний стеш буде контейнером **без** власника і окремою розмовою
  про конфлікти запису.
- **Запит і результат — різні типи й різні системи.** `InteractionRequest`
  (гравець + позиція, без цілі) → `InteractionTriggered` (хост). Клієнт, який
  називає ціль сам, може назвати сундук на іншому кінці поверху.
- **Сід** народжується рівно в одному місці — `DungeonDirector.ResolveSeed()`, і
  викликається тільки на хості. Данж не йде по мережі: йде число
  (`DungeonSeedChannel`), кожен клієнт будує ідентичний поверх сам, а поки чекає —
  мотор гравця вимкнений. Генерація — чиста функція від сіду.
- **Межа реплікації**: `EnemyAuthoritativeComponents.cs` лишається на хості
  (виняток — enabled-біт `EnemyTag`), `PathPoint` і `NavMeshAgentLocation` не
  реплікуються.

## Чого ще немає

- **Затримка на панелях.** Предмет, перетягнутий у сумці, стане на місце, коли
  прийде снепшот з хоста — предикції запитів немає.
- **Кілька торговців**: `VendorShelfLinkSystem` вважає, що полиця одна.
- **Повернення з данжу в лоббі**: старі полиця-ghost і її предмети не
  прибираються.
- **Манекени лоббі** не блимають у клієнта й табло шкоди не оновлюється.
- **Пізнє приєднання**, поки хост у данжі: гравець потрапить у лоббі сам.
- **Relevancy**: усі пул-ghost-и йдуть кожному клієнту.
