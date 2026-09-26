# StationSpeed

[English](README.md) · **Русский**

Мод для Valheim: множители скорости рабочих станций — плавильни всех видов,
жаровни и печь, бродильная бочка, ульи, смолосборники и рост посаженного
(грядки и саженцы отдельно). По множителю на тип, при желании — на конкретный
префаб. Настройки сервера действуют у всех клиентов с модом.

Отличие от Valheim Plus / OdinsQOL и им подобных — в том, как это доходит до
игрока **без модов**: бочки и растения остаются для него согласованными (см.
ниже), а не показывают «ещё бродит», когда у вас «готово»; а то, что заложил
он сам, досдвигает хост.

Состояние и план работ — в `ROADMAP-RU.md`, история версий — в `CHANGELOG-RU.md`.

## Как это устроено

Игра хранит прогресс станций двумя разными способами, и мод обращается с ними
по-разному.

**Тикающие станции** — `Smelter` (плавильня, доменная печь, угольная печь,
прялка, мельница, очиститель эйтра), `CookingStation` (жаровни, печь),
`Beehive`, `SapCollector`. Раз в секунду клиент-**владелец** станции (обычно
ближайший игрок) прибавляет прошедшее время к накопителю в ZDO и выдаёт
продукт, когда набралось `m_secPerProduct`. Это поле экземпляра, и мод ставит
в него значение префаба, делённое на множитель: в `Awake` и заново для всех
загруженных станций при любой смене настроек (правка конфига, значения с
сервера, `stationspeed rescan`). Работает всегда, когда станцией владеет
клиент с модом. Если владеет игрок без мода — станция идёт с ванильной
скоростью; картинку (продукты, слоты, топливо) он в любом случае видит
правильную, потому что она берётся из ZDO. Чтобы станции рядом с хостом
всегда были у хоста, есть отдельный мод [HostOwner](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/) (группа `Stations`).

Топливо. Плавильни жгут его в долях продукта (`m_fuelPerProduct /
m_secPerProduct` в секунду), так что уголь на слиток не меняется сам собой.
Печь жжёт своё по часам (`m_secPerFuel`), поэтому `ScaleOvenFuel` (включено)
делит и его — дров на хлеб как в ванилле; выключено — дрова горят ванильное
время, и ускорение делает выпечку дешевле. Мельница поверх множителя
по-прежнему зависит от ветра.

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

Рядом со сдвинутой меткой мод оставляет свою отметку (`j1ga.stationspeed.shifted`
— метка после сдвига): пока она равна метке, сдвиг уже сделан; новая закладка
даёт новую метку и снимает вопрос сама.

**Чужие закладки досдвигает хост.** Если бочку заложил или растение посадил
игрок без мода, владельцем в тот момент был он, и на нём ничего не сработало;
но сервер держит все ZDO мира и получает новую метку в течение секунды. Хост
(или выделенный сервер с модом) раз в `HostScanInterval` секунд проходит по
объектам мира — по 20 000 за кадр, чтобы не было рывка — и всё, у чего метка
свежая (моложе пяти минут) и не равна отметке, сдвигает сам. Запись доходит до
владельца как любая другая: в `ZDOMan.RPC_ZDOData` решает ревизия данных, а не
владение. Время роста растения без загруженного экземпляра хост считает так
же, как `Plant.GetGrowTime` — по сиду из ZDO; при первой посадке на владельце
оба значения сверяются, и если они вдруг разойдутся (обновление игры), в лог
уйдёт предупреждение, а хост перестанет трогать растения.

**Настройки с сервера.** Через секунду после входа клиента (и при каждом
изменении настроек) сервер шлёт свои множители, `ByPrefab`, `ScaleOvenFuel` и
`Enabled` собственным routed RPC; клиент с модом пользуется ими, пока
подключён, и сразу пересчитывает загруженные станции. Так все владельцы
согласны в скорости. Пакеты не от сервера игнорируются. Если на сервере мода
нет — у каждого свой файл.

## Скриншоты

