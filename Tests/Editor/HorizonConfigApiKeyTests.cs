using System;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using PM.horizOn.Cloud.Core;

namespace PM.horizOn.Cloud.Tests
{
    /// <summary>
    /// TASK-451: the API key obfuscation must not depend on the machine that
    /// imported the config. A config asset is encrypted in the editor on the
    /// developer machine and decrypted at runtime on the player device, so any
    /// machine-specific key material destroys the API key in shipped builds.
    /// These tests simulate assets moving between machines by writing the
    /// serialized field directly.
    /// </summary>
    public class HorizonConfigApiKeyTests
    {
        private const string PlainKey = "test-api-key-12345";

        // Ciphertext of PlainKey under the portable v1 scheme, precomputed
        // outside Unity. If any machine-specific input leaks into the key
        // derivation, decrypting this fixture fails on every machine.
        private const string PortableCiphertext = "hzn1:HAoBHVcuHkduBwoMSWJ2eBp0";

        private static HorizonConfig NewConfigWithSerializedKey(string encryptedValue)
        {
            var config = ScriptableObject.CreateInstance<HorizonConfig>();
            var so = new SerializedObject(config);
            so.FindProperty("_encryptedApiKey").stringValue = encryptedValue;
            so.ApplyModifiedPropertiesWithoutUndo();
            return config;
        }

        private static string ReadSerializedKey(HorizonConfig config)
        {
            return new SerializedObject(config).FindProperty("_encryptedApiKey").stringValue;
        }

        // Replicates the pre-fix device-bound XOR scheme so tests can fabricate
        // legacy assets from arbitrary machines.
        private static string LegacyEncrypt(string plainText, string keyMaterial)
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] keyBytes = Encoding.UTF8.GetBytes(keyMaterial);
            byte[] encrypted = new byte[plainBytes.Length];
            for (int i = 0; i < plainBytes.Length; i++)
            {
                encrypted[i] = (byte)(plainBytes[i] ^ keyBytes[i % keyBytes.Length]);
            }
            return Convert.ToBase64String(encrypted);
        }

        [Test]
        public void ApiKey_FromPortableCiphertext_DecryptsOnAnyMachine()
        {
            var config = NewConfigWithSerializedKey(PortableCiphertext);

            Assert.AreEqual(PlainKey, config.ApiKey);
        }

        [Test]
        public void SetApiKey_ProducesPortableFormat()
        {
            var config = ScriptableObject.CreateInstance<HorizonConfig>();
            config.SetApiKey(PlainKey);

            StringAssert.StartsWith("hzn1:", ReadSerializedKey(config));
        }

        [Test]
        public void SetApiKey_SerializedRoundTrip_SurvivesAssetTransfer()
        {
            var source = ScriptableObject.CreateInstance<HorizonConfig>();
            source.SetApiKey(PlainKey);

            var receiver = NewConfigWithSerializedKey(ReadSerializedKey(source));

            Assert.AreEqual(PlainKey, receiver.ApiKey);
        }

        [Test]
        public void ApiKey_LegacyAssetFromOtherMachine_FailsLoudlyInsteadOfGarbage()
        {
            string foreignBlob = LegacyEncrypt(PlainKey,
                "some-other-machines-device-identifier" + Application.productName);
            var config = NewConfigWithSerializedKey(foreignBlob);

            LogAssert.Expect(LogType.Error, new Regex("different machine|re-import|Config Importer"));
            Assert.AreEqual(string.Empty, config.ApiKey);
        }

        [Test]
        public void ApiKey_LegacyAssetFromThisMachine_IsMigrated()
        {
            string localBlob = LegacyEncrypt(PlainKey,
                SystemInfo.deviceUniqueIdentifier + Application.productName);
            var config = NewConfigWithSerializedKey(localBlob);

            LogAssert.Expect(LogType.Warning, new Regex("[Ll]egacy"));
            Assert.AreEqual(PlainKey, config.ApiKey);
            StringAssert.StartsWith("hzn1:", ReadSerializedKey(config));
        }

        [Test]
        public void SetApiKey_RuntimeInjection_IsUsableImmediately()
        {
            var config = ScriptableObject.CreateInstance<HorizonConfig>();

            config.SetApiKey(PlainKey);

            Assert.AreEqual(PlainKey, config.ApiKey);
        }
    }
}
