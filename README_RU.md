# Persistent SRB Smoke

Первая рабочая реализация отдельного графического мода для **Kerbal Space Program 1.12.x**, создающего длинный сохраняющийся дымный след от твердотопливных ускорителей.

## Уже реализовано

- Автоматический поиск `ModuleEngines` / `ModuleEnginesFX`, использующих `SolidFuel`.
- Поддержка всех загруженных аппаратов, включая отделившиеся, но ещё горящие SRB.
- Отдельные точки эмиссии для каждого `thrustTransform` двигателя.
- World-space частицы: дым остаётся в том месте, где прошла ракета.
- Регистрация ParticleSystem в KSP `FloatingOrigin`, чтобы длинный след корректно переживал сдвиг начала координат.
- Заполнение следа по **пройденному расстоянию**, поэтому на большой скорости он не должен становиться пунктиром.
- Время жизни по умолчанию 150 секунд.
- Постепенное сильное расширение старых клубов дыма.
- Плавное рассеивание/исчезновение.
- Базовая турбулентность через Unity ParticleSystem Noise.
- Небольшой боковой дрейф и подъём дыма.
- Зависимость количества/прозрачности дыма от давления атмосферы.
- В вакууме дым не создаётся.
- Процедурная текстура дыма создаётся самим модом во время запуска — никаких ассетов другого мода не используется.
- Совместимость по архитектуре с Waterfall: Waterfall рисует факел, этот мод — сохраняющийся дым.

## Пока не реализовано

Следующие этапы:

1. Настоящие слои ветра по высоте и wind shear.
2. Более сложная 3D-турбулентность старого дыма.
3. Собственный depth-aware shader и освещение от Солнца/двигателей.
4. Столкновение выхлопа с землёй и растекание облака по стартовой площадке.
5. LOD: объединение старых далёких клубов для экономии FPS.
6. Меню настроек прямо в игре.
7. Пресеты Performance / Realistic / Cinematic / Shuttle.
8. Поддержка нестандартных твёрдых топлив из модов через конфиг.

## Установка

1. Скачайте архив `PersistentSRBSmoke-v*.zip` со страницы [Releases](https://github.com/frd21313123123/PersistentSRBSmoke/releases).
2. Распакуйте архив в корневую папку Kerbal Space Program (или перенесите папку `PersistentSRBSmoke` в вашу папку `GameData/`).
3. Итоговый путь к моду должен выглядеть как `<KSP_DIR>/GameData/PersistentSRBSmoke/`.

## Сборка DLL

Нужны:

- установленный KSP 1.12.x;
- Visual Studio 2022;
- workload **.NET desktop development**;
- переменная окружения `KSP_DIR`, указывающая на папку KSP.

Пример Steam:

```bat
set KSP_DIR=C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program
build.bat
```

Проект берёт KSP/Unity DLL из:

```text
%KSP_DIR%\KSP_x64_Data\Managed
```

После успешной сборки `PersistentSRBSmoke.dll` автоматически копируется в:

```text
%KSP_DIR%\GameData\PersistentSRBSmoke\Plugins\
```

Также скопируй папку:

```text
GameData\PersistentSRBSmoke
```

из проекта в `GameData` игры.

## Настройка эффекта

Файл:

```text
GameData/PersistentSRBSmoke/PluginData/Settings.cfg
```

Основные параметры:

```cfg
lifetime = 150
baseEmissionRate = 24
particlesPerMeter = 0.22
startSize = 2.8
sizeGrowth = 9.0
opacity = 0.72
turbulenceStrength = 0.65
maxParticles = 16000
```

Для более массивного Shuttle-подобного следа можно попробовать:

```cfg
lifetime = 180
baseEmissionRate = 30
particlesPerMeter = 0.28
startSize = 3.2
sizeGrowth = 10.5
opacity = 0.76
turbulenceStrength = 0.8
maxParticles = 24000
```

Если FPS проседает, в первую очередь уменьшай `maxParticles`, `lifetime` и `particlesPerMeter`.


## Автосборка GitHub Actions

Workflow `.github/workflows/build.yml` автоматически собирает DLL и установочный ZIP при push в `main`, pull request и ручном запуске. При создании тега вида `v0.1.0` workflow также создаёт GitHub Release и прикладывает ZIP.

Для CI используются публичные skeleton-сборки KSP 1.11.2 только как compile-time references; они не попадают в архив мода. Локальная сборка с `KSP_DIR` по-прежнему использует настоящие DLL установленного KSP 1.12.x.
