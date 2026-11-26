# Сборка, генерация GIF, релизы и шпаргалка для следующих сессий

Этот файл — точный практический плейбук для сборки, тестирования, обновления превью и публикации релизов **Z-Overlay (ApexHUD)** на машине разработчика.

---

## 1. Окружение на этой машине

- **Проект:** `C:\Users\Aktan\Documents\ApexHUD` (не в `C:\Program Files\Setuper`, там нет прав записи без UAC).
- **SDK:** `.NET 6.0.428` SDK (`dotnet` в `PATH`). Node / Python / `ffmpeg` не установлены (и не нужны).
- **Inno Setup 6:** `C:\Users\Aktan\AppData\Local\Programs\Inno Setup 6\ISCC.exe`
- **GitHub CLI:** `gh` (авторизован под `astarkulov`, репозиторий `astarkulov/z-overlay`, ветка `main`).
- **Shell нюансы (Windows PowerShell 5.1):**
  - `&&` **не поддерживается** — разделять команды переносом строки и проверять `if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }`.
  - `ExecutionPolicy` стоит `Restricted`, поэтому для компиляции установщика вызывайте `ISCC.exe` напрямую вместо `.\installer\build-installer.ps1`.
- **Данные пользователя на диске:**
  - Настройки: `%AppData%\ApexHUD\settings.json`
  - Лог крашей: `%AppData%\ApexHUD\crash.log`

---

## 2. Стандартный цикл релиза (чек-лист)

> **Важное правило:** при каждом обновлении приложения всегда публикуем **новый версионированный релиз** на GitHub (`v1.1.12`, `v1.2.0` и т.д. с флагом `--latest`), прикрепляя **оба файла**: `dist\Z-Overlay-Setup-vX.Y.Z.exe` и `dist\ApexHUD.exe`. Старые теги не перезаписываем.

### Шаг 1. Поднять версию в 4 файлах
1. `ApexHUD.csproj` -> `<Version>X.Y.Z</Version>`
2. `installer\Z-Overlay.iss` -> `#define MyAppVersion "X.Y.Z"`
3. `Windows\HubWindow.xaml` -> `Text="ApexHUD X.Y.Z"` (в футере)
4. `README.md` -> ссылки на `Z-Overlay-Setup-vX.Y.Z.exe` + query-параметр `?v=X.Y.Z` на всех `<img>`, если обновились гифки/скриншоты (сбрасывает кэш GitHub Camo CDN!).

### Шаг 2. Собрать однофайловый `dist\ApexHUD.exe`
Перед сборкой обязательно остановить запущенный из `dist\ApexHUD.exe` процесс, иначе `GenerateBundle` упадёт с `file in use`:

```powershell
Set-Location "C:\Users\Aktan\Documents\ApexHUD"
Get-Process -Name ApexHUD -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq "C:\Users\Aktan\Documents\ApexHUD\dist\ApexHUD.exe" } | Stop-Process -Force
Start-Sleep -Milliseconds 300

dotnet publish ApexHUD.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o dist
```

### Шаг 3. Обновить PNG и живые GIF (если менялся UI)
Встроенный генератор `Core\Diagnostics\ScreenshotExporter.cs` рендерит и статические HiDPI PNG, и зацикленные GIF89a (с `NETSCAPE2.0` и dirty-rect сжатием — без `ffmpeg`!).

```powershell
$cap = Start-Process -FilePath "C:\Users\Aktan\Documents\ApexHUD\dist\ApexHUD.exe" -ArgumentList '--capture-screenshots', 'C:\Users\Aktan\Documents\ApexHUD\docs\screenshots' -Wait -PassThru
Write-Host "CaptureExit=$($cap.ExitCode)"
```

