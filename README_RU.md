# Persistent SRB Smoke

Графический мод для **Kerbal Space Program 1.12.x**, создающий длинный сохраняющийся дымный след от твердотопливных ускорителей. Версия **v0.4** добавляет динамическое объемное освещение, фазовое рассеяние света и мягкие пересечения с геометрией.

## Объемное освещение v0.4

Дым больше не освещается одинаково со всех сторон. Перед каждым кадром мод рассчитывает положение Kerbol, направление камеры и локальную вертикаль планеты.

Реализовано:

- **Directional Kerbol Lighting** — направление на Солнце и его высота над горизонтом.
- **Beer-Lambert attenuation** — прямой солнечный свет ослабляется атмосферой; около горизонта он становится слабее и теплее.
- **Sky Ambient** — рассеянный свет небесного купола, чтобы теневая сторона облака не была полностью чёрной.
- **Ground Bounce** — более слабый отражённый свет от поверхности планеты.
- **Dual-Lobe Henyey-Greenstein**:
  - `g = 0.85` — сильное forward scattering и эффект **Silver Lining**, когда дым подсвечен сзади;
  - `g = -0.35` — мягкий backward scattering при освещении со стороны камеры.
- **Multiple Scattering approximation** — плотные внутренние области получают небольшую долю повторно рассеянного света.
- **Beer-Powder approximation** — сочетание поглощения света и «порошкового» восстановления освещения внутри плотного дыма.
- **Spherical Pseudo-Normals** — RGB процедурной текстуры динамически пересчитывается по UV как поверхность условной сферы, поэтому клубы получают округлый светотеневой градиент.
- **Soft Particles** — включается depth fade штатного KSP particle shader через `_CameraDepthTexture`, `_InvFade` и `SOFTPARTICLES_ON`. Дым плавно пересекается с землёй, стартовой площадкой и корпусом ракеты вместо резкого среза.
- **Dynamic Fallback** — если в игре найден shader `PersistentSRBSmoke/VolumetricSmoke`, параметры освещения автоматически отправляются в него. Если его нет, используется штатный KSP shader и CPU-пересвет процедурной текстуры. Поэтому внешний shader-loader не является обязательной зависимостью.

## Остальные реализованные системы

- Автоматический поиск `ModuleEngines` / `ModuleEnginesFX`, использующих `SolidFuel`.
- Поддержка всех загруженных аппаратов, включая отделившиеся горящие SRB.
- Эмиссия из каждого `thrustTransform`.
- World-space частицы и поддержка KSP `FloatingOrigin`.
- Заполнение следа по пройденному расстоянию — на большой скорости он не должен превращаться в пунктир.
- Разный профиль дыма для разных двигателей: маленькие separation motors создают меньше, более мелкий, тёмный и короткоживущий дым.
- Подавление штатного/старого SRB-дыма без отключения Waterfall или факела двигателя.
- Расширение, затухание, turbulence, buoyancy и wind shear по высоте.
- Синхронизация с `Planetarium.GetUniversalTime()`: при time warp дым стареет, расширяется и перемещается быстрее вместе с игровым временем.
- Near-ground hold и отдельный density-driven pad cloud: плотный дым у площадки расталкивается в стороны, а внешние разреженные клубы поднимаются вверх.
- Процедурная текстура плотности генерируется самим модом.

## Установка

1. Скачай `PersistentSRBSmoke-v*.zip` из Releases или тестовый artifact из GitHub Actions.
2. Распакуй архив в корень KSP либо перенеси папку `PersistentSRBSmoke` в `GameData/`.
3. Итоговый путь: `<KSP_DIR>/GameData/PersistentSRBSmoke/`.

Waterfall можно оставить установленным: Waterfall рисует факел/струю двигателя, PersistentSRBSmoke — сохраняющийся дымный след.

## Настройки объемного света

Файл:

```text
GameData/PersistentSRBSmoke/PluginData/Settings.cfg
```

Стандартные параметры v0.4:

```cfg
volumetricLightingEnabled = true
volumetricScatteringForward = 0.85
volumetricScatteringBackward = -0.35
volumetricMultipleScattering = 0.55
volumetricSoftDepthFactor = 1.65
volumetricSunIntensity = 1.10
volumetricAmbientIntensity = 0.46
volumetricBeerPowderFactor = 0.72
```

Что они делают:

- `volumetricScatteringForward` — сила направленного рассеяния. Чем ближе к `1`, тем сильнее светлый контур при контровом свете.
- `volumetricScatteringBackward` — обратная доля рассеяния при взгляде по направлению света.
- `volumetricMultipleScattering` — насколько сильно свет заполняет плотную внутреннюю часть облака.
- `volumetricSoftDepthFactor` — ширина мягкого перехода при пересечении с геометрией. Большее значение даёт более плавный fade.
- `volumetricSunIntensity` — сила прямого света Kerbol.
- `volumetricAmbientIntensity` — интенсивность рассеянного света неба и поверхности.
- `volumetricBeerPowderFactor` — баланс между затемнением плотного дыма и внутренним порошковым рассеянием.

Если Silver Lining слишком яркий — уменьши `volumetricSunIntensity` или `volumetricScatteringForward`. Если теневая сторона слишком чёрная — увеличь `volumetricAmbientIntensity` или `volumetricMultipleScattering`.

## Сборка DLL

Нужны:

- KSP 1.12.x;
- Visual Studio 2022;
- workload **.NET desktop development**;
- `KSP_DIR`, указывающий на папку игры.

```bat
set KSP_DIR=C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program
build.bat
```

Проект использует DLL из:

```text
%KSP_DIR%\KSP_x64_Data\Managed
```

После сборки DLL автоматически копируется в:

```text
%KSP_DIR%\GameData\PersistentSRBSmoke\Plugins\PersistentSRBSmoke.dll
```

## Автоматическая проверка GitHub Actions

CI теперь выполняет полный набор проверок:

1. проверяет структуру `Settings.cfg` и наличие всех volumetric-параметров;
2. восстанавливает публичные KSP skeleton references;
3. собирает **Debug** с предупреждениями как ошибками;
4. собирает **Release** с предупреждениями как ошибками;
5. упаковывает Release DLL и `GameData` в установочный ZIP.

Skeleton-сборки используются только для компиляции в CI и не попадают в архив мода. Локальная сборка через `KSP_DIR` использует настоящие DLL KSP 1.12.x.

## Производительность

v0.4 пока не делает дорогой полноэкранный raymarching через одну гигантскую 3D density texture. Сохраняется система cloudlet-частиц, но поверх неё добавлены физически мотивированные оптические расчёты и динамический пересвет процедурной текстуры с ограниченной частотой обновления. Это позволяет сохранить длинный след из десятков тысяч частиц без резкого увеличения нагрузки на GPU.

При проблемах с FPS сначала уменьшай `maxParticles`, `lifetime`, `particlesPerMeter` и `dynamicMotionHz`.
