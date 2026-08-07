# Radio
[![unity-meta-file-check](https://github.com/AndanteTribe/Radio/actions/workflows/unity-meta-file-check.yml/badge.svg)](https://github.com/AndanteTribe/Radio/actions/workflows/unity-meta-file-check.yml)
[![Releases](https://img.shields.io/github/release/AndanteTribe/Radio.svg)](https://github.com/AndanteTribe/Radio/releases)
[![GitHub license](https://img.shields.io/github/license/AndanteTribe/Radio.svg)](./LICENSE)
[![openupm](https://img.shields.io/npm/v/jp.andantetribe.radio?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/jp.andantetribe.radio/)

English | [日本語](README_JA.md)

## Overview

**Radio** is a small set of composable Unity audio playback components. Each `AudioHub` has one responsibility, so applications can combine only the behavior they need:

| Component | Responsibility |
|---|---|
| `SingleChannelAudioHub` | Plays overlapping one-shot clips through one `AudioSource`. |
| `MultiChannelsAudioHub` | Rotates playback across multiple `AudioSource` channels. |
| `InteractiveAudioHub` | Adds fade-in, cross-fade, and playback-position synchronization with LitMotion. |
| `AddressableAudioHub` | Loads an `AudioClip` for each request and releases its Addressables handle after playback. |
| `CachedAddressableAudioHub` | Retains successful Addressables acquisitions until `Dispose`. |
| `CompositeVolumeAudioHub<TClip, TId>` | Applies a master volume and per-ID volumes to groups of hubs. |

The playback hubs implement `IAudioHub<T>`. Addressables hubs wrap an `IAudioHub<AudioClip>` and change its input to an address or `AssetReferenceT<AudioClip>`. `CompositeVolumeAudioHub` coordinates volume only and does not itself implement `IAudioHub<T>`.

Radio does not create or discover `AudioSource` components. The application owns the sources and passes them to the appropriate hub.

## Requirements

- Unity 2022.3 or later
- [UniTask](https://github.com/Cysharp/UniTask) 2.5.10 or later
- *(Optional)* [Addressables](https://docs.unity3d.com/Manual/com.unity.addressables.html) 1.21.21 or later for `AddressableAudioHub` and `CachedAddressableAudioHub`
- *(Optional)* [LitMotion](https://github.com/AnnulusGames/LitMotion) for `InteractiveAudioHub`

Addressables and LitMotion are not declared as package dependencies. When either package is installed, the package assembly enables the corresponding hubs through its Version Defines.

## Installation

Open `Window > Package Manager`, select `[+] > Add package from git URL`, and enter:

```text
https://github.com/AndanteTribe/Radio.git?path=src/Radio.Unity/Packages/jp.andantetribe.radio
```

## Quick Start

The following `AudioManager` is a typical setup: one looping BGM source, one overlapping one-shot source for sound effects, and shared master/category volume controls. Assign both `AudioSource` fields in the Inspector.

```csharp
using Cysharp.Threading.Tasks;
using Radio;
using UnityEngine;

public sealed class AudioManager : MonoBehaviour
{
    private enum AudioGroup
    {
        Bgm,
        Se,
    }

    [SerializeField] private AudioSource bgmSource = null!;
    [SerializeField] private AudioSource seSource = null!;

    private MultiChannelsAudioHub bgm = null!;
    private SingleChannelAudioHub se = null!;
    private CompositeVolumeAudioHub<AudioClip, AudioGroup> volumes = null!;

    private void Awake()
    {
        bgm = new MultiChannelsAudioHub(new[] { bgmSource }, volume: 1.0f, loop: true);
        se = new SingleChannelAudioHub(seSource, volume: 1.0f);

        var builder =
            new CompositeVolumeAudioHub<AudioClip, AudioGroup>.Builder(masterVolume: 1.0f);
        builder.AddHub(AudioGroup.Bgm, bgm);
        builder.AddHub(AudioGroup.Se, se);
        builder.SetVolume(AudioGroup.Bgm, 1.0f);
        builder.SetVolume(AudioGroup.Se, 1.0f);
        volumes = builder.Build();
    }

    public void PlayBgm(AudioClip clip)
    {
        bgm.StopAll();
        bgm.PlayAsync(clip, destroyCancellationToken).Forget();
    }

    public void PlaySe(AudioClip clip) =>
        se.PlayAsync(clip, destroyCancellationToken).Forget();

    public void StopBgm() => bgm.StopAll();

    public void StopAll()
    {
        bgm.StopAll();
        se.StopAll();
    }

    public void SetBgmLoop(bool value) => bgm.ApplyLoop(value);

    public void SetMasterVolume(float value) => volumes.ApplyMasterVolume(value);

    public void SetBgmVolume(float value) => volumes.ApplyVolume(AudioGroup.Bgm, value);

    public void SetSeVolume(float value) => volumes.ApplyVolume(AudioGroup.Se, value);

    private void OnDestroy() => volumes.Dispose();
}
```

All volume arguments must be greater than `0` and less than or equal to `1`. Invalid values throw `ArgumentOutOfRangeException`.

## Playback Hubs

### Common interfaces

| API | Description |
|---|---|
| `IAudioHub<T>` | Common playback contract for a key of type `T`. |
| `IAudioHub<T>.AudioSources` | Returns the `AudioSource` instances managed by the hub without allocating an array. |
| `IAudioHub<T>.PlayAsync(T, CancellationToken)` | Starts playback and returns a task representing that implementation's playback lifetime. |
| `IAudioHub<T>.StopAll()` | Stops every source managed by the hub. |
| `IAudioHub<T>.ApplyVolume(float)` | Applies the hub's effective volume. Values must be greater than `0` and less than or equal to `1`. |
| `ILoopableAudioHub<T>` | Extends `IAudioHub<T>` with loop control. |
| `ILoopableAudioHub<T>.ApplyLoop(bool)` | Applies the loop setting to all managed sources and subsequent playback. |

### Implementations

| Type | Construction and input | Behavior |
|---|---|---|
| `SingleChannelAudioHub` | `SingleChannelAudioHub(AudioSource, float)`<br>Input: `AudioClip` | Uses `AudioSource.PlayOneShot`, allowing clips to overlap. `PlayAsync` waits for the clip duration; cancellation stops the wait but not an already-started one-shot. Volume changes affect subsequent one-shots. |
| `MultiChannelsAudioHub` | `MultiChannelsAudioHub(ReadOnlyMemory<AudioSource>, float, bool)`<br>Input: `AudioClip` | Uses `AudioSource.Play` and rotates through the supplied channels. Looping calls wait for channel reuse, `StopAll`, or cancellation; non-looping calls can also complete when the clip duration elapses. Implements `ILoopableAudioHub<AudioClip>`. |
| `InteractiveAudioHub` | `InteractiveAudioHub(ReadOnlyMemory<AudioSource>, TimeSpan, float, bool)` or the overload with a three-second fade<br>Input: `AudioClip` | Adds fade-in, sine/cosine cross-fades, and playback-position synchronization. `PlayAsync` waits for both the transition and channel lifetime. `FadeDuration` exposes the transition duration. Implements `ILoopableAudioHub<AudioClip>` and is available only with LitMotion. |

Supply at least one non-null source to `MultiChannelsAudioHub`, and at least two for normal cross-fading with `InteractiveAudioHub`. `PlayAsync` represents the playback lifetime, not merely the moment playback starts; a looping task can remain pending until its channel is reused, stopped, or cancelled.

## Addressables Wrappers

Both wrappers implement `IAudioHub<string>` and `IAudioHub<AssetReferenceT<AudioClip>>` and delegate `AudioSources`, `StopAll`, and `ApplyVolume` to the wrapped clip hub.

| Type | Construction and input | Handle lifetime |
|---|---|---|
| `AddressableAudioHub` | `AddressableAudioHub(IAudioHub<AudioClip>)`<br>Input: `string` or `AssetReferenceT<AudioClip>` | Loads on every request, delegates playback to the wrapped hub, and releases the handle when playback completes, fails, or is cancelled. Available only with Addressables. |
| `CachedAddressableAudioHub` | `CachedAddressableAudioHub(IAudioHub<AudioClip>)`<br>Input: `string` or `AssetReferenceT<AudioClip>` | Retains every successful Addressables acquisition and releases the corresponding reference counts in `Dispose`. `StopAll` stops playback without releasing cached handles. Available only with Addressables. |

`CachedAddressableAudioHub.Dispose` is not synchronized with in-flight operations. Cancel and observe outstanding `PlayAsync` calls before disposing it, and do not start new requests afterwards.

## Composite Volume

`CompositeVolumeAudioHub<TClip, TId>` groups one or more `IAudioHub<TClip>` instances under each ID. The effective value applied to every hub in a group is:

```text
master volume × group volume
```

Master and group volumes default to `0.5`.

| API | Description |
|---|---|
| `CompositeVolumeAudioHub<TClip, TId>.MasterVolume` | Gets the current master volume. |
| `CompositeVolumeAudioHub<TClip, TId>.Count` | Gets the number of registered groups. |
| `GetVolume(TId)` | Gets a group's volume before master-volume multiplication. |
| `GetHubs(TId)` | Gets the hubs registered in a group. |
| `ApplyMasterVolume(float)` | Changes the master volume and reapplies every group. |
| `ApplyVolume(TId, float)` | Changes one group volume and reapplies that group. |
| `Dispose()` | Disposes every registered hub that implements `IDisposable`. |
| `Builder(IEqualityComparer<TId>?, float)` | Creates a builder with an optional ID comparer and master volume. The default struct is also valid. |
| `Builder.AddHub(TId, IAudioHub<TClip>)` | Registers a hub. Multiple hubs can share one ID. |
| `Builder.SetVolume(TId, float)` | Sets a registered group's volume; an unknown ID throws `KeyNotFoundException`. |
| `Builder.SetMasterVolume(float)` | Sets the master volume used by `Build`. |
| `Builder.Build()` | Builds the composite and resets the builder's registrations. |

## License

This library is released under the MIT license.