| | |
|---|---|
| ![Плавильня с множителем скорости](https://raw.githubusercontent.com/tbsj1ga/StationSpeedValheim/main/docs/media/smelter.webp) | ![От посадки до урожая за секунды](https://raw.githubusercontent.com/tbsj1ga/StationSpeedValheim/main/docs/media/crops.webp) |
| Плавильня с множителем скорости | От посадки до урожая за секунды |

## Совместимость

Проверено на **Valheim 1.0.16** (network version 40), **BepInEx 5.4.23.5** (BepInExPack_Valheim 5.4.2351).

## Кому ставить

| Кто | Что |
|---|---|
| Игроки с модом | станции, которыми они владеют, работают с заданной скоростью |
| Хост | желательно: досдвигает бочки и растения игроков без мода и раздаёт настройки |
| Выделенный сервер | по желанию, ради тех же двух вещей |
| Игроки без мода | подключаются как обычно; бочки и растения у них согласованы |

## Известные конфликты

- Другие моды на скорость станций или роста (Valheim Plus, OdinsQOL и похожие) — множители складываются или спорят. Для каждого вида станций используйте что-то одно.
- Моды, которые меняют длительность брожения или роста только на одном клиенте, ломают то, чего этот мод избегает: ванильные клиенты расходятся в готовности.

## Ошибки и отзывы

GitHub Issues: https://github.com/tbsj1ga/StationSpeedValheim/issues — приложите `BepInEx/LogOutput.log`.

## Установка

Файл `build/StationSpeed.dll` кладётся в

```
%AppData%\r2modmanPlus-local\Valheim\profiles\Valheim\BepInEx\plugins\StationSpeed\
```

или `build.ps1 -Install`. Нужен на клиентах, которые будут владеть станциями
(на практике — у всех, кто играет с модами), и на хосте: хост досдвигает
закладки игроков без мода и раздаёт настройки. На выделенном сервере — по
желанию, ради тех же двух вещей. Игроки без мода подключаются как обычно.

## Настройки

`BepInEx\config\j1ga.stationspeed.cfg`, создаётся при первом запуске. Все
множители по умолчанию 1, допустимо 0.1…100; 2 — вдвое быстрее, 0.5 — вдвое
медленнее. Правки применяются к загруженным станциям сразу.

| Раздел | Ключ | Префабы / смысл |
|---|---|---|
| Smelters | `Smelter`, `BlastFurnace`, `CharcoalKiln`, `SpinningWheel`, `Windmill`, `EitrRefinery`, `OtherSmelter` | `smelter`, `blastfurnace`, `charcoal_kiln`, `piece_spinningwheel`, `windmill`, `eitrrefinery`, всё прочее на `Smelter` |
| Cooking | `CookingStation`, `Oven`, `OtherCooking`, `ScaleOvenFuel` | `piece_cookingstation`, `piece_cookingstation_iron`; `piece_oven`; прочее на `CookingStation`; жечь топливо печи с тем же множителем |
| Timestamp stations | `Fermenter`, `Crops`, `Saplings` | `fermenter`; растения, не вырастающие в дерево; саженцы деревьев |
| Collectors | `Beehive`, `SapCollector` | `piece_beehive`, `piece_sapcollector` |
| Overrides | `ByPrefab` | `префаб=множитель, …` — имеет приоритет над типом |
| Host | `HostShift`, `HostScanInterval` | досдвиг чужих закладок с хоста; период обхода, с |
| Sync | `SyncConfig` | сервер раздаёт настройки клиентам (действует на сервере) |
| General | `Enabled`, `Debug` | выключатель; лог каждой тронутой станции |

Консоль (F5): `stationspeed status` — действующие множители (с сервера, если
подключены к серверу с модом), статистика хоста; `stationspeed rescan` —
применить множители к загруженным станциям ещё раз.

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
| `StationSpeedPlugin.cs` | константы, `Awake`/`Update`/`OnDestroy`, `ShiftStart` с отметкой, `PrefabComponent`, помощники, обработка ошибок |
| `StationSpeedPlugin.Config.cs` | все `ConfigEntry`, действующие значения (свои или с сервера), выбор множителя по префабу и типу, грядка/саженец |
| `StationSpeedPlugin.Patches.cs` | применение к тикающим станциям от префаба, перерасчёт `Reapply`, сдвиг на владельце, Harmony-патчи станций |
| `StationSpeedPlugin.Host.cs` | обход ZDO на хосте, свежесть и отметка, копия `GetGrowTime` |
| `StationSpeedPlugin.Sync.cs` | routed RPC настроек: отправка с сервера, приём на клиенте, патчи `ZNet` |
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
цели рефлексии (`Plant.GetGrowTime`, `ZDOMan.m_objectsByID`) и цели
Harmony-патчей.

## Репозиторий

Ветка `main` на GitHub: https://github.com/tbsj1ga/StationSpeedValheim. Под версионированием: исходники,
`.csproj`, скрипты, документация, заготовка Thunderstore и
`build\StationSpeed.dll`. Не под ним: конфиг BepInEx, `bin/`, `obj/`,
zip-пакеты — см. `.gitignore`.

## Другие моды j1gA

| | Мод |
|---|---|
| [![LivingMap](https://raw.githubusercontent.com/tbsj1ga/LivingMapValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/LivingMap/) | **[LivingMap](https://thunderstore.io/c/valheim/p/j1gA/LivingMap/)** — Постройки, дороги и вырубки на карте и мини-карте. |
| [![WeaponArts](https://raw.githubusercontent.com/tbsj1ga/WeaponArtsValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/WeaponArts/) | **[WeaponArts](https://thunderstore.io/c/valheim/p/j1gA/WeaponArts/)** — Одна клавиша — своя активная способность у каждого оружия: стаггер, таунт, хилы, берсерк, криты. |
| [![ExtendedBosses](https://raw.githubusercontent.com/tbsj1ga/ExtendedBossesValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/ExtendedBosses/) | **[ExtendedBosses](https://thunderstore.io/c/valheim/p/j1gA/ExtendedBosses/)** — Боссы как рейды: фазы, адды, гнёзда, щиты, метки — из ванильных частей. |
| [![HostOwner](https://raw.githubusercontent.com/tbsj1ga/HostOwnerValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/) | **[HostOwner](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/)** — Хост забирает владение станциями и боссами рядом, чтобы его моды работали для всех. |

## Помощь ИИ

Мод разработан с помощью ИИ-ассистента (Claude от Anthropic). Код и
документация написаны вместе с ним и сверены с IL игры; решения по дизайну,
проверка в игре и релизы — за автором.
