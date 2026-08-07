# Radio

[![unity-meta-file-check](https://github.com/AndanteTribe/Radio/actions/workflows/unity-meta-file-check.yml/badge.svg)](https://github.com/AndanteTribe/Radio/actions/workflows/unity-meta-file-check.yml)
[![Releases](https://img.shields.io/github/release/AndanteTribe/Radio.svg)](https://github.com/AndanteTribe/Radio/releases)
[![GitHub license](https://img.shields.io/github/license/AndanteTribe/Radio.svg)](./LICENSE)
[![openupm](https://img.shields.io/npm/v/jp.andantetribe.radio?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/jp.andantetribe.radio/)

[English](README.md) | 日本語

## 概要

**Radio** は、Unity 向けの BGM・効果音・ボイス再生ライブラリです。

`AudioPlayer` は複数の `AudioSource` チャンネルを管理し、`AudioClip` を直接再生します。Addressables を利用する場合だけ `AddressableAudioPlayer` を使用し、BGM ハンドルは [AssetsRegistry](https://github.com/AndanteTribe/AssetsRegistry) によって保持され、`StopAllBgm` または `Dispose` で解放されます。

オプションとして [LitMotion](https://github.com/AnnulusGames/LitMotion) が利用可能な場合、`UseLitMotionCrossFade` による BGM トランジションも利用できます。パッケージを検出すると `ENABLE_LITMOTION` が自動的に有効になり、LitMotion がない場合は関連するソースとアセンブリがコンパイル対象から除外されます。LitMotion を使わない独自実装は `IBgmTransition` から作成できます。

## 要件

- Unity 2022.3 以上
- [UniTask](https://github.com/Cysharp/UniTask) 2.5.10 以上
- *（オプション）* [Addressables](https://docs.unity3d.com/Manual/com.unity.addressables.html) 1.21.21 以上、および [AssetsRegistry](https://github.com/AndanteTribe/AssetsRegistry) 1.0.4 以上 — Addressables 再生に必要
- *（オプション）* [LitMotion](https://github.com/AnnulusGames/LitMotion) — LitMotion クロスフェードに必要

## インストール

`Window > Package Manager` から Package Manager ウィンドウを開き、`[+] > Add package from git URL` を選択して以下の URL を入力します。

```
https://github.com/AndanteTribe/Radio.git?path=src/Radio.Unity/Packages/jp.andantetribe.radio
```

Addressables 再生または LitMotion クロスフェードを使用する場合だけ、上記のオプションパッケージもインストールしてください。スクリプティングシンボルを手動で設定する必要はありません。

## クイックスタート

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
        // この GameObject 上に SE チャンネル + BGM チャンネル × 3 を生成します
        _player = new AudioPlayer(gameObject);
    }

    private async void Start()
    {
        // BGM を再生（デフォルトはループ）
        // destroyCancellationToken は Unity 2022.2 以降で利用可能な MonoBehaviour プロパティです
        _player.PlayBgmAsync(_bgmClip, loop: true, destroyCancellationToken).Forget();

        // 効果音を再生（完了まで待機）
        await _player.PlaySeAsync(_seClip, destroyCancellationToken);
    }
}
```

`AudioPlayer` は外部アセットのハンドルを保持しないため、`Dispose` は不要です。Addressables から再生する場合は `AddressableAudioPlayer` を生成し、破棄時に `Dispose` を呼び出してください。

### Addressables を利用する場合

```csharp
using Cysharp.Threading.Tasks;
using Radio;
using UnityEngine;

public class AddressableRadioSample : MonoBehaviour
{
    private AddressableAudioPlayer _player;

    private void Awake()
    {
        // この GameObject 上に SE チャンネル + BGM チャンネル × 3 を生成します
        _player = new AddressableAudioPlayer(gameObject);
    }

    private void Start()
    {
        // Addressables の文字列アドレスから BGM をロードして再生します
        _player.PlayBgmAsync(
            "assets/audio/bgm/MainTheme.wav",
            loop: true,
            destroyCancellationToken).Forget();
    }

    private void OnDestroy()
    {
        // 保持しているすべての BGM ハンドルを解放します
        _player.Dispose();
    }
}
```

## API

### コンストラクタ

| コンストラクタ | 説明 |
|--------------|------|
| `AudioPlayer(GameObject root, uint bgmChannelCount = 3, bool useVoice = false)` | `root` に `AudioSource` コンポーネントをアタッチしてプレイヤーを初期化します。`useVoice` を `true` にするとボイス専用チャンネルが有効になります。 |
| `AddressableAudioPlayer(GameObject root, uint bgmChannelCount = 3, bool useVoice = false, AssetsRegistry? bgmRegistry = null)` | `AudioPlayer` の機能に Addressables のロードとハンドル管理を追加します。*（Addressables と AssetsRegistry が必要）* |

### プロパティ

| プロパティ | 説明 |
|------------|------|
| `AudioSources Sources` | このプレイヤーが管理する `AudioSource` を公開します。 |
| `AudioSource Sources.Se` | 効果音用のチャンネルです。 |
| `AudioSource? Sources.Voice` | ボイス用のチャンネルです。`useVoice` が `false` の場合は `null` です。 |
| `IReadOnlyList<AudioSource> Sources.Bgm` | ローテーション順の BGM チャンネルです。 |
| `IReadOnlyList<AudioSource> Sources.All` | SE、任意の Voice、BGM の順に並んだ全チャンネルです。 |

`Sources.Bgm` と `Sources.All` のリスト構造は読み取り専用ですが、各 `AudioSource` の `volume`、`outputAudioMixerGroup`、`spatialBlend` などは利用者が変更できます。

### メソッド

| メソッド | 説明 |
|--------|------|
| `PlayBgmAsync(AudioClip clip, bool loop, CancellationToken cancellationToken)` | 指定した BGM クリップを次の BGM チャンネルで再生します。デフォルトはループ再生です。 |
| `PlaySeAsync(AudioClip clip, CancellationToken cancellationToken)` | 効果音を再生し、クリップの長さだけ待機します。 |
| `PlayVoiceAsync(AudioClip clip, CancellationToken cancellationToken)` | ボイスクリップを再生します。*（コンストラクタで `useVoice: true` が必要）* |
| `CrossFadeBgmAsync(AudioClip clip, bool loop, CancellationToken cancellationToken)` | 設定済みの `IBgmTransition` を使って BGM を遷移します。 |
| `ConfigureBgmTransition(IBgmTransition transition)` | `CrossFadeBgmAsync` が使用する独自の BGM トランジションを設定します。 |
| `StopAllBgm()` | 再生中のすべての BGM チャンネルを停止します。`AddressableAudioPlayer` では保持中の BGM ハンドルも解放します。 |
| `PlayBgmAsync` / `PlaySeAsync` / `PlayVoiceAsync` / `CrossFadeBgmAsync` の Addressables オーバーロード | 文字列アドレスまたは `AssetReferenceT<AudioClip>` からロードして再生します。*（`AddressableAudioPlayer` のみ）* |
| `SetMasterVolume(float volume)` | すべてのチャンネルに適用されるマスターボリューム（0〜1）を設定します。 |
| `SetBgmVolume(float volume)` | BGM のボリューム（0〜1）を設定します。 |
| `SetSeVolume(float volume)` | 効果音のボリューム（0〜1）を設定します。 |
| `SetVoiceVolume(float volume)` | ボイスのボリューム（0〜1）を設定します。*（コンストラクタで `useVoice: true` が必要）* |
| `Dispose()` | 保持中の BGM アセットハンドルを解放します。*（`AddressableAudioPlayer` のみ）* |

### BGM トランジション

LitMotion を利用する場合は、プレイヤーの生成後にクロスフェードを設定します。

```csharp
var player = new AudioPlayer(gameObject)
    .UseLitMotionCrossFade(TimeSpan.FromSeconds(3));

await player.CrossFadeBgmAsync(nextBgmClip, cancellationToken: destroyCancellationToken);
```

LitMotion に依存しない独自の遷移は、次の公開インターフェースを実装して設定できます。

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

`BgmTransitionContext` が公開する操作は、現在の BGM チャンネル、管理ボリューム、次の BGM チャンネルの取得、およびトランジション中のチャンネルに対する一時的なボリューム制御に限定されています。

## ライセンス

このライブラリは、MIT ライセンスで公開しています。
