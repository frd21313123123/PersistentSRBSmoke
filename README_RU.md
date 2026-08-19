# Persistent SRB Smoke

Графический мод для **Kerbal Space Program 1.12.x**, создающий сохраняющийся дымный след от твердотопливных ускорителей.

## v0.4.1: настоящий volumetric path

В v0.4.0 освещение fallback-частиц могло применяться дважды: к общей окраске материала и к процедурной текстуре. На тёмных engine-профилях это превращало весь след почти в чёрный. В v0.4.1 базовый серо/тёплый цвет двигателя снова является главным цветом дыма, а освещение создаёт только локальный контраст, тени и подсветку краёв.

Теперь есть два рендера.

### 1. True raymarched volume

Если в игре загружен shader `PersistentSRBSmoke/VolumetricSmoke`, обычный ParticleSystem продолжает симулировать положение, lifetime, time warp, ветер и pad-cloud physics, но его визуальный renderer выключается. Каждая активная частица рисуется как 3D proxy-volume и внутри него выполняется raymarching процедурного density field.

Raymarch shader реализует:

- 3D procedural FBM density + erosion;
- 24 primary ray samples по умолчанию;
- light/shadow marching в направлении Kerbol;
- Dual-Lobe Henyey-Greenstein (`0.85 / -0.35`);
- Beer-Lambert extinction;
- Beer-Powder response;
- multiple-scattering approximation;
- sky ambient + ground bounce;
- тёплое освещение у горизонта;
- high-albedo smoke: теневая часть становится серой/тёплой, а не угольно-чёрной;
- depth fade через `_CameraDepthTexture`.

Исходник шейдера находится в:

```text
Shaders/PersistentSRBVolumetricSmoke.shader
```

Для KSP он компилируется в `.shab` через **KSPBuildTools + Unity 2019.4.18f1** и загружается библиотекой **Shabby**. Workflow `.github/workflows/build-shaders.yml` уже настроен. Для автоматической сборки shader bundle в GitHub нужно один раз добавить repository secrets `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`.

### 2. Native 3D slice-volume fallback

Если `.shab`/Shabby отсутствует, мод больше не возвращается к старым шести карточкам в одной точке. Каждый cloudlet состоит из нескольких density-срезов, распределённых по X/Y/Z через весь объём облака. Это не raymarching, но это 3D slice-volume и выглядит существенно объёмнее старого billboard/cross-card варианта.

Fallback специально ограничивает затемнение:

```cfg
fallbackMinimumLight = 0.72
fallbackCoreShadow = 0.16
```

Поэтому освещение создаёт более тёмное ядро и светлый край, но не может сделать весь след чёрным.

## Основные настройки volumetric renderer

```cfg
volumetricLightingEnabled = true
volumetricScatteringForward = 0.85
volumetricScatteringBackward = -0.35
volumetricMultipleScattering = 0.55
volumetricSoftDepthFactor = 1.65
volumetricSunIntensity = 1.10
volumetricAmbientIntensity = 0.46
volumetricBeerPowderFactor = 0.72

raymarchedVolumetricEnabled = true
raymarchMaxCloudlets = 7000
raymarchSteps = 24
raymarchShadowSteps = 4
raymarchDensityMultiplier = 1.15
raymarchExtinction = 2.10

nativeVolumeSlicesPerAxis = 5
nativeVolumeSliceOpacity = 0.20
fallbackMinimumLight = 0.72
fallbackCoreShadow = 0.16
```

Если raymarching слишком тяжёлый, сначала уменьши `raymarchMaxCloudlets`, затем `raymarchSteps`, затем `raymarchShadowSteps`.

## Другие реализованные системы

- автоматический поиск `ModuleEngines` / `ModuleEnginesFX` с `SolidFuel`;
- разные профили для больших SRB и маленьких separation motors;
- suppression штатного дыма без отключения Waterfall/flame;
- непрерывность следа на большой скорости;
- world-space smoke + `FloatingOrigin`;
- expansion, diffusion, turbulence, buoyancy, wind shear;
- синхронизация с `Planetarium.GetUniversalTime()` при time warp;
- density-driven Shuttle-style pad cloud;
- soft particles / depth fade;
- процедурные текстуры/плотность без заимствованных ассетов.

## Установка

1. Скопируй `GameData/PersistentSRBSmoke` в `KSP/GameData`.
2. Для fallback больше ничего не требуется.
3. Для **true raymarching** дополнительно установи Shabby и положи собранный `PersistentSRBSmokeVolumetric.shab` в `GameData/PersistentSRBSmoke/Shaders/`.

Waterfall можно оставить установленным: он рисует факел, PersistentSRBSmoke — сохраняющийся дым.

## Сборка C#

```bat
set KSP_DIR=C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program
build.bat
```

GitHub Actions проверяет `Settings.cfg`, собирает Debug и Release с warnings-as-errors и создаёт установочный ZIP.
