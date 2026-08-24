# Persistent SRB Smoke 1.0

Начиная с 1.0 мод полностью заменяет прежний Shuriken/cloudlet, Waterfall и EVE-proxy рендеринг на собственный объёмный SRB smoke для KSP 1.12.x.

## Что изменилось

- Один fixed pool из максимум 4 096 body-relative `TrailSegment` с Hermite-центрлайном.
- Горячее плотное сопло и холодный бело-серый след идут через один raymarch-путь: дым начинается внутри bell и не имеет разрыва у сопла.
- Сегменты добавляются по пройденной дистанции. На старте и при медленном liftoff масса дополнительно добавляется по времени, поэтому у неподвижной ракеты остаётся плотный smoke.
- Сохраняются ветер, buoyancy, расширение, dissipation, масштабирование SRB, дым на площадке, physics/rails warp и подавление stock smoke.
- Pad smoke — до 8 локальных логических полей 32³ с pressure-flow, а не набор визуальных частиц.
- Каждые 4 Гц старый след меняется под воздействием атмосферы и coarsen/merge. Merge сохраняет optical mass и импульс; сегменты разных аппаратов не смешиваются.
- D3D11 compute shader строит tile list 16×16 px (до 64 кандидатов), raymarch пропускает пустые интервалы, а depth сцены клипует дым о terrain и корпус.
- Используются Beer–Lambert transmittance, двухлепестковая phase function, 3D noise, освещение от Солнца, weighted-blended OIT composite и half-resolution temporal reconstruction.
- `VolumeTrailShadowLayer` берёт плотность сегментов напрямую и использует ограниченный cache высот terrain для мягких солнечных теней.

## Поддерживаемая конфигурация

Первый объёмный релиз поддерживает только:

- KSP 1.12.x;
- Windows x64;
- Direct3D 11 с compute shader support.

На другом graphics API, при отсутствии bundle или несовместимом bundle эффект выключается. Причина явно записывается в `KSP.log`. Legacy particle fallback намеренно отсутствует.

## Установка

Распакуйте релиз в корень KSP. Должен получиться путь:

```text
<KSP_DIR>/GameData/PersistentSRBSmoke/PluginData/VolumetricSmoke-WindowsD3D11.bundle
```

Запускайте KSP в режиме Windows/D3D11. Мод по-прежнему выключает stock smoke только у обнаруженных двигателей на `SolidFuel`; пламя и обычные engine effects не заменяются.

## Настройки

Редактируйте [`Settings.cfg`](GameData/PersistentSRBSmoke/PluginData/Settings.cfg). Новый корень и версия схемы:

```cfg
VOLUMETRIC_SRB_SMOKE
{
    schemaVersion = 2
}
```

Старый `Settings.cfg` 0.x не мигрируется. Если обнаружена старая корневая нода или несовместимая schema, мод игнорирует файл и использует чистые defaults v2. Возьмите шаблон из текущего релиза.

Профиль `Balanced` рассчитан на 1080p и 2–4 SRB: 1 024 видимых сегмента (256 near / 512 mid / 256 far), 24/14/8 view samples и 4 sun-shadow sample для near/mid.

## Сборка

Нужны KSP 1.12.x, .NET Framework 4.7 targeting pack, Unity **2019.4.18f1** с Windows Build Support и переменные окружения `KSP_DIR`, `UNITY_PATH`.

```bat
set KSP_DIR=C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program
set UNITY_PATH=C:\Program Files\Unity\Hub\Editor\2019.4.18f1\Editor\Unity.exe
build.bat
```

Собрать только D3D11 bundle:

```powershell
./scripts/build-volumetric-assets.ps1
```

Исходный Unity-проект находится в [`unity/VolumetricSmokeAssets`](unity/VolumetricSmokeAssets) и фиксирован на версии Unity KSP 1.12.x.

## Проверки

CI собирает Windows/D3D11 bundle, DLL, проверяет контракт shader assets и включает bundle в ZIP. Для Unity activation в GitHub Actions нужны secrets `UNITY_LICENSE`, `UNITY_EMAIL` и `UNITY_PASSWORD`.

Локальная проверка исходников:

```powershell
./tests/volumetric-smoke-contract.ps1
```

Детерминированные unit-тесты сегментных правил: `dotnet run --project tests/VolumetricSmoke.AlgorithmTests.csproj --configuration Release`. Полная in-game матрица приведена в [`tests/README.md`](tests/README.md).
