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
- Процедурная shape/detail-маска дыма с эрозией краёв и Beer–Lambert-подобной плотностью.
- Автоматическая интеграция с уже установленным EVE Volumetric Clouds: используется его
  загруженный cloud-volume particle shader без копирования или поставки файлов EVE.
- Без EVE автоматически остаётся автономный procedural cloudlet renderer.

## Что изменено для производительности

Текущий рендер всё ещё основан на Unity Shuriken, но самые дорогие места ограничены:

- Perlin-шум ветра теперь вычисляется по сетке высот один раз за dynamic update, а не несколько раз для каждой частицы.
- Старые частицы обновляют динамическую скорость реже свежих.
- Далёкие частицы получают дополнительный LOD по частоте обновления.
- Полностью невидимый след обновляет ветер и поток реже, продолжая двигаться по сохранённой скорости.
- Между такими обновлениями Unity продолжает двигать частицы по уже рассчитанной скорости.
- Один cloudlet по умолчанию состоит из 3 пересекающихся прозрачных quad вместо прежних 6.
- Сортировка десятков тысяч прозрачных частиц по расстоянию выключена по умолчанию.
- Для дыма отключены ненужные shadow/probe/motion-vector проходы; mesh instancing включается, когда его поддерживает shader.
- Большие группы ускорителей делят число сэмплов с компенсацией оптической плотности по Beer–Lambert, а не получают тонкие следы.
- Убраны лишние временные коллекции при периодическом поиске двигателей.
- Поиск stock-smoke компонентов кэшируется, а тяжёлый reflection reset больше не выполняется каждый кадр.

Визуальные параметры возвращены к виду v0.6.1 (`maxParticles = 48000`, три плоскости), а тяжёлая динамика выполняется с частотой `4 Hz`, тени — `8 Hz`.

## Ограничение текущего рендера

Это пока **не отдельный полноценный chunked raymarch renderer**. С установленным EVE след использует его объёмный cloud-particle shader; без EVE каждый клуб остаётся небольшим mesh из пересекающихся прозрачных плоскостей. В обоих режимах плотность нормализуется по Beer–Lambert, поэтому изменение количества плоскостей больше не меняет общую непрозрачность следа. В fallback-режиме очень плотный след всё ещё может упираться в transparent overdraw.

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
maxParticles = 48000
dynamicMotionHz = 4
offscreenDynamicMotionHz = 0.5
engineScanInterval = 2
cloudletPlanes = 3
sortParticles = false

preferEveVolumetricShader = true
volumetricDensity = 1.05
volumetricMinScatter = 0.82
volumetricSoftDepth = 0.008

dynamicMidAge = 0.20
dynamicOldAge = 0.55
dynamicMidStride = 3
dynamicOldStride = 8
dynamicFarDistance = 3500
dynamicFarStrideMultiplier = 3

adaptiveParticleCulling = false
fullDensityEmitterBudget = 8
minimumEmitterDensityScale = 0.35
windCacheLayers = 64
```

Если FPS проседает, сначала уменьшай `lifetime` или `maxParticles`. Не включай `adaptiveParticleCulling`: старый age-only алгоритм разрушал видимую плотность. Для множества ускорителей теперь используется компенсированный бюджет эмиссии.

`preferEveVolumetricShader` не устанавливает EVE и не загружает файлы из приложенного архива.
Он только подключается к shader registry уже установленного EVE. На Windows/D3D11 используется
procedural fallback: EVE выводит этот shader через закрытый off-screen compositor, несовместимый с
Unity Shuriken. Выбранный режим и причина всегда записываются в `KSP.log`.

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
