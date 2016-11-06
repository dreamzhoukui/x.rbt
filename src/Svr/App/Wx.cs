﻿using Rbt.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;

namespace Rbt.Svr.App
{
    public class Wx
    {
        #region 公开属性

        public string code { get; private set; }
        public int status { get { return lg == null ? -1 : lg.status.Value; } }

        public delegate void LogoutHandler(Wx wx);
        public event LogoutHandler Logout;

        #endregion

        #region 私有变量
        RbtDBDataContext db = null;
        x_logon lg = null;
        Wc wc = null;
        BaseRequest baseRequest = null;
        Contact user = null;//当前用户信息
        SyncKey _syncKey;
        static readonly DateTime BaseTime = new DateTime(1970, 1, 1);
        string uuid = "";
        string redirecturl = "";
        string passticket = "";
        string gateway = "";
        string synckey
        {
            get
            {
                string val = String.Empty;
                foreach (var item in _syncKey.List) val += String.Format("{0}_{1}|", item.Key, item.Val);
                return val.TrimEnd('|');
            }
        }
        bool isquit = false;
        #endregion

        #region 私有方法
        static long getcurrentseconds()
        {
            return (long)(DateTime.UtcNow - BaseTime).TotalMilliseconds;
        }

        void outLog(string msg)
        {
            Debug.WriteLine(lg.logon_id + "->" + msg);
        }

        bool loadQrcode()
        {
            try
            {

                outLog("loaduuid");

                var rsp = wc.GetStr("https://login.weixin.qq.com/jslogin?appid=wx782c26e4c19acffb&redirect_uri=https%3A%2F%2Fwx2.qq.com%2Fcgi-bin%2Fmmwebwx-bin%2Fwebwxnewloginpage&fun=new&lang=zh_CN&_=" + getcurrentseconds());
                if (rsp.err) return false;

                outLog("loaduuid->" + Com.ToJson(rsp));

                var reg = new Regex(@"QRLogin.uuid = ""(\S+?)""");
                var m = reg.Match(rsp.data + "");
                if (!m.Success) return false;

                uuid = m.Groups[1].Value;
                lg.status = 2;//已获取UUID
                lg.uuid = uuid;
                lg.openid = null;
                db.SubmitChanges();

                Thread.Sleep(500);

                outLog("qrcode");
                rsp = wc.GetFile(String.Format("https://login.weixin.qq.com/qrcode/{0}?t=webwx&_={1}", uuid, getcurrentseconds()));
                outLog("qrcode->" + Com.ToJson(rsp));

                if (rsp.err) return false;

                lg.qrcode = "data:img/jpg;base64," + Convert.ToBase64String(rsp.data as byte[]);
                lg.status = 3;//已获取二维码
                db.SubmitChanges();

                return true;

            }
            catch { return false; }
        }

        /// <summary>
        /// 等待中。。。
        /// 1、扫描
        /// 2、登陆
        /// </summary>
        /// <param name="t"></param>
        /// <returns>
        /// 0 超时
        /// 1 已登陆
        /// </returns>
        int waitFor(int t, int c)
        {
            if (c >= 10) return 0;

            outLog("wait->" + t + "->" + c);
            string url = String.Format("https://login.weixin.qq.com/cgi-bin/mmwebwx-bin/login?loginicon=true&uuid={0}&tip=0&r={1}&_={2}", uuid, ~getcurrentseconds(), getcurrentseconds());
            var rsp = wc.GetStr(url);
            outLog("wait->" + t + "->" + c + "->" + Com.ToJson(rsp));

            if (rsp.err) return waitFor(t, c + 1);

            var str = rsp.data + "";

            if (str.Contains("code=201"))
            {
                var img = str.Split('\'')[1].TrimEnd('\'');
                lg.headimg = img;
                lg.status = 4;//使用中
                db.SubmitChanges();
                return waitFor(2, 0);
            }

            if (str.Contains("code=200"))
            {
                var reg = new Regex(@"window.redirect_uri=""(\S+?)""");
                redirecturl = reg.Match(str).Groups[1].Value;
                if (!String.IsNullOrEmpty(redirecturl)) gateway = "https://" + new Uri(redirecturl).Host;
                lg.status = 5;//已登陆
                db.SubmitChanges();
                return 1;
            }

            return waitFor(t, c + 1);

        }

