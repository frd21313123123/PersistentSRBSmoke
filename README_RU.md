# Persistent SRB Smoke

Графический мод для **Kerbal Space Program 1.12.x**, создающий длинный сохраняющийся дымный след от твердотопливных ускорителей.

## Что уже реализовано

- Автоматический поиск `ModuleEngines` / `ModuleEnginesFX`, использующих `SolidFuel`.
- Поддержка всех загруженных аппаратов, включая отделившиеся, но ещё горящие SRB.
- Отдельная эмиссия из каждого `thrustTransform`.
- World-space частицы с регистрацией в KSP `FloatingOrigin`.
- Заполнение следа по пройденному расстоянию, чтобы на высокой скорости не появлялись большие разрывы.
- Масштабирование количества, размера, времени жизни, прозрачности и расстояния между клубами в зависимости от тяги двигателя.
- Синхронизация старения и движения дыма с KSP Universal Time при time warp.
- Ветер с изменением направления и скорости по высоте.
- Density-driven модель стартового облака: плотный дым растекается в стороны у площадки, внешние области получают подъём.
- Подавление stock/legacy SRB smoke без отключения Waterfall и факела двигателя.
- Процедурная текстура дыма, создаваемая во время запуска.

## Что изменено для производительности

Текущий рендер всё ещё основан на Unity Shuriken, но самые дорогие места ограничены:

- Perlin-шум ветра теперь вычисляется по сетке высот один раз за dynamic update, а не несколько раз для каждой частицы.
- Старые частицы обновляют динамическую скорость реже свежих.
- Далёкие частицы получают дополнительный LOD по частоте обновления.
- Между такими обновлениями Unity продолжает двигать частицы по уже рассчитанной скорости.
- Один cloudlet по умолчанию состоит из 3 пересекающихся прозрачных quad вместо прежних 6.
- Сортировка десятков тысяч прозрачных частиц по расстоянию выключена по умолчанию.
- Убраны лишние временные коллекции при периодическом поиске двигателей.
- Поиск stock-smoke компонентов кэшируется, а тяжёлый reflection reset больше не выполняется каждый кадр.

Сбалансированные значения по умолчанию: `maxParticles = 36000` и `dynamicMotionHz = 6`.

## Ограничение текущего рендера

Это пока **не настоящий volumetric smoke**. Каждый клуб остаётся небольшим mesh из пересекающихся прозрачных плоскостей с alpha-текстурой. Поэтому при очень плотном следе GPU всё ещё может упираться в transparent overdraw.

Следующий крупный этап — chunked volumetric renderer с density volume, raymarching, Beer-Lambert extinction, фазовой функцией рассеяния, self-shadowing, depth-aware смешиванием и temporal accumulation. План находится в [`docs/VOLUMETRIC_ROADMAP.md`](docs/VOLUMETRIC_ROADMAP.md).

## Установка

1. Скачай `PersistentSRBSmoke-v*.zip` со страницы Releases.
2. Распакуй архив в корневую папку Kerbal Space Program или перенеси папку `PersistentSRBSmoke` в `GameData/`.
3. Итоговый путь должен быть `<KSP_DIR>/GameData/PersistentSRBSmoke/`.

## Настройки

Файл:

```text
GameData/PersistentSRBSmoke/PluginData/Settings.cfg
```

Основные параметры производительности:

```cfg
maxParticles = 36000
dynamicMotionHz = 6
engineScanInterval = 2
cloudletPlanes = 3
sortParticles = false

dynamicMidAge = 0.20
dynamicOldAge = 0.55
dynamicMidStride = 2
dynamicOldStride = 4
dynamicFarDistance = 5000
dynamicFarStrideMultiplier = 2

windCacheLayers = 96
```

Если FPS проседает, сначала уменьшай `maxParticles`, `lifetime`, `particlesPerMeter` и `dynamicMotionHz`. Если упор именно в GPU, оставляй `cloudletPlanes = 3` и `sortParticles = false`.

## Сборка DLL

Нужны:

- KSP 1.12.x;
- Visual Studio 2022;
- workload `.NET desktop development`;
- переменная окружения `KSP_DIR`, указывающая на папку KSP.

Пример Steam:

```bat
set KSP_DIR=C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program
build.bat
```

Проект берёт настоящие KSP/Unity DLL из:

```text
%KSP_DIR%\KSP_x64_Data\Managed
```

После сборки DLL копируется в:

```text
%KSP_DIR%\GameData\PersistentSRBSmoke\Plugins\PersistentSRBSmoke.dll
```

Не забудь также скопировать папку `GameData/PersistentSRBSmoke`, чтобы присутствовал `PluginData/Settings.cfg`.

## GitHub Actions

Workflow `.github/workflows/build.yml` собирает DLL и установочный ZIP при push в `main`, pull request и ручном запуске. GitHub Release создаётся только для тега `v*` либо при ручном запуске с явно включённым `create_release`.

Для CI используются публичные skeleton-сборки KSP 1.11.2 только как compile-time references; в архив мода они не попадают. Локальная сборка с `KSP_DIR` продолжает использовать настоящие DLL KSP 1.12.x.