Выходные файлы в `docs\screenshots\`:
- `hero-overview.gif` / `.png`
- `standings.gif` / `.png`
- `relative.gif` / `.png`
- `inputs.gif` / `.png`
- `fuel-radar.gif` / `.png`
- `hub.png`

### Шаг 4. Smoke-тест запуска
```powershell
$crashLog = Join-Path $env:APPDATA "ApexHUD\crash.log"
if (Test-Path $crashLog) { Remove-Item $crashLog -Force }
$p = Start-Process -FilePath "C:\Users\Aktan\Documents\ApexHUD\dist\ApexHUD.exe" -PassThru
Start-Sleep -Milliseconds 2500
Write-Host "HasExited=$($p.HasExited) HasCrashLog=$(Test-Path $crashLog)"
```
Ожидаемый результат: `HasExited=False HasCrashLog=False`.

### Шаг 5. Собрать Windows-установщик Inno Setup 6
```powershell
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" "C:\Users\Aktan\Documents\ApexHUD\installer\Z-Overlay.iss"
```
Результат: `dist\Z-Overlay-Setup-vX.Y.Z.exe`.

### Шаг 6. Коммит, пуш и публикация GitHub Release
Папки `dist/`, `bin/`, `obj/` игнорируются в `.gitignore` — в git идут только исходники и `docs/screenshots/*`.

```powershell
Set-Location "C:\Users\Aktan\Documents\ApexHUD"
git add -A
git commit -m "vX.Y.Z: краткое описание изменений"
git push origin main

gh release create vX.Y.Z "dist\Z-Overlay-Setup-vX.Y.Z.exe" "dist\ApexHUD.exe" --title "Z-Overlay (ApexHUD) vX.Y.Z" --notes "Описание изменений" --latest
```

---

## 3. Известные грабли (уже решены — не сломать!)

1. **Конфликт пространств имён WPF и WinForms (`CS0104`):**
   - В `ApexHUD.csproj` включён `<UseWindowsForms>true</UseWindowsForms>`, но специально удалены неявные global using:
     `<Using Remove="System.Drawing" />` и `<Using Remove="System.Windows.Forms" />`.
   - Используйте явные алиасы `using Forms = System.Windows.Forms;` и `using Drawing = System.Drawing;`.
2. **Порядок инициализации в `HubWindow`:**
   - Слайдеры с `Minimum="0.70"` стреляют `ValueChanged` прямо во время `InitializeComponent()`.
   - Всегда держите `_syncingUi = true` до конца конструктора, присваивайте `_settings` **до** `InitializeComponent()` и проверяйте `if (_syncingUi || _settings == null) return;` во всех хендлерах.
3. **Запятые вместо точек на русской Windows:**
   - `CultureInfo.DefaultThreadCurrentCulture` влияет только на **новые** потоки, но НЕ на уже запущенный UI-поток!
   - Поэтому в `App.OnStartup` обязательно стоят **обе** пары: `Thread.CurrentThread.CurrentCulture/CurrentUICulture` и `DefaultThreadCurrentCulture/UICulture`.
4. **Пустые круги в Practice / Qualifying (`CarIdxBestLapTime == -1`):**
   - iRacing сбрасывает `CarIdxBestLapTime` в `-1`, когда машина заезжает в боксы.
   - Поэтому и `IRacingSdkReader.BuildStandings`, и `CornerSpeedTracker` подтягивают запасной `FastestTime` из YAML `SessionInfo -> Sessions -> ResultsPositions`.
5. **Оффскрин-рендер WPF (`ScreenshotExporter`):**
   - `RenderTargetBitmap.Render(child)` учитывает смещение `child` внутри родителя (`WrapPanel` давал чёрные кадры для 2–5 виджетов). Всегда рендерить через `VisualBrush(host)` в `DrawingVisual` с `Rect(0, 0, w, h)`.
   - Для записи реальных `DoubleAnimation` (слайды/вспышки `Standings`) хост должен быть примонтирован в показанное offscreen-окно (`Left = -24000`, `ShowInTaskbar = false`, `ShowActivated = false`) с прокачкой `DispatcherFrame`.
6. **Кэш картинок GitHub README:**
   - GitHub Camo кэширует старые GIF по URL. При обновлении гифок обязательно менять `?v=X.Y.Z` в `README.md`.

---

## 4. Идеи для следующих сессий (бэклог)

Если захочется развивать Z-Overlay дальше:
- **Track Map (карта трассы)** — построение контура трассы по координатам/секторам + цветные точки машин по классам и жёлтые зоны флагов.
- **Tires & Brakes** — температуры и износ шин (`LF/RF/LR/RR`), давление, температура тормозов/воды/масла (где доступно в IRSDK).
- **Pit Stop Helper** — отсчёт дистанции до своего бокса (полоса торможения в пит-боксе + таймер стоянки).
- **Автопроверка обновлений** — лёгкий запрос к GitHub Releases API при запуске с ненавязчивым бейджем в Control Hub («Доступна версия vX.Y.Z»).
- **Профили раскладки под машины** — автоматическое переключение раскладки виджетов в зависимости от текущей машины/класса (GT3 / Formula / Oval).
- **Настраиваемый хоткей Lock/Unlock** — возможность выбрать свою комбинацию вместо фиксированного `Ctrl + Shift + L`.
- **Head-to-Head / Battle Box** — компактный блок сравнения темпа с машиной прямо спереди и сзади.
