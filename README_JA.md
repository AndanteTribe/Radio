# Radio
[![unity-meta-file-check](https://github.com/AndanteTribe/Radio/actions/workflows/unity-meta-file-check.yml/badge.svg)](https://github.com/AndanteTribe/Radio/actions/workflows/unity-meta-file-check.yml)
[![Releases](https://img.shields.io/github/release/AndanteTribe/Radio.svg)](https://github.com/AndanteTribe/Radio/releases)
[![GitHub license](https://img.shields.io/github/license/AndanteTribe/Radio.svg)](./LICENSE)
[![openupm](https://img.shields.io/npm/v/jp.andantetribe.radio?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/jp.andantetribe.radio/)

[English](README.md) | 日本語

## 概要

**Radio** は、役割ごとに分割されたUnity向けオーディオ再生コンポーネント群です。アプリケーションは、必要な振る舞いを持つ`AudioHub`だけを組み合わせて利用できます。

| コンポーネント | 役割 |
|---|---|
| `SingleChannelAudioHub` | 1つの`AudioSource`を使って、重複可能なOneShotを再生します。 |
| `MultiChannelsAudioHub` | 複数の`AudioSource`を順番に使って再生します。 |
| `InteractiveAudioHub` | LitMotionを使ったフェードイン、クロスフェード、再生位置の同期を追加します。 |
| `AddressableAudioHub` | リクエストごとに`AudioClip`をロードし、再生後にAddressablesハンドルを解放します。 |
| `CachedAddressableAudioHub` | 成功したAddressablesの取得を`Dispose`まで保持します。 |
| `CompositeVolumeAudioHub<TClip, TId>` | マスター音量とIDごとの音量を複数Hubへ反映します。 |

再生用Hubは`IAudioHub<T>`を実装します。Addressables用Hubは`IAudioHub<AudioClip>`を包み、アドレスまたは`AssetReferenceT<AudioClip>`を入力として受け取れるようにします。`CompositeVolumeAudioHub`は音量だけを管理するため、`IAudioHub<T>`自体は実装しません。

Radioは`AudioSource`コンポーネントの生成や検索を行いません。利用する`AudioSource`はアプリケーション側で所有し、各Hubへ渡してください。

## 要件

- Unity 2022.3以上
- [UniTask](https://github.com/Cysharp/UniTask) 2.5.10以上
- [Addressables](https://docs.unity3d.com/Manual/com.unity.addressables.html) 1.21.21以上
- *（任意）* `InteractiveAudioHub`を利用する場合は[LitMotion](https://github.com/AnnulusGames/LitMotion)

LitMotionがインストールされている場合、パッケージのAssembly Definitionに設定されたVersion Defineによって`InteractiveAudioHub`が有効になります。

## インストール

`Window > Package Manager`からPackage Managerを開き、`[+] > Add package from git URL`を選択して以下を入力します。

```text
https://github.com/AndanteTribe/Radio.git?path=src/Radio.Unity/Packages/jp.andantetribe.radio
```

## クイックスタート

以下の例では、InspectorからSE用AudioSourceを1つ、交互に使用するBGM用AudioSourceを2つ、各AudioClipを設定し、両カテゴリの音量を1つのCompositeで管理します。

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
        // ループ再生のタスクは、チャンネルが一巡するか、StopAllまたはキャンセルまで
        // 完了しないため、ここではfire-and-forgetにします。
        bgmHub.PlayAsync(bgmClip, destroyCancellationToken).Forget();

        // OneShotのタスクは、クリップの長さに相当する待機後に完了します。
        await seHub.PlayAsync(seClip, destroyCancellationToken);
    }

    public void SetMasterVolume(float value) => volumes.ApplyMasterVolume(value);

    public void SetBgmVolume(float value) => volumes.ApplyVolume(VolumeKind.Bgm, value);
}
```

すべての音量引数は`0`より大きく、`1`以下である必要があります。範囲外の場合は`ArgumentOutOfRangeException`が送出されます。

## 再生Hub

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

`AudioSources`は、Hubが所有するAudioSourceを配列生成なしで公開します。`StopAll`はそのHubのAudioSourceを停止し、`ApplyVolume`はHubの実効音量を反映します。

### `SingleChannelAudioHub`

```csharp
var hub = new SingleChannelAudioHub(source, volume: 0.5f);
await hub.PlayAsync(clip, cancellationToken);
```

再生には`AudioSource.PlayOneShot`を使うため、複数クリップを重ねて再生できます。設定値は`PlayOneShot`の`volumeScale`として渡され、実効音量には`AudioSource.volume`も影響します。Hubの音量変更は次回以降のOneShotに適用され、すでに再生中のOneShotには反映されません。`PlayAsync`のキャンセルは再生時間の待機だけをキャンセルし、開始済みのOneShotは停止しません。停止する場合は`StopAll`を呼び出してください。

### `MultiChannelsAudioHub`

```csharp
var hub = new MultiChannelsAudioHub(channels, volume: 0.5f, loop: true);
hub.PlayAsync(clip, cancellationToken).Forget();
```

各リクエストは、チャンネルを順番に選び、選択したAudioSourceを停止して再利用します。1つ以上のnullでない`AudioSource`を渡してください。`Loop`は再生開始時に読み取られ、以降のリクエストに向けて変更できます。

非ループ再生の`PlayAsync`は、クリップの長さが経過する、対象チャンネルが一巡する、または`StopAll`が呼ばれた時点で完了します。ループ再生では、チャンネルが一巡する、`StopAll`が呼ばれる、またはトークンがキャンセルされるまで待機します。リクエストをキャンセルすると、そのリクエストで選択したAudioSourceを停止し、クリップを解除します。

### `InteractiveAudioHub`

`InteractiveAudioHub`はLitMotionがインストールされている場合に利用できます。

```csharp
var hub = new InteractiveAudioHub(
    channels,
    fadeDuration: TimeSpan.FromSeconds(0.5),
    volume: 0.5f,
    loop: true);

hub.PlayAsync(clip, cancellationToken).Forget();
```

最初のクリップはフェードインします。以降はSin/Cosカーブを使って現在のAudioSourceから次のAudioSourceへクロスフェードし、次のクリップは現在の再生位置を自身の長さで折り返した位置から開始します。クロスフェード用途では、2つ以上のnullでないAudioSourceを渡してください。`fadeDuration`を省略するコンストラクタでは3秒になります。

`PlayAsync`は再生の生存期間を表すものであり、フェードだけの完了通知ではありません。内部ではフェード処理と`MultiChannelsAudioHub`と同様のチャンネル生存期間の両方を待つため、ループ再生ではフェード完了後もタスクが待機し続けることがあります。フェード時間だけ待ちたい場合は、別途`UniTask.Delay(hub.FadeDuration)`を利用してください。

新しいクリップを開始すると、実行中のフェードをキャンセルして次の遷移を開始します。`StopAll`はフェードをキャンセルし、すべてのAudioSourceを停止してクリップを解除し、チャンネルの状態をリセットします。

## Addressablesラッパー

どちらのラッパーも`IAudioHub<string>`と`IAudioHub<AssetReferenceT<AudioClip>>`を実装し、`AudioSources`、`StopAll`、`ApplyVolume`は内側のAudioClip用Hubへ委譲します。

### `AddressableAudioHub`

```csharp
var clipHub = new SingleChannelAudioHub(source);
var addressableHub = new AddressableAudioHub(clipHub);

await addressableHub.PlayAsync("audio/se/click", cancellationToken);
await addressableHub.PlayAsync(assetReference, cancellationToken);
```

呼び出しごとに`Addressables.LoadAssetAsync<AudioClip>`を実行します。ハンドルは、内側の`PlayAsync`が完了、失敗、またはキャンセルされた後に解放されます。ロード結果がnullの場合は再生せず、ハンドルを解放します。

### `CachedAddressableAudioHub`

```csharp
var clipHub = new MultiChannelsAudioHub(channels, loop: true);
var addressableHub = new CachedAddressableAudioHub(clipHub);

using var playbackCancellation = new CancellationTokenSource();
var playbackTask =
    addressableHub.PlayAsync("audio/bgm/main", playbackCancellation.Token);

// 制御された終了処理では、進行中のすべてのPlayAsyncをキャンセルし、
// その結果まで確認してから、保持した取得分を解放します。
playbackCancellation.Cancel();
await playbackTask.SuppressCancellationThrow();
addressableHub.StopAll();
addressableHub.Dispose();
```

このラッパーもリクエストごとに`Addressables.LoadAssetAsync<AudioClip>`を呼び出し、すでにロード済みの処理の再利用はAddressablesに任せます。成功した取得回数を記録して保持し、`Dispose`で対応する回数だけ`Addressables.Release`を呼び出します。`StopAll`は再生だけを停止し、保持しているハンドルを解放しません。

`Dispose`は進行中の`PlayAsync`と同期しません。すべてのロードと再生タスクが完了するか、キャンセル結果まで確認した後に呼び出し、それ以降は新しい再生を開始しないでください。キャンセルと待機を含む非同期の終了処理が必要なアプリケーションでは、その生存期間管理をHubの外側で実装してください。

## 複合音量

`CompositeVolumeAudioHub<TClip, TId>`は、1つのIDに1つ以上の`IAudioHub<TClip>`をまとめます。グループ内の各Hubへ適用される実効値は次のとおりです。

```text
マスター音量 × グループ音量
```

ネストされたBuilderでIDの集合を構築します。

```csharp
var builder =
    new CompositeVolumeAudioHub<AudioClip, VolumeKind>.Builder(masterVolume: 0.8f);

builder.AddHub(VolumeKind.Bgm, explorationBgmHub);
builder.AddHub(VolumeKind.Bgm, battleBgmHub); // 1つのIDに複数Hubを登録できます。
builder.AddHub(VolumeKind.Se, seHub);
builder.SetVolume(VolumeKind.Bgm, 0.6f);
builder.SetVolume(VolumeKind.Se, 1.0f);

var volumes = builder.Build();

volumes.ApplyMasterVolume(0.5f);             // すべてのグループへ再反映
volumes.ApplyVolume(VolumeKind.Bgm, 0.75f);  // BGMグループだけへ再反映
```

`SetVolume`を呼ぶ前に、対象IDを`AddHub`で登録する必要があります。未登録の場合は`KeyNotFoundException`が送出されます。マスター音量とグループ音量の既定値は`0.5`です。構築結果は`Count`、`GetVolume`、`GetHubs`から確認できます。Builderは可変の構造体なので、構築中のコピーは避けてください。

## ライセンス

このライブラリはMITライセンスで公開しています。
