using PM.horizOn.Cloud.Helper;
internal static class FriendsContractTests
{
    public static void Run()
    {
        const string id="11223344-5566-7788-99aa-bbccddeeff00", code="H-ESIzRFVmd4iZqrvM3e7_AA";
        Check(FriendsJson.Code(id)==code,"network byte order matches Java UUID");
        Check(FriendsJson.TryResolve(code,out var resolved)&&resolved==id,"canonical code round trip");
        Check(!FriendsJson.TryResolve(code[..^1]+"B",out _)&&!FriendsJson.TryResolve(" "+id,out _),"noncanonical padding bits and trimmed identities refused");
        const string display="{\"name\":null,\"profile\":{\"avatarId\":null,\"frameId\":null,\"badges\":[]}}";
        string json="{\"userId\":\""+id+"\",\"friendCode\":\""+code+"\",\"display\":"+display+"}";
        var lookup=FriendsJson.Clone(FriendsJson.Lookup(json));Check(lookup.display.name==null&&lookup.display.profile.avatarId==null&&lookup.display.profile.frameId==null,"nullable public fields remain absent");
        string snapshot="{\"userId\":\""+id+"\",\"revision\":9007199254740991,\"friendCode\":\""+code+"\",\"friends\":[],\"incoming\":[],\"outgoing\":[],\"blocked\":[],\"limits\":{\"maxFriends\":20,\"maxIncomingRequests\":20,\"maxOutgoingRequests\":20,\"maxBlocked\":100},\"serverTime\":\"2026-10-16T00:00:00.123456789Z\"}";
        Check(FriendsJson.Snapshot(snapshot).revision==9007199254740991L,"safe maximum revision preserved");
        foreach(string value in new[]{"9007199254740992","1e3","1.0000000000000001","-1","\"1\""})
        {bool rejected=false;try{FriendsJson.Snapshot(snapshot.Replace("\"revision\":9007199254740991","\"rev\\u0069sion\":"+value));}catch{rejected=true;}Check(rejected,"original decoded revision lexeme refused");}
        Console.WriteLine("Friends canonical H-code, nullable public projection and exact revision contracts passed.");
    }
    private static void Check(bool condition,string reason){if(!condition)throw new Exception("Friends contract: "+reason);}
}
