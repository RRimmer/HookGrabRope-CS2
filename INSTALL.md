# HookGrabRope-CS2 0.1.0 — установка

[README](README.md) · [История изменений](CHANGELOG.md) · [Лицензия](LICENSE)

## Установка и обновление

1. Серверу нужен CounterStrikeSharp 1.0.380. Для входа через `!admin` используется PMM_SimpleAdmin 0.1.1 с разделом HGR, через `!vip` — VIPCore Fabius и PMM_VIPCore 0.1.6 с карточкой HGR. Эти пакеты устанавливаются отдельно.
2. Запустите `pack.cmd` в корне репозитория — он собирает `HookGrabRope/HookGrabRope.csproj` и создаёт `HookGrabRope-CS2.zip`. Готовый архив также лежит в релизах. Из архива скопируйте серверную папку `addons` в `game/csgo/`. **При обновлении пропустите существующий hgr.jsonc**: при загрузке плагин сам обнаружит старые секции Everyone/Vip, сохранит файл рядом как hgr.old.jsonc и создаст новый шаблон. Перенесите свои серверные настройки вручную. Новые конфиги читаются без перезаписи.
3. Перенесите содержимое `Content-addonmanager` из этого же архива в исходники своего Workshop-аддона. Соберите обе Panorama-панели, общий CSS, particles/hgr, materials/hgr, sounds/hgr и soundevents/hgr.vsndevts. WAV с суффиксом `_loop` содержит метки цикла. Распространите обновлённый аддон на сервер и клиентам, затем смените карту. Без аддона работает только линия Beam: она использует штатный `materials/sprites/laserbeam.vmat`.
4. Проверьте версию командой `css_plugins list`. Для изменений уже установленных настроек используйте `css_hgr_reload` из серверной консоли или с флагом Admin.Flag. Команда завершает активные способности и закрывает панели. После добавления новых ресурсов смените карту.

Архив содержит:

| Путь в архиве | Назначение |
| --- | --- |
| `addons/counterstrikesharp/plugins/HookGrabRope/` | DLL, `deps.json`, `lang`, `gamedata` и `addon-resources.json` |
| `addons/counterstrikesharp/configs/plugins/HookGrabRope/hgr.jsonc` | Серверный конфиг по умолчанию |
| `Content-addonmanager/` | Клиентские частицы, материалы, звуки и Panorama-панели |
| `INSTALL.md`, `CHANGELOG.md` | Документация рядом с плагином |

## Серверный конфиг

`addons/counterstrikesharp/configs/plugins/HookGrabRope/hgr.jsonc` содержит Defaults, Admin, Settings, PaletteDef, PaletteHex и Particles. Defaults.<способность>.Enabled отключает её для обеих ролей. Admin.Flag по умолчанию @css/root. Для входа через раздел SimpleAdmin игроку также нужен доступ к самой админ-панели. Обычные игроки способностей не получают.

Settings.Priority выбирает профиль целиком. При двух ролях игрок может изменить приоритет. Личные параметры двух профилей сохраняются рядом с DLL в `preferences.json` и `vip-preferences.json`. Расход общий для обеих ролей до следующего раунда. Серверные звуки отключаются через Settings.Sounds. Для замены звука зарегистрируйте событие HGR.<имя файла> в hgr.vsndevts, укажите путь в Defaults и обновите серверный addon-resources.json из нового архива pack.cmd.

## VIPCore Fabius

Откройте `game/csgo/addons/counterstrikesharp/configs/plugins/VIPCore/vip.json`. Ниже приведён полный блок HGR в контексте группы. ONLYRIMMER — пример имени. В существующей группе скопируйте свойство `"HGR": { ... }` внутрь её `Values`, рядом с другими VIP-функциями. Сохраните остальные свойства группы и поставьте запятую между соседними свойствами. Используйте обычный JSON без комментариев.

```json
{
  "Groups": {
    "ONLYRIMMER": {
      "Values": {
        "HGR": {
          "Hook": {
            "Enabled": true,
            "Amount": 3,
            "Color": "all",
            "EffectLine": "All",
            "Width": "1-5",
            "Range": "50-550",
            "Distance": "0-550",
            "Step up": "50-100",
            "Step down": "50-100",
            "Pull": "default",
            "Sticking": "default"
          },
          "Grab": {
            "Enabled": true,
            "Amount": "1-3",
            "Color": "all",
            "EffectLine": "Chain, Lasso",
            "Width": "1-5",
            "Range": "50-550",
            "Distance": "50-1000",
            "Start.Distance": "50-300",
            "Strength": "50-1000",
            "Step up": "25-100",
            "Step down": "25-100",
            "Pull": true,
            "Sticking": false
          },
          "Rope": {
            "Enabled": true,
            "Amount": "default",
            "Color": "Yellow, Teal, Red, Blue, Green, Purple",
            "EffectLine": "All",
            "Width": false,
            "Range": false,
            "Step up": "50-100",
            "Step down": "50-100",
            "Pull": true,
            "Sticking": "default"
          }
        }
      }
    }
  }
}
```

### Как читать правила группы

