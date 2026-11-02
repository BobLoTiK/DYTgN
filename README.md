# net-suite

Один пульт для трёх сетевых штук, которые раньше жили сами по себе.

| компонент | что делает | где живёт |
|---|---|---|
| **zapret** (`winws.exe`) | обход DPI для Discord/YouTube, запускается службой `zapret` | `Documents\zapret-discord-youtube-1.9.9d` |
| **TgWsProxy** | MTProto → WebSocket мост для Telegram, `127.0.0.1:1443` | `%LOCALAPPDATA%\net-suite\bin\TgWsProxy_windows.exe` |
| **notion tunnel** | sing-box с TUIC-нодой, socks `127.0.0.1:10808` | `%LOCALAPPDATA%\notion-vpn` |
| **PAC server** | `http://127.0.0.1:10810/proxy.pac` — через туннель только домены Notion | `%LOCALAPPDATA%\notion-vpn\pacsrv.ps1` |

## Как пользоваться

- **«Сеть вкл»** на рабочем столе — поднимает всё: проверяет zapret, запускает TgWsProxy,
  поднимает туннель и PAC, а если нода Notion умерла — ищет новую.
- **«Сеть выкл»** — гасит TgWsProxy, туннель и PAC. zapret остаётся (это служба;
  чтобы гасить и её, нужно `netsuite.ps1 -Action off -IncludeZapret` из админской консоли).
- **«Сеть статус»** — таблица: кто жив, какой порт слушает, отвечает ли Notion.
- Автозапуск: ярлык `net-suite.lnk` в папке «Автозагрузка» вызывает `autostart.vbs`
  → `netsuite.ps1 -Action ensure` (тихий, идемпотентный проход).

Из консоли:

```powershell
powershell -ExecutionPolicy Bypass -File netsuite.ps1 -Action on
powershell -ExecutionPolicy Bypass -File netsuite.ps1 -Action status
powershell -ExecutionPolicy Bypass -File netsuite.ps1 -Action ensure
powershell -ExecutionPolicy Bypass -File netsuite.ps1 -Action restart
```

## Файлы

- `netsuite.ps1` — вся логика: `on` / `off` / `status` / `ensure` / `restart`
- `netsuite.json` — пути, имена процессов, порты. Пути записаны токенами
  `%LOCALAPPDATA%` / `%DOCUMENTS%` и разворачиваются в рантайме, поэтому файл остаётся
  ASCII и не ломается на кириллице в имени профиля
- `on.bat`, `off.bat`, `status.bat` — двойной клик
- `autostart.vbs` — тихий запуск `ensure` при входе в систему
- `shortcuts.ps1` — пересоздать ярлыки и запись в автозагрузке
  (файл сохранён с UTF-8 BOM: без него PowerShell 5.1 не читает кириллицу в именах ярлыков)
- `logs\netsuite-ГГГГММДД.log` — журнал

## Ограничения

- **zapret** требует администратора: его служба `zapret` создана установщиком
  `service.bat`. Без админа supervisor может только проверить, что `winws.exe` живой;
  если служба остановлена — поднять её не сможет и напишет предупреждение в лог.
- Если у zapret стоит другой профиль (`general*.bat`), менять его нужно там же, в его
  папке: этот репозиторий запуском winws не управляет, чтобы не ломать рабочую
  конфигурацию.
- Поиск новой ноды Notion занимает 1–3 минуты и делает это `watch.ps1` из папки
  `notion-vpn`; отдельная задача `NotionTunnelKeepalive` продолжает проверять
  туннель раз в 10 минут.
- Все порты слушают только `127.0.0.1` — наружу ничего не светится.
