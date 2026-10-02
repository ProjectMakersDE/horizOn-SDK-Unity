using System.Collections.Generic;
using NUnit.Framework;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Objects.Network.Requests;
using UnityEngine;

namespace PM.horizOn.Cloud.Tests
{
    [Category("Transport")]
    public class EmailSendingJsonTransportTests
    {
        [System.Serializable]
        private sealed class ScalarRequestFields
        {
            public string userId;
            public string templateSlug;
            public string language;
        }

        [Test]
        public void SendEmailRequest_WritesVariablesAsObjectAlongsideOrdinaryFields()
        {
            var request = new SendEmailRequest
            {
                userId = "recipient-1",
                templateSlug = "tutorial-mail",
                variables = new Dictionary<string, string>
                {
                    ["username"] = "Ada",
                    ["scenario"] = "Tutorial"
                },
                language = "en"
            };

            string json = JsonHelper.ToJsonExcludeEmpty(request);

            var scalarFields = JsonUtility.FromJson<ScalarRequestFields>(json);
            Assert.That(scalarFields.userId, Is.EqualTo("recipient-1"));
            Assert.That(scalarFields.templateSlug, Is.EqualTo("tutorial-mail"));
            Assert.That(scalarFields.language, Is.EqualTo("en"));
            Assert.That(json, Does.Contain("\"variables\":{"));
            Assert.That(json, Does.Contain("\"username\":\"Ada\""));
            Assert.That(json, Does.Contain("\"scenario\":\"Tutorial\""));
            Assert.That(json, Does.Not.Contain("\"variables\":["));
        }

        [Test]
        public void SendEmailRequest_EscapesVariableKeyAndValue()
        {
            var request = new SendEmailRequest
            {
                variables = new Dictionary<string, string>
                {
                    ["quote\"\n"] = "slash\\\ttab"
                }
            };

            string json = JsonHelper.ToJsonExcludeEmpty(request);

            Assert.That(json, Does.Contain("\"variables\":{\"quote\\\"\\n\":\"slash\\\\\\ttab\"}"));
        }

        [Test]
        public void SendEmailRequest_KeepsEmptyObjectAndNullOrEmptyValues()
        {
            var request = new SendEmailRequest
            {
                variables = new Dictionary<string, string>()
            };
            Assert.That(JsonHelper.ToJsonExcludeEmpty(request), Does.Contain("\"variables\":{}"));

            request.variables["nil"] = null;
            request.variables["empty"] = "";
            string json = JsonHelper.ToJsonExcludeEmpty(request);
            Assert.That(json, Does.Contain("\"variables\":{\"nil\":null,\"empty\":\"\"}"));
        }
    }
}
