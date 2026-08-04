# SIMBA Runtime API

## SIMBAPlayer

`SIMBAPlayer` is the recommended public API. It is a facade over either `ShellMeshAnimator` or `LineMeshPlayer` and the associated `FieldColorController`.

### State

```csharp
bool IsLoaded
bool IsPlaying
GeometryType Geometry
int FrameCount
int CurrentFrame
int NextFrame
float FrameInterpolation
float FramesPerSecond
float Duration
float CurrentTime
string CurrentField
int CurrentFieldIndex
SIMBAColorMap CurrentColorMap
FieldColorController.RangeMode CurrentRangeMode
string[] AvailableFields
```

### Playback

```csharp
void Play()
void Pause()
void Stop()
void Restart()
void SetFrame(int frame)
void SetNormalizedTime(float normalizedTime)
void SetSpeed(float speed)
void SetLoop(bool loop)
void Reload()
```

`SetNormalizedTime` accepts values from 0 to 1.

### Field rendering

```csharp
bool SetField(string fieldName)
void SetField(int fieldIndex)
void SetColorMap(SIMBAColorMap colorMap)
void SetColorMap(Texture2D customTexture)
void UseGlobalRange()
void UsePerFrameRange()
void SetManualRange(float minimum, float maximum)
void SetMetallic(float value)
void SetSmoothness(float value)
```

Manual ranges saturate values outside the selected interval at the first or last colormap color.

### Events

```csharp
 event Action Loaded;
 event Action<int> FrameChanged;
 event Action<int, string> FieldChanged;
 event Action<SIMBAColorMap> ColorMapChanged;
 event Action PlaybackFinished;
```

Example:

```csharp
private void OnEnable()
{
    player.Loaded += OnLoaded;
    player.FrameChanged += OnFrame;
}

private void OnDisable()
{
    player.Loaded -= OnLoaded;
    player.FrameChanged -= OnFrame;
}
```

## FieldColorController

The controller remains public for advanced use. It owns the GPU field buffer, colormap, range and surface appearance. Most applications should access it through `SIMBAPlayer`.

## SIMBAUtilities

```csharp
string path = SIMBAUtilities.CaptureScreenshot();
```

By default screenshots are saved under `Application.persistentDataPath/SIMBA Screenshots`.

## UI and XR integration

UnityEvents can call the public methods directly. For enum values such as colormaps, use a small adapter method:

```csharp
public void SelectPlasma()
{
    player.SetColorMap(SIMBAColorMap.Plasma);
}
```

The API does not depend on a specific interaction package and can therefore be used with mouse controls, the Input System, XR Interaction Toolkit or Meta interaction components.