        void login()
        {
            outLog("login");
            var rsp = wc.GetStr(redirecturl + "&fun=new&version=v2");
            outLog("login->" + Com.ToJson(rsp));

            if (rsp.err) return;

            Regex reg = new Regex(@"<skey>(\S+?)</skey><wxsid>(\S+?)</wxsid><wxuin>(\d+)</wxuin><pass_ticket>(\S+?)</pass_ticket>");
            passticket = String.Empty;
            baseRequest = new BaseRequest();

            var m = reg.Match(rsp.data + "");
            if (!m.Success) return;

            baseRequest.Skey = m.Groups[1].Value;
            baseRequest.Sid = m.Groups[2].Value;
            passticket = HttpUtility.UrlDecode(m.Groups[4].Value, Encoding.UTF8);

            lg.uin = baseRequest.Uin = Convert.ToInt64(m.Groups[3].Value);
            db.SubmitChanges();

        }

        void wxInit()
        {
            outLog("init");

            string url = String.Format("{0}/cgi-bin/mmwebwx-bin/webwxinit?pass_ticket={1}&skey={2}&r={3}", gateway, passticket, baseRequest.Skey, getcurrentseconds());
            var rsp = wc.PostData(url, Com.ToJson(new { BaseRequest = baseRequest }));

            outLog("init->" + Com.ToJson(rsp));

            if (rsp.err) return;

            user = Com.FromJson<Contact>(rsp.data + "", "User");
            _syncKey = Com.FromJson<SyncKey>(rsp.data + "", "SyncKey");

        }

        void wxStatusNotify()
        {
            string url = String.Format("{0}/cgi-bin/mmwebwx-bin/webwxstatusnotify?lang=zh_CN&pass_ticket={1}", gateway, passticket);
            var o = new
            {
                BaseRequest = baseRequest,
                Code = 3,
                FromUserName = user.UserName,
                ToUserName = user.UserName,
                ClientMsgId = getcurrentseconds()
            };
            wc.PostData(url, Com.ToJson(o));
        }

        void loadContact()
        {
            string url = String.Format("{0}/cgi-bin/mmwebwx-bin/webwxgetcontact?pass_ticket={1}&skey={2}&r={3}", gateway, passticket, baseRequest.Skey, getcurrentseconds());
            var json = wc.GetStr(url);
            //userlist = Com.FromJson<List<Contact>>(json, "MemberList");
            //grouplist = userlist.Where(o => o.NickName.StartsWith("@@")).ToList();
        }

        void SyncCheck()
        {
            outLog("synccheck");

            var url = String.Format("{0}/cgi-bin/mmwebwx-bin/synccheck?r={1}&sid={2}&uin={3}&skey={4}&deviceid={5}&synckey={6}&_{7}", gateway, getcurrentseconds(), baseRequest.Sid, baseRequest.Uin, baseRequest.Skey, baseRequest.DeviceID, synckey, getcurrentseconds());
            var rsp = wc.GetStr(url);

            outLog("synccheck->" + Com.ToJson(rsp));

            if (rsp.err) { return; }

            var reg = new Regex("{retcode:\"(\\d+)\",selector:\"(\\d+)\"}");
            var m = reg.Match(rsp.data + "");
            if (!m.Success) { Exit(1); return; }

            var rt = int.Parse(m.Groups[1].Value);
            var sel = int.Parse(m.Groups[2].Value);

            if (isquit || rt != 0) Exit(1);
            else if (sel == 2) wxSync();

        }

