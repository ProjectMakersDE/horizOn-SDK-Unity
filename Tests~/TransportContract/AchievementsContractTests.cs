using PM.horizOn.Cloud.Helper;
internal static class AchievementsContractTests
{
    public static void Run()
    {
        string definition="{\"key\":\"starter\",\"name\":\"Start\",\"description\":\"\",\"imageUrl\":null,\"hidden\":false,\"active\":true,\"mode\":\"statistic\",\"condition\":{\"statisticKey\":\"score\",\"threshold\":9007199254740991},\"rewards\":{\"values\":[{\"key\":\"gold\",\"amount\":9007199254740991}]}}";
        string snapshot="{\"userId\":\"00000000-0000-4000-8000-000000001008\",\"revision\":1,\"definitionVersion\":\""+new string('a',64)+"\",\"definitions\":["+definition+"],\"progress\":[],\"limits\":{\"maxDefinitions\":20,\"maxLifetimeKeys\":500},\"serverTime\":\"2026-10-16T00:00:00Z\"}";
        var parsed=AchievementsJson.Snapshot(snapshot);Check(parsed.definitions[0].condition.threshold==9007199254740991L&&parsed.definitions[0].rewards.values[0].amount==9007199254740991L,"safe integer preserved");
        Check(AchievementsJson.Snapshot(snapshot.Replace("\"definitions\":["+definition+"]","\"definitions\":null")).definitions==null,"null metadata preserved distinctly");
        Check(AchievementsJson.Snapshot(snapshot.Replace("\"definitions\":["+definition+"]","\"definitions\":[]")).definitions.Length==0,"empty metadata remains explicit replacement");
        foreach(string field in new[]{"amount","threshold"})foreach(string value in new[]{"9007199254740992","1e3","1.0000000000000001","\"1\"","-1"})
        {
            string escaped=field.Substring(0,1)+"\\u"+((int)field[1]).ToString("x4")+field.Substring(2);
            bool refused=false;try{AchievementsJson.Snapshot(snapshot.Replace("\""+field+"\":9007199254740991","\""+escaped+"\":"+value));}catch{refused=true;}
            Check(refused,"decoded numeric key refuses unchanged unsafe lexeme");
        }
        bool duplicate=false;try{AchievementsJson.Snapshot(snapshot.Replace("\"revision\":1","\"revision\":1,\"\\u0072evision\":2"));}catch{duplicate=true;}Check(duplicate,"duplicate decoded keys rejected");
        string complete="{\"key\":\"starter\",\"unlockedAt\":null,\"rewardState\":\"complete\",\"inboxEntryId\":null,\"pendingReward\":null}";
        var compact=AchievementsJson.Snapshot(snapshot.Replace("\"progress\":[]","\"progress\":["+complete+"]"));
        Check(compact.progress[0].unlockedAt==null&&compact.progress[0].pendingReward==null&&compact.progress[0].rewardState=="complete","compact permanent unlock has no fabricated date or reward copy");
        foreach(var invalid in new[]{complete.Replace("\"unlockedAt\":null","\"unlockedAt\":\"2026-10-16T00:00:00Z\""),complete.Replace("\"rewardState\":\"complete\"","\"rewardState\":\"pending\"")})
        {bool refused=false;try{AchievementsJson.Snapshot(snapshot.Replace("\"progress\":[]","\"progress\":["+invalid+"]"));}catch{refused=true;}Check(refused,"compact and legacy formats cannot be mixed");}
        string compactSnapshot=snapshot.Replace("\"progress\":[]","\"progress\":["+complete+"]");
        string fresh="{\"achievements\":"+compactSnapshot+",\"newlyUnlocked\":[\"starter\"],\"replayed\":false,\"deliveryResults\":[]}";
        Check(AchievementsJson.Unlock(fresh).deliveryResults.Length==0,"compact unlock contains no delivery outcome");
        bool missingUnlock=false;try{AchievementsJson.Unlock(fresh.Replace("[\"starter\"]","[]"));}catch{missingUnlock=true;}
        Check(missingUnlock,"fresh unlock must name exactly one newly unlocked key");
        string definitions=string.Join(",",Enumerable.Range(0,701).Select(i=>definition.Replace("starter","k"+i)));
        string progress=string.Join(",",Enumerable.Range(0,701).Select(i=>complete.Replace("starter","k"+i)));
        string raised=snapshot.Replace("\"definitions\":["+definition+"]","\"definitions\":["+definitions+"]").Replace("\"progress\":[]","\"progress\":["+progress+"]")
            .Replace("\"maxDefinitions\":20,\"maxLifetimeKeys\":500","\"maxDefinitions\":800,\"maxLifetimeKeys\":1000");
        Check(AchievementsJson.Snapshot(raised).progress.Length==701,"admin can raise the default lifetime limit");
        Check(AchievementsJson.Snapshot(raised.Replace("\"maxDefinitions\":800,\"maxLifetimeKeys\":1000","\"maxDefinitions\":20,\"maxLifetimeKeys\":50")).progress.Length==701,"lowered limits preserve existing unlocked state");
        Console.WriteLine("Achievements opaque metadata, exact safe integers and decoded lexeme contracts passed.");
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception("Achievements contract: "+message);}
}
