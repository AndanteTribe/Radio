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
- [Addressables](https://docs.unity3d.com/Manual/com.unity.addressables.html) 1.21.21 or later
- *(Optional)* [LitMotion](https://github.com/AnnulusGames/LitMotion) for `InteractiveAudioHub`

When LitMotion is installed, the package's assembly definition enables `InteractiveAudioHub` through its version define.

## Installation

Open `Window > Package Manager`, select `[+] > Add package from git URL`, and enter:

```text
https://github.com/AndanteTribe/Radio.git?path=src/Radio.Unity/Packages/jp.andantetribe.radio
```

## Quick Start

Assign the sources and clips in the Inspector. The following example uses one source for sound effects, two sources for rotating BGM playback, and one composite volume controller for both categories.

```csharp
using Cysharp.Threading.Tasks;
using Radio;
using UnityEngine;

public sealed class RadioSample : MonoBehaviour
{
    private enum VolumeKind
    {
        Bgm,
        Se,
    }

    [SerializeField] private AudioSource seSource = null!;
    [SerializeField] private AudioSource[] bgmSources = null!;
    [SerializeField] private AudioClip seClip = null!;
    [SerializeField] private AudioClip bgmClip = null!;

    private SingleChannelAudioHub seHub = null!;
    private MultiChannelsAudioHub bgmHub = null!;
    private CompositeVolumeAudioHub<AudioClip, VolumeKind> volumes = null!;

    private void Awake()
    {
        seHub = new SingleChannelAudioHub(seSource);
        bgmHub = new MultiChannelsAudioHub(bgmSources, loop: true);

        var builder =
            new CompositeVolumeAudioHub<AudioClip, VolumeKind>.Builder(masterVolume: 1.0f);
        builder.AddHub(VolumeKind.Bgm, bgmHub);
        builder.AddHub(VolumeKind.Se, seHub);
        builder.SetVolume(VolumeKind.Bgm, 0.7f);
        builder.SetVolume(VolumeKind.Se, 1.0f);
        volumes = builder.Build();
    }

    private async UniTaskVoid Start()
    {
        // A looping task remains pending until its channel cycles, StopAll is called,
        // or the token is cancelled, so fire-and-forget is appropriate here.
        bgmHub.PlayAsync(bgmClip, destroyCancellationToken).Forget();

        // A one-shot task completes after the clip duration.
        await seHub.PlayAsync(seClip, destroyCancellationToken);
    }

    public void SetMasterVolume(float value) => volumes.ApplyMasterVolume(value);

    public void SetBgmVolume(float value) => volumes.ApplyVolume(VolumeKind.Bgm, value);
}
```

All volume arguments must be greater than `0` and less than or equal to `1`. Invalid values throw `ArgumentOutOfRangeException`.

## Playback Hubs

### `IAudioHub<T>`

```csharp
public interface IAudioHub<in T>
{
    ReadOnlySpan<AudioSource> AudioSources { get; }
    UniTask PlayAsync(T key, CancellationToken cancellationToken);
    void StopAll();
    void ApplyVolume(float value);
}
```

`AudioSources` exposes the sources owned by the hub without allocating an array. `StopAll` stops that hub's sources, while `ApplyVolume` applies the hub's effective volume.

### `SingleChannelAudioHub`

```csharp
var hub = new SingleChannelAudioHub(source, volume: 0.5f);
await hub.PlayAsync(clip, cancellationToken);
```

Playback uses `AudioSource.PlayOneShot`, so clips can overlap. The configured value is passed as `PlayOneShot`'s `volumeScale`; the effective volume is also affected by `AudioSource.volume`. Changing the hub volume affects subsequent one-shots, not ones already playing. Cancelling `PlayAsync` cancels its duration wait but does not stop an already-started one-shot; use `StopAll` to stop the source.

### `MultiChannelsAudioHub`

```csharp
var hub = new MultiChannelsAudioHub(channels, volume: 0.5f, loop: true);
hub.PlayAsync(clip, cancellationToken).Forget();
```

Each request stops and reuses the next source in the channel ring. Provide at least one non-null `AudioSource`. `Loop` is read when playback starts and can be changed for subsequent requests.

For non-looping playback, `PlayAsync` completes when the clip duration elapses, its channel cycles, or `StopAll` is called. For looping playback, it remains pending until the channel cycles, `StopAll` is called, or the token is cancelled. Cancelling a request stops and clears the source selected for that request.

### `InteractiveAudioHub`

`InteractiveAudioHub` is available when LitMotion is installed.

```csharp
var hub = new InteractiveAudioHub(
    channels,
    fadeDuration: TimeSpan.FromSeconds(0.5),
    volume: 0.5f,
    loop: true);

hub.PlayAsync(clip, cancellationToken).Forget();
```

The first clip fades in. Later clips cross-fade from the current source to the next source using sine/cosine curves, and the next clip starts at the current playback position modulo its own length. Supply at least two non-null sources for cross-fading. The constructor overload without `fadeDuration` uses three seconds.

`PlayAsync` represents the playback lifetime, not a notification that only the fade has completed. Internally it waits for both the fade operation and the same channel-lifetime rules as `MultiChannelsAudioHub`; a looping call can therefore remain pending after the fade. If only the fade duration is relevant to the caller, wait for `UniTask.Delay(hub.FadeDuration)` separately.

Starting another clip cancels the active fade and begins the next transition. `StopAll` cancels the fade, stops every channel, clears clips, and resets the channel ring.

## Addressables Wrappers

Both wrappers implement `IAudioHub<string>` and `IAudioHub<AssetReferenceT<AudioClip>>` and delegate `AudioSources`, `StopAll`, and `ApplyVolume` to the wrapped clip hub.

### `AddressableAudioHub`

```csharp
var clipHub = new SingleChannelAudioHub(source);
var addressableHub = new AddressableAudioHub(clipHub);

await addressableHub.PlayAsync("audio/se/click", cancellationToken);
await addressableHub.PlayAsync(assetReference, cancellationToken);
```

Every call invokes `Addressables.LoadAssetAsync<AudioClip>`. Its handle is released after the wrapped `PlayAsync` completes, fails, or is cancelled. A null load result is released without starting playback.

### `CachedAddressableAudioHub`

```csharp
var clipHub = new MultiChannelsAudioHub(channels, loop: true);
var addressableHub = new CachedAddressableAudioHub(clipHub);

using var playbackCancellation = new CancellationTokenSource();
var playbackTask =
    addressableHub.PlayAsync("audio/bgm/main", playbackCancellation.Token);

// During a controlled shutdown, first cancel and observe every outstanding
// PlayAsync operation, then release retained acquisitions.
playbackCancellation.Cancel();
await playbackTask.SuppressCancellationThrow();
addressableHub.StopAll();
addressableHub.Dispose();
```

This wrapper still calls `Addressables.LoadAssetAsync<AudioClip>` for every request, allowing Addressables to reuse an already-loaded operation. Each successful acquisition is counted and retained; `Dispose` calls `Addressables.Release` the corresponding number of times. `StopAll` only stops playback and does not release the retained handles.

`Dispose` is not synchronized with in-flight `PlayAsync` operations. Call it only after all loads and playback tasks have completed or been cancelled and observed, and do not start new requests afterwards. Applications that require coordinated asynchronous shutdown should own that cancellation and waiting policy outside the hub.

## Composite Volume

`CompositeVolumeAudioHub<TClip, TId>` groups one or more `IAudioHub<TClip>` instances under each ID. The effective value applied to every hub in a group is:

```text
master volume × group volume
```

Use its nested builder to assemble the immutable set of IDs:

```csharp
var builder =
    new CompositeVolumeAudioHub<AudioClip, VolumeKind>.Builder(masterVolume: 0.8f);

builder.AddHub(VolumeKind.Bgm, explorationBgmHub);
builder.AddHub(VolumeKind.Bgm, battleBgmHub); // Multiple hubs may share one ID.
builder.AddHub(VolumeKind.Se, seHub);
builder.SetVolume(VolumeKind.Bgm, 0.6f);
builder.SetVolume(VolumeKind.Se, 1.0f);

var volumes = builder.Build();

volumes.ApplyMasterVolume(0.5f);             // Reapplies every group.
volumes.ApplyVolume(VolumeKind.Bgm, 0.75f);  // Reapplies only BGM hubs.
```

An ID must be registered with `AddHub` before `SetVolume`; otherwise `KeyNotFoundException` is thrown. The default master and group volumes are `0.5`. `Count`, `GetVolume`, and `GetHubs` expose the resulting configuration. The builder is a mutable struct; avoid copying it while assembling a configuration.

## License

This library is released under the MIT license.
