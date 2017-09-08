using System;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

internal static class GitHubLastestRealease
{
    [DataContract]
    private class LastestRealease
    {
        public static LastestRealease Parse(Stream stream)
        {
            var serializer = new DataContractJsonSerializer(typeof(LastestRealease));
            return (LastestRealease)serializer.ReadObject(stream);
        }

        [DataMember(Name = "tag_name", IsRequired = true)]
        public string TagName { get; set; }
    }
    public static bool CheckNewVersion(string uri)
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName();

            LastestRealease last;

            var req = HttpWebRequest.Create(uri) as HttpWebRequest;
            req.UserAgent = asm.FullName;
            req.Timeout = 5000;
            using (var res = req.GetResponse())
            using (var stream = res.GetResponseStream())
                last = LastestRealease.Parse(stream);

            return new Version(last.TagName) > asm.Version;
        }
        catch
        {
            return false;
        }
    }
}
