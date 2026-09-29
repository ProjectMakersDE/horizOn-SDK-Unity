using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace PM.horizOn.Cloud.Tests
{
    /// <summary>
    /// End-to-end validated actions flow against a real horizOn backend. Not part of the CI
    /// filter (category "E2E", CI runs "Transport"); it is ignored unless the environment of the
    /// Unity process names a backend:
    ///
    ///   HORIZON_E2E_BASE_URL      backend origin, for example http://localhost:3170 (required)
    ///   HORIZON_E2E_API_KEY       app API key with validated actions rules (required)
    ///   HORIZON_E2E_LEADERBOARD   board key to bind the runs to ("" = unbound runs)
    ///   HORIZON_E2E_STAGE         stage key when the rules define stages ("" = none)
    ///   HORIZON_E2E_SCORE         score to submit (default 100)
    ///   HORIZON_E2E_RUN_SECONDS   seconds between StartRun and submit, so the rules' minimum
    ///                             duration passes (default 3)
    ///   HORIZON_E2E_EARNED_KEY    value key to credit (only when the rules define it)
    ///   HORIZON_E2E_EARNED_AMOUNT amount per run for that key (default 1)
    ///
    /// Flow: anonymous sign-up, GetState, StartRun + SubmitValidated with the raw log (automatic
    /// evidence upload when the server asks for it), StartRun + SubmitValidatedWithHash followed
    /// by a manual UploadEvidence (EVIDENCE_NOT_REQUESTED is accepted when the rules do not ask
    /// for evidence), GetState again, sign-out. About eight requests per run; no retries.
    ///
    /// Run headless, e.g.:
    ///   HORIZON_E2E_BASE_URL=... HORIZON_E2E_API_KEY=... Unity -batchmode -nographics
    ///     -projectPath .unity-ci-project -runTests -testPlatform EditMode -testCategory E2E
    ///     -testResults e2e.xml -logFile e2e.log
    /// </summary>
    [Category("E2E")]
    public class ValidatedActionsE2ETests
    {
        private const float EvidenceWaitSeconds = 10f;

        private string _baseUrl;
        private string _apiKey;
        private string _leaderboard;
        private string _stage;
        private long _score;
        private int _runSeconds;
        private string _earnedKey;
        private long _earnedAmount;

        [SetUp]
        public void SetUp()
        {
            _baseUrl = Env("HORIZON_E2E_BASE_URL").TrimEnd('/');
            _apiKey = Env("HORIZON_E2E_API_KEY");
            if (string.IsNullOrEmpty(_baseUrl) || string.IsNullOrEmpty(_apiKey))
            {
                Assert.Ignore("Set HORIZON_E2E_BASE_URL and HORIZON_E2E_API_KEY to run the validated actions E2E.");
            }

            _leaderboard = NullIfEmpty(Env("HORIZON_E2E_LEADERBOARD"));
            _stage = NullIfEmpty(Env("HORIZON_E2E_STAGE"));
            _score = EnvLong("HORIZON_E2E_SCORE", 100);
            _runSeconds = (int)EnvLong("HORIZON_E2E_RUN_SECONDS", 3);
            _earnedKey = NullIfEmpty(Env("HORIZON_E2E_EARNED_KEY"));
            _earnedAmount = EnvLong("HORIZON_E2E_EARNED_AMOUNT", 1);

            // A session cached by an earlier run must not leak into this one.
            PlayerPrefs.DeleteKey("horizOn_UserSession");
            PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
            NetworkService.ResetInstance();
            LogService.ResetInstance();
            EventService.ResetInstance();

            var config = ScriptableObject.CreateInstance<HorizonConfig>();
            config.SetApiKey(_apiKey);
            config.SetHosts(new[] { _baseUrl });
            var serialized = new SerializedObject(config);
            // WARN keeps raw response bodies (session tokens) out of the log; no retries keeps the call volume small.
            serialized.FindProperty("_logLevel").intValue = (int)Enums.LogType.WARN;
            serialized.FindProperty("_maxRetryAttempts").intValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            LogService.Instance.Initialize(config);
            LogService.Instance.EnableEventPublishing = false;
            NetworkService.Instance.Initialize(config);
            NetworkService.Instance.SetActiveHost(_baseUrl);
        }

        [TearDown]
        public void TearDown()
        {
            if (string.IsNullOrEmpty(_baseUrl) || string.IsNullOrEmpty(_apiKey))
            {
                return;
            }

            if (UserManager.Instance.IsSignedIn)
            {
                UserManager.Instance.SignOut(keepAnonymousToken: false);
            }
            foreach (var gameObject in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (gameObject.name == "[UserManager]" ||
                    gameObject.name == "[ValidatedActionsManager]" ||
                    gameObject.name == "[LeaderboardManager]")
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }
            PlayerPrefs.DeleteKey("horizOn_UserSession");
            PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
        }

        [Test]
        [Timeout(120000)]
        public async Task ValidatedActions_FullFlow_AgainstTheBackend()
        {
            // SDK errors are part of the checked flow (EVIDENCE_NOT_REQUESTED); they must not fail
            // the test as unhandled logs. The log scope is per test, so this is set in the body.
            LogAssert.ignoreFailingMessages = true;
            var validated = ValidatedActionsManager.Instance;

            string displayName = "e2e-unity-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Assert.That(await UserManager.Instance.SignUpAnonymous(displayName), Is.True,
                "anonymous sign-up against " + _baseUrl + " failed");

            // 1. State before any run.
            PlayerState before = await validated.GetState();
            Assert.That(before, Is.Not.Null, "GetState failed: " + validated.LastErrorCode);
            long balanceBefore = _earnedKey != null ? before.GetBalance(_earnedKey) : 0;

            // 2. Start a run and submit it with the raw log; the SDK uploads evidence when asked.
            string evidenceUploadedRunId = null;
            ValidatedEvidenceFailure evidenceFailure = null;
            Action<EvidenceUploadResult> onUploaded = data => evidenceUploadedRunId = data.runId;
            Action<ValidatedEvidenceFailure> onFailed = data => evidenceFailure = data;
            HorizonApp.Events.Subscribe(EventKeys.ValidatedEvidenceUploaded, onUploaded);
            HorizonApp.Events.Subscribe(EventKeys.ValidatedEvidenceUploadFailed, onFailed);
            long credited = 0;
            try
            {
                ValidatedRun first = await validated.StartRun(_leaderboard);
                Assert.That(first, Is.Not.Null, "StartRun failed: " + validated.LastErrorCode);
                Assert.That(first.ticket, Is.Not.Empty);
                Assert.That(validated.HasActiveRun, Is.True);

                await Task.Delay(TimeSpan.FromSeconds(_runSeconds));
                byte[] firstLog = InputLog(first);
                ValidatedSubmitResult firstResult = await validated.SubmitValidated(
                    _score, firstLog, _stage, null, Earned());
                Assert.That(firstResult, Is.Not.Null, "SubmitValidated failed: " + validated.LastErrorCode);
                Assert.That(firstResult.accepted, Is.True);
                Assert.That(firstResult.runId, Is.EqualTo(first.runId));
                Assert.That(validated.HasActiveRun, Is.False, "the ticket is single use");
                credited += Credited(firstResult);

                if (firstResult.evidence != null && firstResult.evidence.required)
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(EvidenceWaitSeconds);
                    while (evidenceUploadedRunId == null && evidenceFailure == null && DateTime.UtcNow < deadline)
                    {
                        await Task.Delay(100);
                    }
                    Assert.That(evidenceFailure, Is.Null,
                        "automatic evidence upload failed: " + evidenceFailure?.code);
                    Assert.That(evidenceUploadedRunId, Is.EqualTo(first.runId), "automatic evidence upload did not finish");
                }
            }
            finally
            {
                HorizonApp.Events.Unsubscribe(EventKeys.ValidatedEvidenceUploaded, onUploaded);
                HorizonApp.Events.Unsubscribe(EventKeys.ValidatedEvidenceUploadFailed, onFailed);
            }

            // 3. Second run, submitted with a ready hash; the evidence is uploaded by hand.
            ValidatedRun second = await validated.StartRun(_leaderboard);
            Assert.That(second, Is.Not.Null, "second StartRun failed: " + validated.LastErrorCode);
            await Task.Delay(TimeSpan.FromSeconds(_runSeconds));
            byte[] secondLog = InputLog(second);
            ValidatedSubmitResult secondResult = await validated.SubmitValidatedWithHash(
                _score + 1, ValidatedActionsManager.ComputeInputLogHash(secondLog), _stage, null, Earned());
            Assert.That(secondResult, Is.Not.Null, "SubmitValidatedWithHash failed: " + validated.LastErrorCode);
            Assert.That(secondResult.accepted, Is.True);
            credited += Credited(secondResult);

            bool uploaded = await validated.UploadEvidence(second.runId, secondLog);
            if (secondResult.evidence != null && secondResult.evidence.required)
            {
                Assert.That(uploaded, Is.True, "UploadEvidence failed: " + validated.LastEvidenceErrorCode);
            }
            else
            {
                Assert.That(uploaded, Is.False);
                Assert.That(validated.LastErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.EvidenceNotRequested),
                    "the rules did not ask for evidence, so the server must answer EVIDENCE_NOT_REQUESTED");
            }

            // 4. State after both runs.
            PlayerState after = await validated.GetState();
            Assert.That(after, Is.Not.Null, "second GetState failed: " + validated.LastErrorCode);
            if (_earnedKey != null)
            {
                Assert.That(after.GetBalance(_earnedKey), Is.EqualTo(balanceBefore + credited),
                    "the balance must grow by what the two submits credited");
            }

            UserManager.Instance.SignOut(keepAnonymousToken: false);
            Assert.That(UserManager.Instance.IsSignedIn, Is.False);
        }

        private List<EarnedValue> Earned()
        {
            if (_earnedKey == null)
            {
                return null;
            }
            return new List<EarnedValue> { new EarnedValue { key = _earnedKey, amount = _earnedAmount } };
        }

        private long Credited(ValidatedSubmitResult result)
        {
            if (_earnedKey == null || result.state == null)
            {
                return 0;
            }
            PlayerStateValue value = result.state.GetValue(_earnedKey);
            return value != null ? value.credited : 0;
        }

        // Deterministic stand-in for a recorded input log: derived from the run seed.
        private static byte[] InputLog(ValidatedRun run)
        {
            return Encoding.UTF8.GetBytes($"unity-e2e;run={run.runId};seed={run.seed};inputs=L,R,J,L,L,R");
        }

        private static string Env(string name)
        {
            return (Environment.GetEnvironmentVariable(name) ?? string.Empty).Trim();
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static long EnvLong(string name, long fallback)
        {
            return long.TryParse(Env(name), out long value) ? value : fallback;
        }
    }
}
