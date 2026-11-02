# net-suite

Один пульт для трёх сетевых штук, которые раньше жили сами по себе. Скачал папку,
запустил `install.bat` — дальше всё по ярлыкам.

| компонент | что делает | где живёт |
|---|---|---|
| **zapret** (`winws.exe`) | обход DPI для Discord/YouTube, запускается службой `zapret` | `Documents\zapret-discord-youtube-1.9.9d` (своя папка, не в репо) |
| **TgWsProxy** | MTProto → WebSocket мост для Telegram, `127.0.0.1:1443` | `bin\TgWsProxy_windows.exe` |
| **notion tunnel** | sing-box с TUIC-нодой, socks `127.0.0.1:10808` | `bin\sing-box.exe` + `notion\sb-config.json` |
| **PAC server** | `http://127.0.0.1:10810/proxy.pac` — через туннель только домены Notion | `notion\pacsrv.ps1` |

## Установка с нуля

1. Скачать папку/архив репозитория и распаковать куда угодно.
2. Двойной клик по `install.bat`. Он:
   - проверит движки в `bin\` (sing-box, xray, TgWsProxy) и докачает недостающие;
   - если нет `notion\sb-config.json` — найдёт рабочую бесплатную ноду (1–3 минуты);
   - создаст ярлыки на рабочем столе и запись в автозагрузке;
   - поставит задачу обновления ноды раз в 10 минут;
   - поднимет всё и покажет таблицу статуса.

Дальше нужен только рабочий стол: **«Сеть вкл»**, **«Сеть выкл»**, **«Сеть статус»**,
**«Notion»** — последний просто открывает Notion в Firefox.

## Как это работает с Firefox

У Firefox по умолчанию `network.proxy.type = 5` — «использовать системные настройки
прокси». Система читает `AutoConfigURL` из реестра, там наш PAC: домены с `notion`
идут через туннель, всё остальное — напрямую. Поэтому Firefox не нужно ни
перезапускать с флагами, ни создавать отдельный профиль: `netsuite.ps1` при каждом
проходе проверяет и восстанавливает `AutoConfigURL`, если его сбросили.

Отдельный профиль или второй экземпляр браузера не используются специально — чтобы
не открывалось лишних окон и не ломались твои настройки.

## Из консоли

```powershell
powershell -ExecutionPolicy Bypass -File netsuite.ps1 -Action on
powershell -ExecutionPolicy Bypass -File netsuite.ps1 -Action status
powershell -ExecutionPolicy Bypass -File netsuite.ps1 -Action ensure
powershell -ExecutionPolicy Bypass -File netsuite.ps1 -Action restart
```

## Файлы

- `netsuite.ps1` — вся логика: `on` / `off` / `status` / `ensure` / `restart`
- `netsuite.json` — пути, имена процессов, порты. Пути записаны токенами
  (`%REPO%`, `%LOCALAPPDATA%`, `%DOCUMENTS%`) и разворачиваются в рантайме, поэтому
  файл ASCII и не зависит от кириллицы в профиле
- `setup.ps1` + `install.bat` — установка/починка одной кнопкой
- `on.bat`, `off.bat`, `status.bat`, `notion-window.bat` — то, на что смотрят ярлыки
- `autostart.vbs` — тихий `ensure` при входе в систему
- `shortcuts.ps1` — пересоздать ярлыки и автозагрузку (UTF-8 BOM: без него PowerShell 5.1
  не читает кириллицу в именах ярлыков)
- `bin\` — движки: `sing-box.exe`, `xray.exe`, `TgWsProxy_windows.exe`
- `notion\` — рантайм туннеля: `sb-config.json`, `pacsrv.ps1`, `boot.vbs`, `watch.ps1`,
  `build-tuic.ps1` (поиск новой ноды), `build.ps1` (VLESS-фолбэк), `collect.ps1`
- `logs\` — журналы

## Ограничения

- **zapret** требует администратора: его служба `zapret` создана установщиком
  `service.bat` в его собственной папке. Без админа пульт только проверяет, что
  `winws.exe` живой, и пишет предупреждение в лог.
- Бесплатная нода живёт не вечно. Задача `NotionTunnelKeepalive` раз в 10 минут
  проверяет туннель и при смерти ноды запускает поиск новой. Если ноды не нашлось,
  `sb-config.json` останется старый, и `ensure` будет пробовать снова.
- Все порты слушают только `127.0.0.1` — наружу ничего не светится.
- `sb-config.json` — конфиг бесплатной публичной ноды, ничего личного.
