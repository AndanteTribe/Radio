#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AndanteTribe.Unity.Extensions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.Util;
using UnityEngine.TestTools;

namespace Radio.Tests
{
    /// <summary>
    /// Verifies that the generic LitMotion entry point preserves the Addressables player API.
    /// </summary>
    public class AddressableLitMotionIntegrationTests
    {
        private const string IntroAddress = "radio-integration-intro";
        private const string LoopGuid = "5feab473c38a4dcfbadc77f30cc75985";

        private GameObject _root = null!;
        private AudioClip _intro = null!;
        private AudioClip _loop = null!;
        private InMemoryAudioClipProvider _provider = null!;
        private ResourceLocationMap _locator = null!;

        [SetUp]
        public void SetUp()
        {
            PrepareAddressablesForDirectLocatorUse();
            _root = new GameObject("AddressableLitMotionIntegrationTests");
            _root.AddComponent<AudioListener>();
            _intro = CreateClip("Integration Intro");
            _loop = CreateClip("Integration Loop");
            _provider = new InMemoryAudioClipProvider(new Dictionary<string, AudioClip>
            {
                [IntroAddress] = _intro,
                [LoopGuid] = _loop,
            });
            UnityEngine.AddressableAssets.Addressables.ResourceManager.ResourceProviders.Add(_provider);
            _locator = new ResourceLocationMap("RadioIntegrationTests", capacity: 2);
            AddLocation(IntroAddress);
            AddLocation(LoopGuid);
            UnityEngine.AddressableAssets.Addressables.AddResourceLocator(_locator);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.AddressableAssets.Addressables.RemoveResourceLocator(_locator);
            UnityEngine.AddressableAssets.Addressables.ResourceManager.ResourceProviders.Remove(_provider);
            DestroyImmediate(_root);
            DestroyImmediate(_intro);
            DestroyImmediate(_loop);
        }

        [UnityTest]
        public IEnumerator AddressablePlayerUsesLitMotionForAddressAndReferenceOverloads() => UniTask.ToCoroutine(async () =>
        {
            var registry = new AssetsRegistry();
            var player = new AddressableAudioPlayer(
                    _root,
                    bgmChannelCount: 2,
                    bgmRegistry: registry)
                .UseLitMotionCrossFade(TimeSpan.FromSeconds(0.05f));

            Assert.That(player, Is.InstanceOf<AddressableAudioPlayer>());

            await player.CrossFadeBgmAsync(IntroAddress);
            await player.CrossFadeBgmAsync(new AssetReferenceT<AudioClip>(LoopGuid), loop: false);

            Assert.That(player.Sources.Bgm[0].clip, Is.Null);
            Assert.That(player.Sources.Bgm[1].clip, Is.SameAs(_loop));
            Assert.That(player.Sources.Bgm[1].loop, Is.False);
            Assert.That(registry.Count, Is.EqualTo(2));

            player.Dispose();

            Assert.That(registry.Count, Is.Zero);
            Assert.That(_provider.ReleaseCount(IntroAddress), Is.EqualTo(1));
            Assert.That(_provider.ReleaseCount(LoopGuid), Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator AddressablePlayerUsesLitMotionForDirectClipOverload() => UniTask.ToCoroutine(async () =>
        {
            var player = new AddressableAudioPlayer(
                    _root,
                    bgmChannelCount: 2)
                .UseLitMotionCrossFade(TimeSpan.FromSeconds(0.05f));

            await player.CrossFadeBgmAsync(_intro, loop: false);

            Assert.That(player.Sources.Bgm[0].clip, Is.SameAs(_intro));
            Assert.That(player.Sources.Bgm[0].loop, Is.False);
            player.Dispose();
        });

        private void AddLocation(string key)
        {
            _locator.Add(key, new ResourceLocationBase(key, key, _provider.ProviderId, typeof(AudioClip)));
        }

        private static void PrepareAddressablesForDirectLocatorUse()
        {
            var addressablesInstanceField = typeof(UnityEngine.AddressableAssets.Addressables)
                .GetField("m_AddressablesInstance", BindingFlags.NonPublic | BindingFlags.Static);
            var addressablesImplType = addressablesInstanceField!.GetValue(null)!.GetType();
            var addressablesInstance = Activator.CreateInstance(
                addressablesImplType,
                new LRUCacheAllocationStrategy(1000, 1000, 100, 10));
            addressablesInstanceField.SetValue(null, addressablesInstance);
            addressablesImplType
                .GetField("hasStartedInitialization", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)!
                .SetValue(addressablesInstance, true);
            addressablesImplType
                .GetField("m_InitializationOperation", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(addressablesInstance, default(AsyncOperationHandle<IResourceLocator>));
            addressablesImplType
                .GetField("m_OnHandleCompleteAction", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(addressablesInstance, new Action<AsyncOperationHandle>(_ => { }));
        }

        private static AudioClip CreateClip(string name)
        {
            const int sampleRate = 44100;
            const float seconds = 1.0f;
            var samples = Mathf.CeilToInt(sampleRate * seconds);
            return AudioClip.Create(name, samples, channels: 1, frequency: sampleRate, stream: false);
        }

        private static void DestroyImmediate(UnityEngine.Object obj)
        {
            if (obj != null)
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }
    }
}
