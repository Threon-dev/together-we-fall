# Together We Fall

Top-down co-op action-roguelike у стилі Path of Exile 2 на Unity DOTS.
Зараз це **прототип продуктивності**: перевіряємо, чи тримає архітектура
100+ ворогів з navmesh-рухом і розштовхуванням, і чи зручно її буде
розширювати до мультиплеєра.

> **Архітектурні рішення** — у скілі `architecture-conventions`
> (`.claude/skills/architecture-conventions/SKILL.md`). Він — джерело правди
> щодо *як вирішувати*. Цей файл описує *що вже зроблено*.
> Звіряйся зі скілом перед тим, як пропонувати нову систему чи компонент.

## Стек

Unity **6000.3.18f1**, URP 17.3. Точні версії пакетів:

| Пакет | Версія |
|---|---|
| `com.unity.entities` | 1.4.8 |
| `com.unity.entities.graphics` | 1.4.21 |
| `com.unity.ai.navigation` | 2.0.14 |
| `com.unity.inputsystem` | 1.20.0 |
| `com.unity.addressables` | 2.9.1 (встановлений, ще не використовується) |

`activeInputHandler` — **New**. Legacy `UnityEngine.Input` кине виняток
у рантаймі, а не впаде на компіляції.

`Assets/Photon/Fusion` лишився з попереднього прототипу. Не використовується,
але й не видалений — не чіпати без запиту.

## Що працює

Хвиля зі 100+ ворогів спавниться по периметру, вони знаходять гравця,
обходять перешкоди по navmesh, розштовхуються в натовпі й не проходять
крізь стіни. Гравець ходить на WASD і дивиться на курсор. У HUD — FPS,
найгірший кадр і лічильник живих ворогів.

**Замірів продуктивності ще не знято.** Це наступний крок.

## Конвеєр систем

```
ChaseTargetSystem        Burst, паралельно    обирає ціль з буфера позицій гравців
RepathRequestSystem      Burst, паралельно    ВИРІШУЄ, кому потрібен новий шлях
PathfindingSystem        main thread, N/кадр  РАХУЄ шлях (NavMesh.CalculatePath)
FollowPathSystem         Burst, паралельно    веде по кутах шляху
SeparationSystem         Burst, паралельно    розштовхує сусідів (spatial hash)
NavMeshProjectionSystem  Burst, 1 потік       обрізає рух до дозволеного поверхнею
MovementApplySystem      Burst, паралельно    ЄДИНИЙ запис у LocalTransform
```

**Головне правило конвеєра:** усі стадії лише накопичують
`MovementData.DesiredVelocity`, а сутність рухає рівно одна система в кінці.
Тому новий вплив на рух ніколи не створює двох систем, що б'ються за трансформ,
і будь-який доданок можна вимкнути окремо під час профілювання.

## Ключові шви під мультиплеєр

- **Позиції гравців** — `PlayerPositionsSingleton` + `DynamicBuffer<PlayerPositionElement>`.
  Системи ворогів читають тільки цей буфер. Єдиний, хто його наповнює —
  `PlayerPositionPublisher` (MonoBehaviour). Заміна локального інпуту на
  мережевий снепшот = переписати цей один файл.
- **Мостів GO↔ECS рівно два**: `PlayerPositionPublisher` і `DebugSpawnTrigger`.
  Обидва без ігрової логіки.
- **Компоненти розділені по межі реплікації**:
  `EnemyAuthoritativeComponents.cs` (лишається на хості) vs
  `EnemyPresentationComponents.cs` (майбутні `[GhostField]`).
  `PathPoint` і `NavMeshAgentLocation` — свідомо не реплікуються.
- **Інпут** читається в одному місці й віддається як намір у світових координатах.

## Структура

