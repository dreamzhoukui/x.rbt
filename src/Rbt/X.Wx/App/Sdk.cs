﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Net;
using System.Text;
using X.Core.Utility;

namespace X.Wx.App
{
    public class Sdk
    {
        public static string uk = "";//用户key

        static string key = "";//应用key
        static string gateway = "http://rbt.80xc.com/api/";//网关
        class api
        {
            public string name;
            public object ps;
        }
        static api last;

        static T doapi<T>(string api, Dictionary<string, string> values) where T : Resp
        {
            var wc = new WebClient();
            wc.Proxy = null;
            last = new Sdk.api() { name = api, ps = values };
            if (!string.IsNullOrEmpty(uk)) wc.Headers.Add("Cookie", "ukey=" + uk);
            var fs = new NameValueCollection();
            if (values != null) foreach (var k in values.Keys) fs.Add(k, values[k]);
            string json = "";
            try
            {
                Debug.WriteLine("doapi:" + api + "@post->" + Serialize.ToJson(values));
                var data = wc.UploadValues(gateway + api, fs);
                json = Encoding.UTF8.GetString(data);
                Debug.WriteLine("doapi:" + api + "@back->->" + json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("doapi:" + api + "@err->" + ex.Message);
                return new Resp() { issucc = false, msg = ex.Message } as T;
            }
            finally
            {
                last = null;
                wc.Dispose();
            }
            if (json.Contains("0x0006") && api != "check")
            {
                var ck = Check();
                if (last != null && ck) return doapi<T>(last.name, last.ps as Dictionary<string, string>);
            }
            return Serialize.FromJson<T>(json);
        }

        public static bool Init(string k)
        {
            key = k;
            return Check();
        }
        public static bool Check()
        {
            var rsp = doapi<Resp>("check", new Dictionary<string, string>() { { "akey", key } });
            if (rsp.issucc) uk = rsp.msg;
            return rsp.issucc;
        }
        public static void WxLogin(string uin, string nk, string hd)
        {
            doapi<Resp>("wx.login", new Dictionary<string, string>() { { "headimg", hd }, { "uin", uin }, { "nickname", nk } });
        }
        public static MsgResp LoadMsg(string uin)
        {
            return doapi<MsgResp>("msg.load", new Dictionary<string, string>() { { "uin", uin } });
        }
        public static ReplyResp LoadReply(string uin)
        {
            return doapi<ReplyResp>("reply.load", new Dictionary<string, string>() { { "uin", uin } });
        }
        public static Resp ContactSync(string data, string uin)
        {
            return doapi<Resp>("contact.sync", new Dictionary<string, string>() { { "data", data }, { "uin", uin } });
        }

    }
    public class Resp
    {
        public bool issucc { get; set; }
        public string msg { get; set; }
    }
    public class MsgResp : Resp
    {
        public class Msg
        {
            public int type { get; set; }
            public List<string> touser { get; set; }
            public string content { get; set; }
        }
        public List<Msg> items { get; set; }
    }
    public class ReplyResp : Resp
    {
        public class Reply
        {
            public int tp { get; set; }
            public int match { get; set; }
            public int type { get; set; }
            public string[] keys { get; set; }
            public string content { get; set; }
            public string[] users { get; set; }
        }
        public List<Reply> items { get; set; }
    }
}
