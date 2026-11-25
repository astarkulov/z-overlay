# Z-Overlay (ApexHUD) — Native Telemetry Overlay for iRacing

![.NET 6](https://img.shields.io/badge/.NET-6.0%20Windows-512BD4?style=flat-square)
![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D6?style=flat-square)
![Telemetry](https://img.shields.io/badge/Telemetry-60%20Hz%20Native%20IRSDK-22C55E?style=flat-square)
![Size](https://img.shields.io/badge/Portable%20EXE-~345%20KB-3B82F6?style=flat-square)
![License](https://img.shields.io/badge/License-MIT-94A3B8?style=flat-square)

Ультра-лёгкий, нативный и бесплатный оверлей телеметрии для **iRacing** на **C# / .NET 6 (WPF + прямое чтение Shared Memory на 60 Гц)** — без Electron, без Chromium, без подписок и без просадок FPS.

Современный полупрозрачный стиль **Carbon Glass** с раздельной прозрачностью подложек (текст, графики и цвета классов остаются 100% чёткими), векторными иконками марок авто и полным набором боевых фич уровня Racelab.

---

## Виджеты

### Relative
- Ближайшие машины спереди и сзади с точными отрывами в секундах (`CarIdxEstTime`)
- Векторные иконки марок авто (**Porsche, Ferrari, BMW, Mercedes-AMG, Audi, McLaren, Lamborghini, Ford, Chevrolet/Corvette, Aston Martin, Acura, Honda, Cadillac, Toyota, Mazda, Dallara, Hyundai, Lotus, Formula**)
- Разница темпа относительно тебя (`-0.4s` красным, если накатывает / `+0.5s` зелёным, если медленнее)
- Раскраска круговых (`Cyan`) и обгоняющих на круг (`Red/Rose`), статусы `PIT`, `OUT` (выезд на холодной резине), `OFF`
- Бейджи лицензии / Safety Rating, iRating, SOF, инциденты, температура трека/воздуха, `WET` и флаги

### Standings (Multiclass)
- Мультикласс с расчётом **SOF** по каждому классу и живым прогнозом **`±iR`** (формула Elo iRacing)
- Отыгранные/потерянные позиции со старта (`▲2` / `▼1`), векторные иконки марок, бейджи типа резины (`S / M / H / W / D`)
- Переключение **Gap к лидеру / Interval до машины впереди**
- Фиолетовая подсветка лучшего круга класса, количество пит-стопов и длина текущего стинта (`P1·8` / `L11`)

### Input Telemetry
- 60 FPS аппаратный график газа / тормоза / сцепления с полосами срабатывания **ABS** и настраиваемой длиной окна (**`1.0 – 12.0 сек`**)
- **Apex Min Speed** — автоматическая фиксация и удержание минимальной скорости в повороте (`MIN 94 km/h`)
- 12-сегментный LED шифт-бар с вспышкой на отсечке, вертикальные бары педалей с `%`, передача / скорость / RPM и векторный индикатор угла руля
- Нижняя полоска электроники машины (**`BB`**, **`TC`**, **`ABS`**) + встроенный **Delta Bar** к лучшему кругу

### Fuel Strategy
- Точный трекер расхода (фильтрует пит-стопы и неполные круги) с 3 режимами расчёта: **`AVG 5L` / `LAST` / `MAX (Safe)`**
- 3-колоночная матрица долива: **`MIN (+0L)` | `ADD (+margin)` | `SAFE (+1.5L)`**
- Оставшиеся круги на баке vs круги до финиша, счётчик пит-стопов (`NO STOP` / `1 STOP`), примерное время дозаправки (`PIT FILL ~4.8s`) и целевой расход на экономию (`SAVE`)

### Radar / Blind Spot
- Минималистичный радар с дугами **`CAR LEFT` / `CAR RIGHT` / `3 WIDE`**, задней дугой приближения (<10 м) и дистанцией в метрах
- **Автоскрытие на чистой трассе** в Lock-режиме — 0 пикселей в центре экрана, пока рядом никого нет

---

## Управление, кастомизация и мультимонитор
- **Один портативный `.exe` (~345 КБ)** — не требует установки, работает поверх игры без перехвата фокуса (`WS_EX_TRANSPARENT | WS_EX_NOACTIVATE`)
- **Автопоказ только при активном iRacing** — мгновенно скрывает все виджеты при `Alt+Tab` или сворачивании игры
- **Ресайз за край мышкой** — в режиме редактирования тяни за правый край, нижний край или правый нижний угол (от `55%` до `250%`), плюс общий масштаб до `220%`
- **Позиционирование и мультимонитор:** выбор разрешённых мониторов, жёсткое ограничение в рамках экрана, сетка привязки **3×3** (углы/края/центр), магнитное прилипание (`≤ 14 px`) и сдвиг стрелками (`↑ ↓ ← →`, с `Shift` — 10 px)
- **Выбор шрифтов:** 4 motorsport-пресета (`Modern`, `Bahnschrift DIN`, `Rajdhani F1`, `Exo 2`) + любой установленный в Windows шрифт отдельно для текста и цифр
- **Встроенный Demo Mode (60 Гц)** — полная симуляция мультикласса GT3 + LMP2 в Спа для удобной настройки без запуска iRacing

---

## Быстрый старт

1. Скачай `ApexHUD.exe` из раздела **[Releases](https://github.com/astarkulov/z-overlay/releases)** (нужен [.NET 6 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)).
2. Запусти `ApexHUD.exe` — со старта включён **Демо-режим (60 Гц)**, чтобы сразу расставить виджеты, подобрать масштаб, прозрачность и шрифты.
3. Нажми **Закрепить (`Ctrl + Shift + L`)** — все виджеты станут click-through.
4. Выключи **Демо-режим** в сайдбаре для перехода в **Live iRacing**.
5. В настройках графики iRacing включи **Borderless Windowed** (`Full Screen = off`, `Border = off`).

Настройки и позиции окон автоматически сохраняются в `%AppData%\ApexHUD\settings.json`.

---

## Сборка из исходников

Требуется [.NET 6 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/6.0) (Windows x64):

```powershell
git clone https://github.com/astarkulov/z-overlay.git
cd z-overlay
dotnet publish ApexHUD.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o dist
```

Готовый однофайловый бинарник появится в `dist\ApexHUD.exe`.
