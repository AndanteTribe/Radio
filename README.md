# Radio
[![unity-meta-file-check](https://github.com/AndanteTribe/Radio/actions/workflows/unity-meta-file-check.yml/badge.svg)](https://github.com/AndanteTribe/Radio/actions/workflows/unity-meta-file-check.yml)
[![Releases](https://img.shields.io/github/release/AndanteTribe/Radio.svg)](https://github.com/AndanteTribe/Radio/releases)
[![GitHub license](https://img.shields.io/github/license/AndanteTribe/Radio.svg)](./LICENSE)
[![openupm](https://img.shields.io/npm/v/jp.andantetribe.radio?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/jp.andantetribe.radio/)

English | [日本語](README_JA.md)

## Overview
**Radio** is a Unity audio playback library for BGM, sound effects, and interactive music.

It provides `AudioPlayerCore`, which manages multiple `AudioSource` channels (SE, Voice, BGM) and integrates with Unity's Addressables system for asset loading. All BGM handles are cached via [AssetsRegistry](https://github.com/AndanteTribe/AssetsRegistry) and released automatically on `Dispose`.

Optionally, when [LitMotion](https://github.com/AnnulusGames/LitMotion) is available (i.e., the `ENABLE_LITMOTION` scripting define symbol is set), cross-fade BGM transitions are also supported.

## Requirements
- Unity 2022.3 or later
- [Addressables](https://docs.unity3d.com/Manual/com.unity.addressables.html) 1.21.21 or later
- [UniTask](https://github.com/Cysharp/UniTask) 2.5.10 or later
- [AssetsRegistry](https://github.com/AndanteTribe/AssetsRegistry) 1.0.4 or later
- *(Optional)* [LitMotion](https://github.com/AnnulusGames/LitMotion) — required for cross-fade BGM support (enable with `ENABLE_LITMOTION` scripting define symbol)

## Installation
Open `Window > Package Manager`, select `[+] > Add package from git URL`, and enter the following URL:

```
https://github.com/AndanteTribe/Radio.git?path=src/Radio.Unity/Packages/jp.andantetribe.radio
```

## Quick Start

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using Radio;
using UnityEngine;

public class RadioSample : MonoBehaviour
{
    private AudioPlayerCore _player;

    private void Awake()
    {
        // Creates SE channel + 3 BGM channels on this GameObject
        _player = new AudioPlayerCore(gameObject);
    }

    private async void Start()
    {
        // Play BGM (loops by default)
        // destroyCancellationToken is a MonoBehaviour property available in Unity 2022.2+
        _player.PlayBgmAsync("assets/audio/bgm/MainTheme.wav", loop: true, destroyCancellationToken).Forget();

        // Play a sound effect and wait for completion
        await _player.PlaySeAsync("assets/audio/se/Click.wav", destroyCancellationToken);
    }

    private void OnDestroy()
    {
        // Releases all cached BGM handles
        _player.Dispose();
    }
}
```

## API

### Constructor

| Constructor | Description |
|-------------|-------------|
| `AudioPlayerCore(GameObject root, uint bgmChannelCount = 3, bool useVoice = false, AssetsRegistry? bgmRegistry = null)` | Initializes the player, attaching `AudioSource` components to `root` as needed. `bgmChannelCount` sets the number of BGM channels. Set `useVoice` to `true` to enable a dedicated voice channel. |
| `AudioPlayerCore(GameObject root, TimeSpan fadeDuration, uint bgmChannelCount = 3, bool useVoice = false, AssetsRegistry? bgmRegistry = null)` | Same as above, with an additional `fadeDuration` parameter for cross-fade transitions. *(Requires `ENABLE_LITMOTION`)* |

### Methods

| Method | Description |
|--------|-------------|
| `PlayBgmAsync(string address, bool loop, CancellationToken cancellationToken)` | Loads and plays a BGM clip from the given Addressables address. Loops by default. |
| `PlayBgmAsync(AssetReferenceT<AudioClip> reference, bool loop, CancellationToken cancellationToken)` | Loads and plays a BGM clip from an `AssetReferenceT<AudioClip>`. Loops by default. |
| `StopAllBgm()` | Stops all playing BGM channels and releases cached handles. |
| `PlaySeAsync(string address, CancellationToken cancellationToken)` | Loads and plays a sound effect, then releases the handle when playback finishes. |
| `PlaySeAsync(AssetReferenceT<AudioClip> reference, CancellationToken cancellationToken)` | Loads and plays a sound effect from an `AssetReferenceT<AudioClip>`. |
| `PlayVoiceAsync(string address, CancellationToken cancellationToken)` | Loads and plays a voice clip. *(Requires `useVoice: true` in constructor)* |
| `PlayVoiceAsync(AssetReferenceT<AudioClip> reference, CancellationToken cancellationToken)` | Loads and plays a voice clip from an `AssetReferenceT<AudioClip>`. *(Requires `useVoice: true` in constructor)* |
| `CrossFadeBgmAsync(string address, bool loop, CancellationToken cancellationToken)` | Performs a cross-fade transition to a new BGM track. *(Requires `ENABLE_LITMOTION`)* |
| `CrossFadeBgmAsync(AssetReferenceT<AudioClip> reference, bool loop, CancellationToken cancellationToken)` | Cross-fades to a new BGM track from an `AssetReferenceT<AudioClip>`. *(Requires `ENABLE_LITMOTION`)* |
| `SetMasterVolume(float volume)` | Sets the master volume (0–1) applied to all channels. |
| `SetBgmVolume(float volume)` | Sets the BGM volume (0–1). |
| `SetSeVolume(float volume)` | Sets the sound effect volume (0–1). |
| `SetVoiceVolume(float volume)` | Sets the voice volume (0–1). *(Requires `useVoice: true` in constructor)* |
| `Dispose()` | Releases all cached BGM asset handles. |

## License
This library is released under the MIT license.