```
Assets/_Project/
├── Code/
│   ├── Bootstrap/GameBootstrap.cs        єдина точка Initialize(), без static і DI
│   ├── Config/                           4 ScriptableObject-конфіги
│   ├── Shared/                           PlayerPositions*, SimulationSettings*
│   ├── Player/                           InputReader, Motor, MoveIntent, PositionPublisher
│   ├── CameraRig/TopDownCameraRig.cs     fixed yaw 45°, pitch 55°, зум
│   ├── Enemies/{Components,Authoring,Systems}/
│   ├── Spawning/{Components,Authoring,Systems}/
│   ├── Debug/                            DebugHud (OnGUI), DebugSpawnTrigger
│   └── Editor/ArenaSceneBuilder.cs       генерує всю сцену з нуля
├── Data/                                 Enemy/Spawn/Pathfinding/SeparationConfig.asset
├── Prefabs/EnemyPrefab.prefab
└── Scenes/
    ├── Arena.unity                       головна сцена
    ├── Arena/SubScene.unity              бейкінг: SpawnPoints, WaveSpawner, SimulationSettings
    └── Arena_NavMesh.asset               забейканий navmesh
```

## Як перезібрати сцену

1. `Tools → Together We Fall → Build Arena Scene`
2. **Обов'язковий ручний крок:** `SpawnPoints`, `WaveSpawner` і `SimulationSettings`
   будуть уже виділені в ієрархії → правий клік → `New Sub Scene` → `From Selection`.
   Без SubScene вони не бейкаються: хвилі не спавняться і сінглтон налаштувань
   не створюється, тож **жодна** система ворогів не запуститься.
3. Play. `Space` — спавн хвилі.

Робити SubScene кодом я свідомо не став: редакторне API не перевіряється
компіляцією, а через меню це два кліки.

## Граблі, на які вже наступили

Це реальні баги з цієї роботи, не гіпотетичні. Перевіряй їх першими,
якщо щось поводиться дивно.

- **`EnabledRefRW<T>` в `IJobEntity` вимагає `[WithPresent(typeof(T))]`.**
  Без атрибута компонент потрапляє в запит **з фільтром за enabled-станом**,
  і джоба бачить лише тих, у кого прапорець уже піднятий. Симптом: система
  спрацьовує рівно один раз на сутність. Так вороги перестали перераховувати
  шлях і почали бігати крізь стіни.
- **`IJobEntity` тільки з `LocalTransform` ловить усе підряд.** Точки спавну
  та інші забейкані об'єкти теж мають `LocalTransform`. Потрібен
  `[WithAll(typeof(EnemyTag))]`, інакше вони стають фантомними сусідами
  в spatial hash.
- **`NavMeshSurface.BuildNavMesh()` не зберігає дані.** Це рантайм-об'єкт;
  без явного `AssetDatabase.CreateAsset` сцена збереже посилання в нікуди
  і після перезавантаження шляхів не буде.
- **Ніщо не тримає сутність на navmesh саме по собі.** Рух по кутах шляху
  лише *виглядає* як обхід перешкод. Обмеження дає тільки
  `NavMeshProjectionSystem` через `NavMeshQuery.MoveLocation`.
- **`CellSize` у separation мусить бути ≥ `Radius`** — інакше блок 3×3
  клітинок не покриває радіус і сусіди тихо зникають. Затиснуто при бейку.

## Наступні кроки

1. **Заміри FPS на 100 / 300 / 500 ворогах.** `PathfindingSystem` має лишатись
   плоским по кількості (стеля `MaxRequestsPerFrame`), `SeparationSystem` —
   рости лінійно. Квадратичне зростання separation означає проблему з `CellSize`.
2. Найдешевші важелі, якщо просяде: `MaxNeighbors` 12→6, `MaxRequestsPerFrame`.
3. Якщо `PathfindingSystem` стане вузьким місцем — заміна на `NavMeshQuery`
   у Burst-джобах. Шов уже готовий: `NeedsRepath` на вході, буфер `PathPoint`
   на виході, зміна торкається одного файлу.

## Чого свідомо немає

Netcode for Entities і будь-яка мережа, атаки, здоров'я, лут, прогресія,
складна AI-поведінка (тільки chase), Unity Physics, модуль завантаження
Addressables (правило в скілі є, потреби ще не було).

Не додавати без явного запиту.
