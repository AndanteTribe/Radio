# Radio
[![unity-meta-file-check](https://github.com/AndanteTribe/Radio/actions/workflows/unity-meta-file-check.yml/badge.svg)](https://github.com/AndanteTribe/Radio/actions/workflows/unity-meta-file-check.yml)
[![Releases](https://img.shields.io/github/release/AndanteTribe/Radio.svg)](https://github.com/AndanteTribe/Radio/releases)
[![GitHub license](https://img.shields.io/github/license/AndanteTribe/Radio.svg)](./LICENSE)
[![openupm](https://img.shields.io/npm/v/jp.andantetribe.radio?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/jp.andantetribe.radio/)

[English](README.md) | 日本語

## 概要
**Radio** は、Unity 向けの BGM・効果音・インタラクティブ音楽再生ライブラリです。

`AudioPlayerCore` は複数の `AudioSource` チャンネル（SE・ボイス・BGM）を管理し、Unity の Addressables システムと連携してアセットをロードします。BGM ハンドルはすべて [AssetsRegistry](https://github.com/AndanteTribe/AssetsRegistry) によってキャッシュされ、`Dispose` 呼び出し時に自動的に解放されます。

オプションとして、[LitMotion](https://github.com/AnnulusGames/LitMotion) が利用可能な場合（スクリプティングシンボル `ENABLE_LITMOTION` が設定されている場合）、クロスフェードによる BGM トランジションもサポートされます。

## 要件
- Unity 2022.3 以上
- [Addressables](https://docs.unity3d.com/Manual/com.unity.addressables.html) 1.21.21 以上
- [UniTask](https://github.com/Cysharp/UniTask) 2.5.10 以上
- [AssetsRegistry](https://github.com/AndanteTribe/AssetsRegistry) 1.0.4 以上
- *（オプション）* [LitMotion](https://github.com/AnnulusGames/LitMotion) — クロスフェード BGM サポートに必要（スクリプティングシンボル `ENABLE_LITMOTION` で有効化）

## インストール
`Window > Package Manager` から Package Manager ウィンドウを開き、`[+] > Add package from git URL` を選択して以下の URL を入力します。

```
https://github.com/AndanteTribe/Radio.git?path=src/Radio.Unity/Packages/jp.andantetribe.radio
```

## クイックスタート

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
        // この GameObject 上に SE チャンネル + BGM チャンネル × 3 を生成します
        _player = new AudioPlayerCore(gameObject);
    }

    private async void Start()
    {
        // BGM を再生（デフォルトはループ）
        // destroyCancellationToken は Unity 2022.2 以降で利用可能な MonoBehaviour プロパティです
        _player.PlayBgmAsync("assets/audio/bgm/MainTheme.wav", loop: true, destroyCancellationToken).Forget();

        // 効果音を再生（完了まで待機）
        await _player.PlaySeAsync("assets/audio/se/Click.wav", destroyCancellationToken);
    }

    private void OnDestroy()
    {
        // キャッシュされたすべての BGM ハンドルを解放します
        _player.Dispose();
    }
}
```

## API

### コンストラクタ

| コンストラクタ | 説明 |
|--------------|------|
| `AudioPlayerCore(GameObject root, uint bgmChannelCount = 3, bool useVoice = false, AssetsRegistry? bgmRegistry = null)` | `root` に `AudioSource` コンポーネントをアタッチしてプレイヤーを初期化します。`bgmChannelCount` で BGM チャンネル数を設定します。`useVoice` を `true` にするとボイス専用チャンネルが有効になります。 |
| `AudioPlayerCore(GameObject root, TimeSpan fadeDuration, uint bgmChannelCount = 3, bool useVoice = false, AssetsRegistry? bgmRegistry = null)` | 上記と同様ですが、クロスフェードの `fadeDuration` パラメータが追加されています。*（`ENABLE_LITMOTION` が必要）* |

### メソッド

| メソッド | 説明 |
|--------|------|
| `PlayBgmAsync(string address, bool loop, CancellationToken cancellationToken)` | 指定した Addressables アドレスから BGM クリップをロードして再生します。デフォルトはループ再生です。 |
| `PlayBgmAsync(AssetReferenceT<AudioClip> reference, bool loop, CancellationToken cancellationToken)` | `AssetReferenceT<AudioClip>` から BGM クリップをロードして再生します。デフォルトはループ再生です。 |
| `StopAllBgm()` | 再生中のすべての BGM チャンネルを停止し、キャッシュされたハンドルを解放します。 |
| `PlaySeAsync(string address, CancellationToken cancellationToken)` | 効果音をロードして再生し、再生完了後にハンドルを解放します。 |
| `PlaySeAsync(AssetReferenceT<AudioClip> reference, CancellationToken cancellationToken)` | `AssetReferenceT<AudioClip>` から効果音をロードして再生します。 |
| `PlayVoiceAsync(string address, CancellationToken cancellationToken)` | ボイスクリップをロードして再生します。*（コンストラクタで `useVoice: true` が必要）* |
| `PlayVoiceAsync(AssetReferenceT<AudioClip> reference, CancellationToken cancellationToken)` | `AssetReferenceT<AudioClip>` からボイスクリップをロードして再生します。*（コンストラクタで `useVoice: true` が必要）* |
| `CrossFadeBgmAsync(string address, bool loop, CancellationToken cancellationToken)` | 新しい BGM トラックへクロスフェードトランジションを行います。*（`ENABLE_LITMOTION` が必要）* |
| `CrossFadeBgmAsync(AssetReferenceT<AudioClip> reference, bool loop, CancellationToken cancellationToken)` | `AssetReferenceT<AudioClip>` から新しい BGM トラックへクロスフェードします。*（`ENABLE_LITMOTION` が必要）* |
| `SetMasterVolume(float volume)` | すべてのチャンネルに適用されるマスターボリューム（0〜1）を設定します。 |
| `SetBgmVolume(float volume)` | BGM のボリューム（0〜1）を設定します。 |
| `SetSeVolume(float volume)` | 効果音のボリューム（0〜1）を設定します。 |
| `SetVoiceVolume(float volume)` | ボイスのボリューム（0〜1）を設定します。*（コンストラクタで `useVoice: true` が必要）* |
| `Dispose()` | キャッシュされたすべての BGM アセットハンドルを解放します。 |

## ライセンス
このライブラリは、MIT ライセンスで公開しています。
