# История изменений

## 0.1.0 — первый публичный выпуск

### Русский

**Способности**

- Grab: одна попытка на нажатие, захват живого игрока или выпавшего оружия, удержание на `Start.Distance` и бросок по G с силой `Strength`. Цель следует за прицелом по изогнутому лучу из 11 сегментов; промах не тратит заряд.
- Hook: зацеп за точку под прицелом, автоподтягивание с удержанием конечной позиции или ручное подтягивание колесом в режиме `Self`.
- Rope: трос с ограничением длины и сохранением поперечной скорости для раскачивания; регулировка длины колесом при включённом `Pull`.
- Линии `Beam`, `Laser`, `Lasso` и `Chain` с шириной, палитрой HEX, `rainbow` и цветом команды. `Beam` использует штатный материал CS2, остальные три — частицы клиентского аддона.

**Доступ и конфигурация**

- Профили администратора (`Admin.Flag`) и VIP (объект `HGR` в группе VIPCore) с фиксированными значениями, диапазонами, `default` и блокировкой `false`.
- `Settings.Priority` выбирает профиль при двух ролях; личные настройки хранятся по SteamID в `preferences.json` и `vip-preferences.json`.
- Лимит `Amount` за раунд, `-1` — безлимит. Счётчик обнуляется на старте раунда; промахи заряд не расходуют.
- `hgr.jsonc` с комментариями к каждому ключу. Конфиг старой схемы `Everyone`/`Vip` сохраняется как `hgr.old.jsonc` и заменяется текущим шаблоном.
- `css_hgr_reload` перечитывает конфиг, завершает способности и закрывает панели.

**Интерфейс**

- Panorama-панели `hgr_admin.xml` и `hgr_vip.xml` для `!admin → Управление HGR` и `!vip → HGR`, общий CSS и страница уведомлений.
- Бинды в чат из каждой способности, включая пару alias для режима удержания и бинды колеса, сохраняющие переключение оружия.
- Уведомления в чате и тосты PMM_VIPCore; каналы переключаются отдельно для каждого игрока.

**Звук**

- Семь событий `HGR.*` в `soundevents/hgr.vsndevts`: старт, поимка и удержание Grab, зацеп и подтягивание Hook, зацеп и отпускание Rope.
- Зацикленный звук удержания останавливается вместе со способностью; `Settings.Sounds` выключает все звуки сразу.

### English

**Abilities**

- Grab: a single attempt per press, grabs a living player or a dropped weapon, holds it at `Start.Distance` and throws it with G using `Strength`. The target follows the crosshair along an 11-segment curved beam; a miss spends no charge.
- Hook: attaches to the point under the crosshair, pulls automatically and holds the endpoint, or reels by hand in `Self` mode.
- Rope: a length-limited rope that keeps sideways speed for swinging, with wheel reeling when `Pull` is enabled.
- `Beam`, `Laser`, `Lasso` and `Chain` lines with width, HEX palette, `rainbow` and team colour. `Beam` uses the stock CS2 material, the other three come from the client addon.

**Access and configuration**

- Admin (`Admin.Flag`) and VIP (the `HGR` object in a VIPCore group) profiles with fixed values, ranges, `default` and `false` locks.
- `Settings.Priority` picks the profile for players with both roles; personal values are stored per SteamID in `preferences.json` and `vip-preferences.json`.
- A per-round `Amount` limit, `-1` for unlimited. The counter resets at round start and misses spend nothing.
- A commented `hgr.jsonc`. A configuration in the old `Everyone`/`Vip` schema is saved as `hgr.old.jsonc` and replaced with the current template.
- `css_hgr_reload` reloads the configuration, ends active abilities and closes the panels.

**Interface**

- Panorama panels `hgr_admin.xml` and `hgr_vip.xml` for `!admin → Manage HGR` and `!vip → HGR`, shared CSS and a notifications page.
- Bind lines printed from every ability, including the hold-mode alias pair and wheel binds that keep weapon switching.
- Chat notifications and PMM_VIPCore toasts, switchable per player.

**Sound**

- Seven `HGR.*` events in `soundevents/hgr.vsndevts`: Grab start, catch and hold, Hook cast and pull, Rope attach and release.
- The looped hold sound stops with the ability; `Settings.Sounds` disables every sound at once.
