# StationSpeed

Мод для Valheim: множители скорости рабочих станций — плавильни всех видов,
жаровни и печь, бродильная бочка, ульи, смолосборники и рост посаженного. По
множителю на тип, при желании — на конкретный префаб.

Отличие от Valheim Plus / OdinsQOL и им подобных — в том, как это доходит до
игрока **без модов**: бочки и растения остаются для него согласованными (см.
ниже), а не показывают «ещё бродит», когда у вас «готово».

Состояние и план работ — в `ROADMAP.md`, история версий — в `CHANGELOG.md`.

## Как это устроено

Игра хранит прогресс станций двумя разными способами, и мод обращается с ними
по-разному.

**Тикающие станции** — `Smelter` (плавильня, доменная печь, угольная печь,
прялка, мельница, очиститель эйтра), `CookingStation` (жаровни, печь),
`Beehive`, `SapCollector`. Раз в секунду клиент-**владелец** станции (обычно
ближайший игрок) прибавляет прошедшее время к накопителю в ZDO и выдаёт
продукт, когда набралось `m_secPerProduct`. Это поле экземпляра, и мод делит
его на множитель в `Awake`. Работает всегда, когда станцией владеет клиент с
модом. Если владеет игрок без мода — станция идёт с ванильной скоростью; картинку
(продукты, слоты, топливо) он в любом случае видит правильную, потому что она
берётся из ZDO.

**Станции по метке времени** — `Fermenter` и `Plant`. В ZDO лежит только момент
старта (`StartTime`, `plantTime`); готовность **каждый клиент считает сам** из
своей копии длительности (`m_fermentationDuration`, `GetGrowTime()`). Если
изменить длительность, ваш клиент и ванильный разойдутся в мнении об одной и
той же бочке: у вас «готово», у него «бродит», и его `Interact` не отправит
`RPC_Tap`, пока не пройдёт ванильное время. Поэтому длительность не трогается,
а один раз сдвигается метка: в момент закладки владелец пишет
`start = now − D + D/k`. Ванильная формула после этого даёт один и тот же ответ
на любом клиенте, с модом и без. Для растений то же самое с `plantTime`;
`GetGrowTime()` у них засеян по растению, так что все клиенты сходятся и в ней.

Ограничение общее: сдвиг делает владелец в момент закладки (`Fermenter.RPC_AddItem`)
или посадки (`Plant.Awake`). Если бочку заложил или растение посадил игрок без
мода и владел ими в этот момент — метка ванильная. Как закрыть и это с хоста,
см. `ROADMAP.md`.

Значения читаются при загрузке станции: уже загруженные станции получат новый
множитель после перезахода или перезагрузки зоны.

## Установка

Файл `build/StationSpeed.dll` кладётся в

```
%AppData%\r2modmanPlus-local\Valheim\profiles\Valheim\BepInEx\plugins\StationSpeed\
```

или `build.ps1 -Install`. Нужен на клиентах, которые будут владеть станциями
(на практике — у всех, кто играет с модами); на выделенном сервере не нужен.
Игроки без мода подключаются как обычно.

## Настройки

`BepInEx\config\j1ga.stationspeed.cfg`, создаётся при первом запуске. Все
множители по умолчанию 1, допустимо 0.1…100; 2 — вдвое быстрее, 0.5 — вдвое
медленнее.

| Раздел | Ключ | Префабы |
|---|---|---|
| Smelters | `Smelter`, `BlastFurnace`, `CharcoalKiln`, `SpinningWheel`, `Windmill`, `EitrRefinery`, `OtherSmelter` | `smelter`, `blastfurnace`, `charcoal_kiln`, `piece_spinningwheel`, `windmill`, `eitrrefinery`, всё прочее на `Smelter` |
| Cooking | `CookingStation`, `Oven`, `OtherCooking` | `piece_cookingstation`, `piece_cookingstation_iron`; `piece_oven`; прочее на `CookingStation` |
| Timestamp stations | `Fermenter`, `Plants` | `fermenter`; всё с компонентом `Plant` |
| Collectors | `Beehive`, `SapCollector` | `piece_beehive`, `piece_sapcollector` |
| Overrides | `ByPrefab` | `префаб=множитель, …` — имеет приоритет над типом |
| General | `Enabled`, `Debug` | выключатель; лог каждой тронутой станции |

Консоль (F5): `stationspeed status` — действующие множители.

## Где что лежит

| Что | Где |
|---|---|
| Конфиг | `BepInEx\config\j1ga.stationspeed.cfg` |
| Исходники | `src\StationSpeedPlugin*.cs` — один `partial class`, по файлу на область |
| Сборка | `build\StationSpeed.dll` |
| Версия мода (одно место) | константа `Version` в `src\StationSpeedPlugin.cs`; `build.ps1 -Package` подставляет её в `manifest.json` |
| Проверка ссылок | `check-refs.ps1`, запускается сборкой |
| Пакет Thunderstore | `thunderstore\` (manifest, icon 256×256, README) → `build\StationSpeed-<версия>.zip` |
| Лицензия | `LICENSE`, MIT |

| Файл | Что в нём |
|---|---|
| `StationSpeedPlugin.cs` | константы, `Awake`/`OnDestroy`, `ShiftStart`, помощники, обработка ошибок |
| `StationSpeedPlugin.Config.cs` | все `ConfigEntry`, выбор множителя по префабу и типу |
| `StationSpeedPlugin.Patches.cs` | обработчики станций и Harmony-патчи: `Smelter/CookingStation/Beehive/SapCollector.Awake`, `Fermenter.RPC_AddItem`, `Plant.Awake` |
| `StationSpeedPlugin.Commands.cs` | консольная команда `stationspeed` |

## Сборка

```
powershell -ExecutionPolicy Bypass -File .\build.ps1            # собрать и проверить ссылки
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install   # ... и положить в plugins
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Package   # ... и собрать zip для Thunderstore
```

Компилятор — `csc.exe` из .NET Framework (C# 5: без `out var`, `?.`, `$""`,
`nameof`), ссылки — прямо из папки игры и `BepInEx\core` профиля r2modman; пути
в начале `build.ps1`, `check-refs.ps1` и `src\StationSpeed.csproj`. После сборки
`check-refs.ps1` через Mono.Cecil сверяет с игрой каждую ссылку на тип и член,
цели рефлексии (`Plant.GetGrowTime`) и цели Harmony-патчей.

## Репозиторий

Локальный git-репозиторий, ветка `main`. Под версионированием: исходники,
`.csproj`, скрипты, документация, заготовка Thunderstore и
`build\StationSpeed.dll`. Не под ним: конфиг BepInEx, `bin/`, `obj/`,
zip-пакеты — см. `.gitignore`.
