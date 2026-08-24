# Persistent SRB Smoke 1.0.1

Версия 1.0.1 сохраняет body-relative сегментную симуляцию дыма, но рендерит её встроенным прозрачным материалом KSP. Unity Editor, custom shader, AssetBundle, Waterfall и EVE не требуются.

## Что изменилось

- Один fixed pool из максимум 4 096 body-relative `TrailSegment` с Hermite-центрлайном.
- Горячее плотное сопло и холодный бело-серый след идут через один soft-ribbon путь: дым начинается внутри bell и не имеет разрыва у сопла.
- Сегменты добавляются по пройденной дистанции. На старте и при медленном liftoff масса дополнительно добавляется по времени, поэтому у неподвижной ракеты остаётся плотный smoke.
- Сохраняются ветер, buoyancy, расширение, dissipation, масштабирование SRB, дым на площадке, physics/rails warp и подавление stock smoke.
- Pad smoke — до 8 локальных логических полей 32³ с pressure-flow, а не набор визуальных частиц.
- Каждые 4 Гц старый след меняется под воздействием атмосферы и coarsen/merge. Merge сохраняет optical mass и импульс; сегменты разных аппаратов не смешиваются.
- Используется встроенный `Particles/Alpha Blended`, сгенерированная мягкая smoke texture, две camera-facing ленты на сегмент, сортировка сзади-вперёд и depth test о terrain/корпус.
- `VolumeTrailShadowLayer` берёт плотность сегментов напрямую и использует ограниченный cache высот terrain для мягких солнечных теней.

## Поддерживаемая конфигурация

Первый объёмный релиз поддерживает только:

- KSP 1.12.x;
- Windows x64;
- любой поддерживаемый KSP graphics API со встроенным прозрачным particle shader.

Если KSP не предоставляет встроенный прозрачный particle shader, эффект выключается с ясной записью в `KSP.log`.

## Установка

Распакуйте релиз в корень KSP. Должен получиться путь:

```text
<KSP_DIR>/GameData/PersistentSRBSmoke/Plugins/PersistentSRBSmoke.dll
```

Запускайте KSP обычным способом. Мод по-прежнему выключает stock smoke только у обнаруженных двигателей на `SolidFuel`; пламя и обычные engine effects не заменяются.

## Настройки

Редактируйте [`Settings.cfg`](GameData/PersistentSRBSmoke/PluginData/Settings.cfg). Новый корень и версия схемы:

```cfg
VOLUMETRIC_SRB_SMOKE
{
    schemaVersion = 2
}
```

Старый `Settings.cfg` 0.x не мигрируется. Если обнаружена старая корневая нода или несовместимая schema, мод игнорирует файл и использует чистые defaults v2. Возьмите шаблон из текущего релиза.

Профиль `Balanced` рассчитан на 1080p и 2–4 SRB: 1 024 видимых сегмента (256 near / 512 mid / 256 far).

## Сборка

Нужны KSP 1.12.x, .NET Framework 4.7 targeting pack и переменная окружения `KSP_DIR`.

```bat
set KSP_DIR=C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program
build.bat
```

`build.bat` собирает DLL и проверяет stock-рендерер; Unity и лицензия не нужны.

## Проверки

CI собирает DLL, проверяет контракт stock-рендерера и включает DLL в ZIP. Unity secrets не нужны.

Локальная проверка исходников:

```powershell
./tests/volumetric-smoke-contract.ps1
```

Детерминированные unit-тесты сегментных правил: `dotnet run --project tests/VolumetricSmoke.AlgorithmTests.csproj --configuration Release`. Полная in-game матрица приведена в [`tests/README.md`](tests/README.md).
