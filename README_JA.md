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
- *（任意）* `AddressableAudioHub`と`CachedAddressableAudioHub`を利用する場合は[Addressables](https://docs.unity3d.com/Manual/com.unity.addressables.html) 1.21.21以上
- *（任意）* `InteractiveAudioHub`を利用する場合は[LitMotion](https://github.com/AnnulusGames/LitMotion)

AddressablesとLitMotionはRadio自体の依存関係には含まれません。各パッケージがインストールされている場合、Assembly DefinitionのVersion Defineによって対応するHubが有効になります。

## インストール

`Window > Package Manager`からPackage Managerを開き、`[+] > Add package from git URL`を選択して以下を入力します。

```text
https://github.com/AndanteTribe/Radio.git?path=src/Radio.Unity/Packages/jp.andantetribe.radio
```

## クイックスタート

以下は、ループするBGM用AudioSourceを1つ、重複再生するSE用AudioSourceを1つ、マスター音量とカテゴリ音量を持つ、一般的な`AudioManager`の実装例です。2つの`AudioSource`はInspectorから設定してください。

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

すべての音量引数は`0`より大きく、`1`以下である必要があります。範囲外の場合は`ArgumentOutOfRangeException`が送出されます。

## 再生Hub

### 共通インターフェイス

| API | 説明 |
|---|---|
| `IAudioHub<T>` | `T`型のキーを受け取る共通の再生契約です。 |
| `IAudioHub<T>.AudioSources` | Hubが管理する`AudioSource`を、配列を生成せずに返します。 |
| `IAudioHub<T>.PlayAsync(T, CancellationToken)` | 再生を開始し、各実装が定める再生期間を表すタスクを返します。 |
| `IAudioHub<T>.StopAll()` | Hubが管理するすべてのAudioSourceを停止します。 |
| `IAudioHub<T>.ApplyVolume(float)` | Hubの実効音量を反映します。値は`0`より大きく`1`以下である必要があります。 |
| `ILoopableAudioHub<T>` | `IAudioHub<T>`へループ制御を追加します。 |
| `ILoopableAudioHub<T>.ApplyLoop(bool)` | 管理対象の全AudioSourceと、以降の再生へループ設定を反映します。 |

### 実装

| 型 | 生成と入力 | 振る舞い |
|---|---|---|
| `SingleChannelAudioHub` | `SingleChannelAudioHub(AudioSource, float)`<br>入力: `AudioClip` | `AudioSource.PlayOneShot`を使うため重複再生できます。`PlayAsync`はクリップの長さだけ待機します。キャンセルされても開始済みのOneShotは停止せず、待機だけを終了します。音量変更は次回以降のOneShotへ反映されます。 |
| `MultiChannelsAudioHub` | `MultiChannelsAudioHub(ReadOnlyMemory<AudioSource>, float, bool)`<br>入力: `AudioClip` | `AudioSource.Play`を使い、渡されたチャンネルを順番に利用します。ループ再生はチャンネルの再利用、`StopAll`、キャンセルまで待機し、非ループ再生ではクリップ長の経過でも完了します。`ILoopableAudioHub<AudioClip>`を実装します。 |
| `InteractiveAudioHub` | `InteractiveAudioHub(ReadOnlyMemory<AudioSource>, TimeSpan, float, bool)`、またはフェード時間が3秒のオーバーロード<br>入力: `AudioClip` | フェードイン、Sin/Cosカーブによるクロスフェード、再生位置の同期を追加します。`PlayAsync`は遷移とチャンネルの生存期間の両方を待機します。`FadeDuration`から遷移時間を取得できます。`ILoopableAudioHub<AudioClip>`を実装し、LitMotion導入時のみ利用できます。 |

`MultiChannelsAudioHub`には1つ以上、通常のクロスフェードを行う`InteractiveAudioHub`には2つ以上のnullでないAudioSourceを渡してください。`PlayAsync`は再生開始の通知ではなく再生の生存期間を表すため、ループ再生のタスクはチャンネルが再利用、停止、またはキャンセルされるまで待機し続ける場合があります。

## Addressablesラッパー

どちらのラッパーも`IAudioHub<string>`と`IAudioHub<AssetReferenceT<AudioClip>>`を実装し、`AudioSources`、`StopAll`、`ApplyVolume`は内側のAudioClip用Hubへ委譲します。

| 型 | 生成と入力 | ハンドルの生存期間 |
|---|---|---|
| `AddressableAudioHub` | `AddressableAudioHub(IAudioHub<AudioClip>)`<br>入力: `string`または`AssetReferenceT<AudioClip>` | リクエストごとにロードし、内側のHubへ再生を委譲します。再生の完了、失敗、キャンセル時にハンドルを解放します。Addressables導入時のみ利用できます。 |
| `CachedAddressableAudioHub` | `CachedAddressableAudioHub(IAudioHub<AudioClip>)`<br>入力: `string`または`AssetReferenceT<AudioClip>` | 成功したAddressablesの取得を保持し、`Dispose`で参照回数分を解放します。`StopAll`は再生を停止しますが、保持中のハンドルは解放しません。Addressables導入時のみ利用できます。 |

`CachedAddressableAudioHub.Dispose`は進行中の処理と同期しません。未完了の`PlayAsync`をキャンセルして結果を確認した後に破棄し、それ以降は新しい再生を開始しないでください。

## 複合音量

`CompositeVolumeAudioHub<TClip, TId>`は、1つのIDに1つ以上の`IAudioHub<TClip>`をまとめます。グループ内の各Hubへ適用される実効値は次のとおりです。

```text
マスター音量 × グループ音量
```

マスター音量とグループ音量の既定値は`0.5`です。

| API | 説明 |
|---|---|
| `CompositeVolumeAudioHub<TClip, TId>.MasterVolume` | 現在のマスター音量を取得します。 |
| `CompositeVolumeAudioHub<TClip, TId>.Count` | 登録されているグループ数を取得します。 |
| `GetVolume(TId)` | マスター音量を乗算する前のグループ音量を取得します。 |
| `GetHubs(TId)` | グループに登録されたHubを取得します。 |
| `ApplyMasterVolume(float)` | マスター音量を変更し、全グループへ再反映します。 |
| `ApplyVolume(TId, float)` | 1つのグループ音量を変更し、そのグループへ再反映します。 |
| `Dispose()` | 登録されたHubのうち、`IDisposable`を実装するものをすべて破棄します。 |
| `Builder(IEqualityComparer<TId>?, float)` | IDの比較方法とマスター音量を指定してBuilderを生成します。既定値の構造体も利用できます。 |
| `Builder.AddHub(TId, IAudioHub<TClip>)` | Hubを登録します。1つのIDに複数のHubを登録できます。 |
| `Builder.SetVolume(TId, float)` | 登録済みグループの音量を設定します。未登録IDの場合は`KeyNotFoundException`を送出します。 |
| `Builder.SetMasterVolume(float)` | `Build`で使用するマスター音量を設定します。 |
| `Builder.Build()` | Compositeを構築し、Builderが保持する登録をリセットします。 |

## ライセンス

このライブラリはMITライセンスで公開しています。