        void wxSync()
        {
            outLog("sync");

            string url = String.Format("{0}/cgi-bin/mmwebwx-bin/webwxsync?sid={1}&skey={2}&pass_ticket={3}", gateway, baseRequest.Sid, baseRequest.Skey, passticket);
            var o = new
            {
                BaseRequest = baseRequest,
                SyncKey = _syncKey,
                rr = getcurrentseconds()
            };
            var rsp = wc.PostData(url, Com.ToJson(o));

            outLog("sync->" + Com.ToJson(rsp));

            if (rsp.err) { Exit(1); return; }

            _syncKey = Com.FromJson<SyncKey>(rsp.data + "", "SyncKey");

            var msglist = Com.FromJson<List<Msg>>(rsp.data + "", "AddMsgList");

            foreach (var m in msglist) if (m.FromUserName != user.UserName) outLog("msg->" + user.Uin + "->" + m.Content);// Debug.WriteLine(user.Uin + "收到消息->" + m.MsgId + "--->>>" + m.Content);

        }

        void Exit(int c)
        {
            outLog("exit");

            isquit = true;

            if (lg != null)
            {
                lg.uuid = null;
                lg.uin = null;
                lg.qrcode = null;
                lg.headimg = null;
                lg.status = 1;
            }

            try
            {
                db.SubmitChanges();
                db.Dispose();
            }
            catch { }

            if (c == 1) Logout?.Invoke(this);

        }

        #endregion

        public Wx(long id)
        {
            db = new RbtDBDataContext();
            lg = db.x_logon.FirstOrDefault(o => o.logon_id == id);
            code = lg.code;
            baseRequest = new BaseRequest();
            wc = new Wc();
            outLog("in");
        }

        #region 公开方法
        /// <summary>
        /// 开始运行
        /// 1、拉取二维码 loadQrcode
        /// 2、等待扫描和登陆 waitFor
        /// 3、初始化 wxInit
        /// 4、微信状态 wxStatusNotify
        /// 5、获取联系人和群聊 loadContact
        /// 6、循环发送心跳包 wxSync 间隔2秒
        /// </summary>
        public void Run()
        {
            outLog("run");

            isquit = false;

            while (!isquit)
            {
                var lr = loadQrcode();
                if (!lr) continue;

                Thread.Sleep(500);

                var wr = waitFor(1, 0);
                if (wr != 1) continue;

                if (string.IsNullOrEmpty(redirecturl)) continue;

                break;
            }

            login();
            wxInit();
            if (user == null) { Exit(1); return; }

            wxStatusNotify();
            //loadContact();

            lg.status = 6;//初始化完成
            db.SubmitChanges();

            while (!isquit) { SyncCheck(); Thread.Sleep(2 * 1000); }

        }

        /// <summary>
        /// 退出方法
        /// </summary>
        public void Quit()
        {
            Exit(0);
        }

        #endregion


        class BaseRequest
        {
            public BaseRequest()
            {
                DeviceID = "e" + getcurrentseconds();// "e84617712" + DateTime.Now.ToString("fffffff");
            }
            public long Uin { get; set; }
            public string Sid { get; set; }
            public string Skey { get; set; }
            public string DeviceID { get; set; }
        }
        class SyncKey
        {
            public class KeyValuePair
            {
                public int Key { get; set; }
                public int Val { get; set; }
            }
            public int Count { get; set; }
            public IList<KeyValuePair> List { get; set; }
        }

    }

    public class Contact
    {
        public long Uin { get; set; }
        public string UserName { get; set; }
        public string NickName { get; set; }
        /// <summary>
        /// 1-好友， 2-群组， 3-公众号
        /// </summary>
        public int ContactFlag { get; set; }
        public int MemberCount { get; set; }
        public List<Contact> MemberList { get; set; }
        public string Signature { get; set; }
        public string RemarkName { get; set; }
        public string HeadImgUrl { get; set; }
        public string EncryChatRoomId { get; set; }
    }

    public class Msg
    {
        public string MsgId { get; set; }
        public string FromUserName { get; set; }
        public string ToUserName { get; set; }
        public int MsgType { get; set; }
        public string Content { get; set; }
    }
}
