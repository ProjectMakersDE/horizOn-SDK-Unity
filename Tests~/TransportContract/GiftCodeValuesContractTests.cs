using System;
using System.Linq;
using PM.horizOn.Cloud.Examples.Features;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Transport;
using UnityEngine;

internal static class GiftCodeValuesContractTests
{
    public static void Run()
    {
        var user = new UserData { UserId = "gift-player", AccessToken = "session" };
        string id = "00000000-0000-4000-8000-000000001007";
        var intent = new PM.horizOn.Cloud.Objects.Network.Responses.GiftCodeIntent { userId=user.UserId,code="ORIGINAL",projectScope=PM.horizOn.Cloud.Objects.Network.Responses.GiftCodeIntent.ProjectFingerprint("gift-project"),campaignId=id,expectedCount="9007199254740993",configRevision="9223372036854775807" };
        Check(GiftCodeTransportContract.TryCreateRedeemPlan(user,"session","gift-project",intent,out var plan) && plan.Request.expectedCount==intent.expectedCount,"exact original tuple in request");
        Check(!GiftCodeTransportContract.TryCreateRedeemPlan(user,"session","other",intent,out _),"project cannot retarget intent");
        Check(!GiftCodeTransportContract.TryCreateRedeemPlan(user,"session","CODE",out _),"V1 closed");
        foreach (string invalid in new[] {null,"","00","01","+1","-1","1.0","1e2"," 1","9223372036854775808"})
            Check(!PM.horizOn.Cloud.Objects.Network.Responses.GiftCodeIntent.CanonicalCount(invalid),"invalid/overflow sequence refused");
        string prepared="{\"valid\":true,\"protocolVersion\":2,\"campaignId\":\""+id+"\",\"expectedCount\":\"9007199254740993\",\"configRevision\":\"9223372036854775807\"}";
        Check(GiftCodeJson.Validate(prepared).expectedCount=="9007199254740993","prepare beyond double remains exact");
        try { GiftCodeJson.Validate(prepared.Replace("\"9007199254740993\"","9007199254740993")); throw new Exception("Numeric count accepted"); } catch (FormatException) { }
        string state = "{\"userId\":\"gift-player\",\"day\":\"2026-10-16\",\"revision\":1,\"definitionVersion\":1,\"values\":[{\"key\":\"gold\",\"balance\":9007199254740991,\"earnedToday\":1,\"dailyCap\":null}],\"operationWindow\":{\"serverTime\":\"2026-10-16T00:00:00Z\",\"oldestAcceptedAt\":\"2026-09-16T00:00:00Z\"}}";
        string valid = "{\"campaignId\":\""+id+"\",\"count\":\"9007199254740994\",\"configRevision\":\"1\",\"alreadyApplied\":false,\"success\":true,\"grantedValues\":[{\"key\":\"1.gold\",\"amount\":9007199254740991}],\"playerValues\":" + state + ",\"replayed\":false}";
        Check(GiftCodeJson.Redeem(valid).grantedValues[0].amount == 9007199254740991L, "maximum safe grant remains exact");
        foreach (string invalid in new[] { "9007199254740992", "1.0", "1e2", "-1", "0", "\"1\"", "null" })
            Reject(valid.Replace("\"amount\":9007199254740991", "\"amou\\u006et\":" + invalid));
        Reject(valid.Replace("\"replayed\":false", "\"replayed\":1"));
        Reject(valid.Replace("\"alreadyApplied\":false","\"alreadyApplied\":true").Replace("\"replayed\":false","\"replayed\":true"));
        Reject(valid.Replace("\"key\":\"1.gold\"", "\"key\":\"GOLD\""));
        Reject(valid.Replace("\"key\":\"1.gold\"", "\"key\":\"gold\\n\""));
        Reject(valid.Replace("\"amount\":9007199254740991", "\"amount\":1,\"amou\\u006et\":2"));
        Reject(valid.Replace("[{\"key\":\"1.gold\",\"amount\":9007199254740991}]", "[{\"key\":\"gold\",\"amount\":1},{\"key\":\"gold\",\"amount\":2}]"));
        Reject("{\"success\":true,\"grantedValues\":[{\"key\":\"gold\",\"amount\":1}],\"playerValues\":null}");
        Reject("{\"success\":true,\"grantedValues\":null}");
        Reject("{\"success\":true,\"grantedValues\":[" + string.Join(",", Enumerable.Range(0, 9).Select(i => "{\"key\":\"gold" + i + "\",\"amount\":1}")) + "]}");
        string project = "gift-project", key = GiftCodeSampleStore.Key(project, user.UserId);
        try
        {
            PlayerPrefs.DeleteKey(key);
            Check(GiftCodeSampleStore.Save(project, intent), "persisted before transport");
            Check(GiftCodeSampleStore.TryLoad(project, user.UserId, out var loaded) && loaded.code == intent.code && loaded.expectedCount == intent.expectedCount && loaded.campaignId == id, "restart preserves exact original code and UUID");
            Check(!GiftCodeSampleStore.Save(project, intent.Copy()), "uncertain intent cannot be replaced");
            Check(GiftCodeSampleStore.TryLoad("another-project", user.UserId, out var other) && other == null, "project isolation");
            Check(GiftCodeSampleStore.TryLoad(project, "another-player", out other) && other == null, "player isolation");
            Check(!GiftCodeSampleStore.Confirm(project, new PM.horizOn.Cloud.Objects.Network.Responses.GiftCodeIntent { userId=user.UserId,code="OTHER" }) && PlayerPrefs.HasKey(key), "only exact confirmed intent removed");
            Check(GiftCodeSampleStore.Confirm(project, intent) && !PlayerPrefs.HasKey(key), "successful original intent confirmed");
            PlayerPrefs.SetString(key, "broken");
            Check(!GiftCodeSampleStore.TryLoad(project, user.UserId, out _) && !GiftCodeSampleStore.Save(project, intent) && PlayerPrefs.GetString(key) == "broken", "corrupt intent stays blocked and recoverable");
            PlayerPrefs.SetString("horizOn_AnonymousToken", new string('x', 36));
            Check(GiftCodeSampleStore.PrepareIdentity(out var restore) && restore, "legacy identity selected for restore");
            PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
            Check(GiftCodeSampleStore.PrepareIdentity(out restore) && restore && PlayerPrefs.GetString("horizOn_AnonymousToken").Length == 36, "failed restore never silently creates replacement identity");
            PlayerPrefs.DeleteKey("horizOn_AnonymousToken"); PlayerPrefs.DeleteKey("horizOn_gift_identity_recovery");
            Check(GiftCodeSampleStore.PrepareIdentity(out restore) && !restore, "first identity attempt marked durably");
            Check(!GiftCodeSampleStore.PrepareIdentity(out _), "unconfirmed signup cannot create another identity");
        }
        finally
        {
            PlayerPrefs.DeleteKey(key); PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
            PlayerPrefs.DeleteKey("horizOn_gift_identity_recovery"); PlayerPrefs.DeleteKey("horizOn_gift_identity_started");
        }
        Console.WriteLine("Gift code additive DTO, safe integer, immutable intent and identity recovery contracts passed.");
    }
    private static void Reject(string json)
    {
        bool rejected = false; try { GiftCodeJson.Redeem(json); } catch (Exception) { rejected = true; }
        Check(rejected, "malformed gift code response refused before adoption");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception("Gift code contract failed: " + message); }
}