| Запись | Результат |
|---|---|
| Блок способности отсутствует или Enabled равен false | Способность закрыта для этой группы. Глобальный Defaults.<способность>.Enabled также должен быть включён. |
| Число или строка с числом | Фиксированное значение. Hook.Amount из примера даёт ровно 3 применения, изменить лимит игрок не может. Фиксированный безлимит задаётся числом -1. |
| Диапазон в кавычках | Игрок выбирает внутри пересечения диапазона группы и глобальных границ. При базовом безлимите Grab.Amount из примера начинает с 3. |
| Поле пропущено или указано "default" | Начальное значение берётся из Defaults, редактирование разрешено в глобальных пределах. |
| false у числа, цвета или линии | Фиксирует значение из Defaults. Например, ширина и дальность Rope закрыты для редактирования. |
| Color и EffectLine | "all" открывает весь список. Через запятую задаётся белый список. Цвета берутся из PaletteDef и PaletteHex; варианты линии — Beam, Laser, Lasso, Chain. Одновременно отображается одна линия. |
| Pull и Sticking | true включает и разрешает переключение, false выключает и блокирует. "default" наследует начальный режим и разрешает его менять. Для Hook.Pull режим Self в Defaults означает ручное подтягивание колесом. |

### Что регулирует каждое поле

| Поле | Где применяется | Поведение |
|---|---|---|
| Amount / Width | Все три | Количество успешных применений за раунд / ширина линии. Шаг ширины 0,5. |
| Range | Все три | Дальность зацепа Hook и Rope или поиска цели Grab. Шаг в меню 0,5. |
| Distance | Hook, Grab | Максимальный путь подтягивания игрока / предел отдаления удерживаемой цели. |
| Start.Distance | Grab | Начальная дистанция удержания при включённом Pull. Точка входит в имя ключа. |
| Strength | Grab | Сила броска цели по G. |
| Step up / Step down | Все три | Два независимых шага колеса. У Grab вверх отдаляет цель, вниз приближает. У Hook в ручном режиме и Rope с включённым Pull вверх укорачивает трос, вниз удлиняет. |
| Pull | Все три | Hook предлагает Авто / Сам / Откл. Grab притягивает пойманную цель к Start.Distance. Rope разрешает менять длину колесом, по умолчанию выключено. |
| Sticking | Все три | Включено — повторное нажатие отпускает. Выключено — нужен бинд на удержание. Подходящий бинд выводится из меню способности. |

После изменения vip.json примените конфигурацию VIPCore либо перезапустите сервер. Команда css_hgr_reload перечитывает серверный hgr.jsonc. Войдите игроком этой группы через **!vip → HGR → ИСП.** Для приведённого примера у Hook должно отображаться фиксированное 3, у Grab — выбор от 1 до 3 и две линии, у Rope — шесть цветов. При двух ролях выберите Vip в пункте «Приоритет настроек», чтобы увидеть ограничения группы.

При переходе со старой схемы замените Enabled: "On" на true, Size на Width, ThrowForce на Strength и CaptureDistance на Grab.Range. Старый Length соответствует Distance у Grab и Range у Hook/Rope. Для старого Amount: "0" выберите "default", если нужен редактируемый лимит с начальным значением из Defaults. Звуки задаются в hgr.jsonc. Для новых HEX в PaletteHex пересоберите CSS через pack.cmd и обновите клиентский аддон.

## Управление

- Входы: `!admin → Управление HGR` и `!vip → HGR → ИСП.`. Персональные уведомления общие для всех способностей выбранного профиля.
- В меню каждой способности есть «Бинды в чат». При залипании пример — `bind V css_grab`. Повторное нажатие отпускает цель. При удержании используйте пару alias из чата.
- Grab делает одну попытку. Промах расходует ноль зарядов. G бросает пойманную цель с настроенной силой. При выключенном Pull сохраняется дистанция захвата, даже за пределом Distance. До входа в обычный диапазон цель можно только приближать.
- Hook в режиме Авто удерживает игрока после подтягивания. В режиме Сам колесо меняет длину. У Rope колесо работает при включённом Pull. Направления вверх/вниз для Hook и Rope — ближе/дальше, для Grab — дальше/ближе. Колесо сохраняет переключение оружия.
- Для сравнения линий на следующем захвате используйте серверную команду `css_hgr_visual beam|laser|lasso|chain`. Значение default возвращает личный выбор.

## Сборка из исходников

Нужен .NET SDK 10. В корне репозитория:

```powershell
dotnet build HookGrabRope/HookGrabRope.csproj -c Release
.\pack.cmd
```

`pack.cmd` вызывает `pack.ps1`, собирает Release и упаковывает `HookGrabRope-CS2.zip`. Архив можно распаковать прямо в `game/csgo/`.

## EN

Run `pack.cmd`, deploy the archive's `addons` directory under `game/csgo` and merge `Content-addonmanager` into the Workshop addon sources. Rebuild and distribute the addon, including the sound event bank and loop-marked WAV. Change map to precache the resources. Preserve your existing `hgr.jsonc` during deployment so the plugin can back up a legacy configuration. Replace legacy VIP group rules with the `Values` example above. The server configuration is read-only after initialization. Personal changes are stored per SteamID and role. Use `css_hgr_reload` to reload settings.
