using System;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Objects.Network.Responses;
static class GroupsContractTests
{
    public static void Run()
    {
        const string json="{\"x.y\":9007199254740993,\"$key\":{\"\":null},\"fraction\":1.0000000000000001,\"empty\":\"\"}";
        var value=GroupJsonData.FromJson(json);Require(value.Json==json,"lossless arbitrary JSON");
        Require(GroupsJson.Quote("a\"\\\n}")=="\"a\\\"\\\\\\n}\"","wire quoting");
        foreach(var raw in new[]{"[]","null","{\"n\":1e999}","{\"n\":NaN}","{\"x\":1,\"x\":2}","{\"a\":\""+new string('x',2048)+"\"}",new string('[',18)+"0"+new string(']',18)}){
            bool rejected=false;try{GroupJsonData.FromJson(raw);}catch(FormatException){rejected=true;}Require(rejected,"reject invalid data");
        }
        string id="aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",time="2026-10-16T00:00:00Z";
        string group="{\"id\":\""+id+"\",\"revision\":0,\"name\":\"A\",\"description\":\"\",\"joinMode\":\"invite\",\"leaderId\":\""+id+"\",\"memberCount\":21,\"data\":"+json+",\"myRole\":\"leader\",\"createdAt\":\""+time+"\",\"updatedAt\":\""+time+"\"}";
        var parsed=GroupsJson.Group(group);Require(parsed.data.Json==json&&parsed.memberCount==21,"retained group and data");
        var copy=GroupsJson.Clone(parsed);copy.name="mutated";Require(parsed.name=="A"&&ReferenceEquals(parsed.data,copy.data),"mutable DTO separate, immutable raw shared");
        var publicGroup=GroupsJson.Group(group.Replace(",\"data\":"+json,"").Replace(",\"myRole\":\"leader\"",""));Require(publicGroup.data==null&&publicGroup.myRole==null,"omitted private data/role");
        foreach(var revision in new[]{"1.0000000000000001","9007199254740992","1e0","-1","null"}){
            bool rejected=false;try{GroupsJson.Group(group.Replace("\"revision\":0","\"revisi\\u006fn\":"+revision));}catch(Exception e)when(e is FormatException||e is System.Collections.Generic.KeyNotFoundException){rejected=true;}Require(rejected,"decoded revision tokens");
        }
        Console.WriteLine("Groups lossless JSON, omission/null, safe revision and bounded data contracts passed.");
    }
    static void Require(bool yes,string name){if(!yes)throw new Exception("Groups contract: "+name);}
}
