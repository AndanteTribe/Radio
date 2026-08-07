# Radio

[![unity-meta-file-check](https://github.com/AndanteTribe/Radio/actions/workflows/unity-meta-file-check.yml/badge.svg)](https://github.com/AndanteTribe/Radio/actions/workflows/unity-meta-file-check.yml)
[![Releases](https://img.shields.io/github/release/AndanteTribe/Radio.svg)](https://github.com/AndanteTribe/Radio/releases)
[![GitHub license](https://img.shields.io/github/license/AndanteTribe/Radio.svg)](./LICENSE)
[![openupm](https://img.shields.io/npm/v/jp.andantetribe.radio?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/jp.andantetribe.radio/)

English | [日本語](README_JA.md)

## Overview

**Radio** is a Unity audio playback library for BGM, sound effects, and voice.

`AudioPlayer` manages multiple `AudioSource` channels and plays `AudioClip` instances directly. Use `AddressableAudioPlayer` only when Addressables playback is needed. Its BGM handles are retained through [AssetsRegistry](https://github.com/AndanteTribe/AssetsRegistry) and released by `StopAllBgm` or `Dispose`.

Optionally, when [LitMotion](https://github.com/AnnulusGames/LitMotion) is available, BGM transitions can be enabled through `UseLitMotionCrossFade`. Package detection defines `ENABLE_LITMOTION` automatically; without LitMotion, the related source and assembly are excluded from compilation. Custom transitions that do not use LitMotion can implement `IBgmTransition`.

## Requirements

- Unity 2022.3 or later
- [UniTask](https://github.com/Cysharp/UniTask) 2.5.10 or later
- *(Optional)* [Addressables](https://docs.unity3d.com/Manual/com.unity.addressables.html) 1.21.21 or later and [AssetsRegistry](https://github.com/AndanteTribe/AssetsRegistry) 1.0.4 or later — required for Addressables playback
- *(Optional)* [LitMotion](https://github.com/AnnulusGames/LitMotion) — required for LitMotion cross-fades

## Installation

Open `Window > Package Manager`, select `[+] > Add package from git URL`, and enter the following URL:

```
https://github.com/AndanteTribe/Radio.git?path=src/Radio.Unity/Packages/jp.andantetribe.radio
```

Install the optional packages above only when using Addressables playback or LitMotion cross-fades. No scripting define symbol needs to be set manually.

## Quick Start

```csharp
using Cysharp.Threading.Tasks;
using Radio;
using UnityEngine;

public class RadioSample : MonoBehaviour
{
    [SerializeField] private AudioClip _bgmClip;
    [SerializeField] private AudioClip _seClip;

    private AudioPlayer _player;

    private void Awake()
    {
        // Creates an SE channel + 3 BGM channels on this GameObject
        _player = new AudioPlayer(gameObject);
    }

    private async void Start()
    {
        // Play BGM (loops by default)
        // destroyCancellationToken is a MonoBehaviour property available in Unity 2022.2+
        _player.PlayBgmAsync(_bgmClip, loop: true, destroyCancellationToken).Forget();

        // Play a sound effect and wait for completion
        await _player.PlaySeAsync(_seClip, destroyCancellationToken);
    }
}
```

`AudioPlayer` owns no external asset handles and does not require `Dispose`. For Addressables playback, create an `AddressableAudioPlayer` and call `Dispose` when it is destroyed.

### Using Addressables

```csharp
using Cysharp.Threading.Tasks;
using Radio;
using UnityEngine;

public class AddressableRadioSample : MonoBehaviour
{
    private AddressableAudioPlayer _player;

    private void Awake()
    {
        // Creates an SE channel + 3 BGM channels on this GameObject
        _player = new AddressableAudioPlayer(gameObject);
    }

    private void Start()
    {
        // Load and play BGM from an Addressables string address
        _player.PlayBgmAsync(
            "assets/audio/bgm/MainTheme.wav",
            loop: true,
            destroyCancellationToken).Forget();
    }

    private void OnDestroy()
    {
        // Release all retained BGM handles
        _player.Dispose();
    }
}
```

## API

### Constructor

| Constructor | Description |
|-------------|-------------|
| `AudioPlayer(GameObject root, uint bgmChannelCount = 3, bool useVoice = false)` | Initializes the player, attaching `AudioSource` components to `root` as needed. Set `useVoice` to `true` to enable a dedicated voice channel. |
| `AddressableAudioPlayer(GameObject root, uint bgmChannelCount = 3, bool useVoice = false, AssetsRegistry? bgmRegistry = null)` | Adds Addressables loading and handle ownership to `AudioPlayer`. *(Requires Addressables and AssetsRegistry)* |

### Properties

| Property | Description |
|----------|-------------|
| `AudioSources Sources` | Exposes the `AudioSource` components managed by this player. |
| `AudioSource Sources.Se` | The sound-effect channel. |
| `AudioSource? Sources.Voice` | The voice channel, or `null` when `useVoice` is `false`. |
| `IReadOnlyList<AudioSource> Sources.Bgm` | The BGM channels in rotation order. |
| `IReadOnlyList<AudioSource> Sources.All` | Every channel in SE, optional Voice, then BGM order. |

The list structure of `Sources.Bgm` and `Sources.All` is read-only, while each `AudioSource` remains mutable. Users can configure properties such as `volume`, `outputAudioMixerGroup`, and `spatialBlend`.

### Methods

| Method | Description |
|--------|-------------|
| `PlayBgmAsync(AudioClip clip, bool loop, CancellationToken cancellationToken)` | Plays the specified BGM clip on the next BGM channel. Loops by default. |
| `PlaySeAsync(AudioClip clip, CancellationToken cancellationToken)` | Plays a sound effect and waits for the clip length. |
| `PlayVoiceAsync(AudioClip clip, CancellationToken cancellationToken)` | Plays a voice clip. *(Requires `useVoice: true` in constructor)* |
| `CrossFadeBgmAsync(AudioClip clip, bool loop, CancellationToken cancellationToken)` | Transitions BGM through the configured `IBgmTransition`. |
| `ConfigureBgmTransition(IBgmTransition transition)` | Configures the custom BGM transition used by `CrossFadeBgmAsync`. |
| `StopAllBgm()` | Stops all BGM channels. `AddressableAudioPlayer` also releases retained BGM handles. |
| Addressables overloads of `PlayBgmAsync` / `PlaySeAsync` / `PlayVoiceAsync` / `CrossFadeBgmAsync` | Loads and plays from a string address or `AssetReferenceT<AudioClip>`. *(`AddressableAudioPlayer` only)* |
| `SetMasterVolume(float volume)` | Sets the master volume (0–1) applied to all channels. |
| `SetBgmVolume(float volume)` | Sets the BGM volume (0–1). |
| `SetSeVolume(float volume)` | Sets the sound effect volume (0–1). |
| `SetVoiceVolume(float volume)` | Sets the voice volume (0–1). *(Requires `useVoice: true` in constructor)* |
| `Dispose()` | Releases all retained BGM asset handles. *(`AddressableAudioPlayer` only)* |

### BGM Transitions

When using LitMotion, configure the cross-fade after creating the player:

```csharp
var player = new AudioPlayer(gameObject)
    .UseLitMotionCrossFade(TimeSpan.FromSeconds(3));

await player.CrossFadeBgmAsync(nextBgmClip, cancellationToken: destroyCancellationToken);
```

A transition that does not depend on LitMotion can implement and configure the following public interface:

```csharp
public interface IBgmTransition
{
    UniTask TransitionAsync(
        BgmTransitionContext context,
        AudioClip clip,
        bool loop,
        CancellationToken cancellationToken);
}

player.ConfigureBgmTransition(new CustomBgmTransition());
```

`BgmTransitionContext` limits the exposed operations to the current BGM channel, managed volume, acquisition of the next BGM channel, and temporary volume control for channels owned by the transition.

## License

This library is released under the MIT license.
