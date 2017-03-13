﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using System.Linq;
using X.Core.Utility;
using X.Lpw.App;
using X.Core.Plugin;
using System.IO;
using System.Text.RegularExpressions;

namespace X.Lpw
{
    class Program
    {
        static Wc wx = null;
        static string nickname;
        static string headimg;
        static string uin = "";
        static bool stop = false;
        static bool ctdone = false;
        static Login lg = null;
        static Main main = null;


        static Queue<Msg> msg_qu = null;

        [STAThreadAttribute]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Loger.Init();
            System.Net.ServicePointManager.DefaultConnectionLimit = 512;

            lg = new Login();

            Rbt.LoadConfig();

            msg_qu = new Queue<Msg>();

            Sdk.Init();
            Sdk.OutLog += Sdk_OutLog;

            //微信线程
            startWx();

            Application.Run(lg);

            if (string.IsNullOrEmpty(headimg)) return;

            main = new Main(wx, headimg);

            msgProcc();//消息队列处理

            //转发线程
            sendMsg();

            //报备回复
            replyMsg();

            Application.Run(main);
            stop = true;

            if (wx != null) wx.Quit();
        }

        private static void Sdk_OutLog(string log)
        {
            if (Rbt.user != null && Rbt.user.IsDebug)
            {
                try
                {
                    File.AppendAllText(Application.StartupPath + "\\log_" + DateTime.Now.ToString("yyyy-MM-dd") + ".txt", "log@" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:ffff") + "\r\n" + log + "\r\n\r\n");
                }
                catch { }
            }
            Debug.WriteLine(log);
        }

        /// <summary>
        /// 消息处理
        /// </summary>
        private static void msgProcc()
        {
            new Thread(o =>
            {
                while (!stop)
                {
                    if (msg_qu.Count() == 0) { Thread.Sleep(1500); continue; }
                    Msg m = null;
                    lock (msg_qu) m = msg_qu.Dequeue();
                    if (m == null) { Thread.Sleep(1500); continue; }
                    var r = wx.Send(m.username, 1, m.content);
                    if (r && m.status > 0) Sdk.SetStatus(m.msg_id, m.status);
                    Thread.Sleep(Tools.GetRandNext(100, 1000));
                }
            }).Start();
        }
        /// <summary>
        /// 报备回复
        /// </summary>
        private static void replyMsg()
        {
            new Thread(o =>
            {
                Thread.Sleep(15 * 1000);
                while (!stop)
                {
                    Thread.Sleep(10 * 1000);
                    if (Rbt.user == null) continue;

                    if (Rbt.user.Send.Count() == 0)
                    {
                        outLog("未配置转发规则，请先进行设置");
                        continue;
                    }

                    var st = DateTime.Now.ToString("HH:mm");
                    outLog("回复@" + st + "->开始获取");
                    var names = Rbt.user.Send.Select(s => s.BuildName).ToList();
                    var rsp = Sdk.LoadMsg(names, wx.user.Uin + "", 2);

                    if (stop) break;
                    if (rsp.Data == null || rsp.Data.Count() == 0) { outLog("回复@" + st + "->无内容"); continue; }

                    outLog("回复@" + st + "->开始发送，" + rsp.Data.Count() + "条");

                    foreach (var m in rsp.Data)
                    {
                        var ps = m.regist_user_id.Split('_');

                        var u = Rbt.user.Contacts.FirstOrDefault(c => c.NickName == ps[1]);
                        if (u == null) { outLog("回复@" + st + "->找不到发送人：" + ps[1]); }

                        //Wc.Contact ur = null;
                        //if (ps.Length == 3) ur = Rbt.cfg.Contacts.FirstOrDefault(c => c.NickName == ps[2]);

                        var txt = Rbt.user.Reply.Send_Succ
                            .Replace("[城市]", Rbt.cfg.CityName)
                            .Replace("[楼盘]", m.build_name)
                            .Replace("[发送人]", ps.Length == 3 ? ps[2] : ps[1])
                            .Replace("[经纪人]", m.agent_name)
                            .Replace("[经纪人电话]", m.agent_mobile)
                            .Replace("[客户姓名]", m.customer_name)
                            .Replace("[客户电话]", m.customer_mobile);

                        lock (msg_qu) msg_qu.Enqueue(new Msg()
                        {
                            msg_id = m.regist_id,
                            content = txt,
                            username = u.UserName,
                            status = 3
                        });

                        //if (wx.Send(u.UserName, 1, txt)) Sdk.SetStatus(m.regist_id, 3);
                        Thread.Sleep(5 * 1000);

                    }

                    outLog("回复@" + st + "->结束");
                }
            }).Start();
        }
        /// <summary>
        /// 群发消息
        /// </summary>
        private static void sendMsg()
        {
            new Thread(o =>
            {
                Thread.Sleep(20 * 1000);

                while (!stop)
                {
                    Thread.Sleep(10 * 1000);

                    if (Rbt.user == null) continue;

                    if (Rbt.user.Send.Count() == 0)
                    {
                        outLog("未配置转发规则，请先进行设置");
                        continue;
                    }

                    var st = DateTime.Now.ToString("HH:mm");
                    outLog("转发@" + st + "->开始获取");

                    var names = Rbt.user.Send.Select(s => s.BuildName).ToList();
                    var rsp = Sdk.LoadMsg(names, "", 1);

                    if (stop) break;
                    if (rsp.Data == null || rsp.Data.Count() == 0) { outLog("转发@" + st + "->无内容"); continue; }

                    outLog("转发@" + st + "->开始发送，" + rsp.Data.Count() + "条");

                    foreach (var m in rsp.Data)
                    {
                        var r = Rbt.user.Send.FirstOrDefault(rc => rc.BuildName == m.build_name);
                        if (r == null)
                        {
                            outLog("缺少楼盘：" + m.build_name + "的转发规则，请添加");
                            continue;
                        }

                        var ps = m.regist_user_id.Split('_');

                        var ct = Rbt.user.Contacts.FirstOrDefault(c => c.NickName == r.NickName);
                        if (ct == null)
                        {
                            outLog("楼盘：" + m.build_name + "的转发规则失效，找不到转发目标群，请更改");
                            continue;
                        }

                        var txt = @"" + m.build_name + "<br/>公司：" + m.company_name + "<br/>客户姓名：" + m.customer_name + "<br/>客户电话：" + m.customer_mobile + "<br/>经纪姓名：" + m.agent_name + "<br/>经纪电话：" + m.agent_mobile + "<br/>看房时间：" + m.look_date_str;

                        lock (msg_qu) msg_qu.Enqueue(new Msg()
                        {
                            msg_id = m.regist_id,
                            content = txt,
                            username = ct.UserName,
                            status = 2
                        });

                        Thread.Sleep(5 * 1000);
                    }

                    outLog("群发@" + st + "->结束");
                }
            }).Start();
        }
        /// <summary>
        /// 运行微信
        /// </summary>
        private static void startWx()
        {
            new Thread(o =>
            {
                wx = new Wc();
                wx.LoadQr += Wx_LoadQr; ;
                wx.Scaned += Wx_Scaned;
                wx.Loged += Wx_Loged;
                wx.LogonOut += Wx_LogonOut;
                wx.OutLog += Wx_OutLog; ;
                wx.NewMsg += Wx_NewMsg;
                wx.ContactLoaded += Wx_ContactLoaded;
                wx.Run();

            }).Start();
        }
        /// <summary>
        /// 输出日志
        /// </summary>
        /// <param name="msg"></param>
        private static void outLog(string msg)
        {
            ((Action)(() =>
            {
                if (main != null) main.OutLog(msg);
            })).BeginInvoke(null, null);
        }

        /// <summary>
        /// 微信通讯录加载事件
        /// </summary>
        /// <param name="cts"></param>
        /// <param name="gname"></param>
        /// <param name="isdone"></param>
        private static void Wx_ContactLoaded(Wc.Contact ct, bool isdone)
        {
            if (main == null || ct == null || ct.MemberList == null) return;
            main.SetContact(ct);

            if (string.IsNullOrEmpty(ct.UserName))
            {
                Rbt.user.Contacts = ct.MemberList;
                outLog("同步主通讯录");
            }
            else
            {
                var idx = Rbt.user.Contacts.FindIndex(o => o.UserName == ct.UserName);
                if (idx >= 0) Rbt.user.Contacts[idx] = ct;
                outLog("同步群：" + ct.NickName + " " + ct.MemberCount + "人");
            }

            if (isdone)
            {
                Rbt.SaveConfig();
                Rbt.SaveUser();

                if (!ctdone && !string.IsNullOrEmpty(Rbt.user.Reply.Rbt_Online))
                {
                    foreach (var n in Rbt.user.Collect.Ids)
                    {
                        var c = Rbt.user.Contacts.FirstOrDefault(o => o.NickName == n);
                        if (c == null) continue;
                        lock (msg_qu)
                        {
                            msg_qu.Enqueue(new Msg()
                            {
                                content = Rbt.user.Reply.Rbt_Online.Replace("[城市]", Rbt.cfg.CityName),
                                username = c.UserName
                            });
                        }
                    }
                }

                if (!ctdone) outLog("通讯录同步完成");
                else outLog("通讯录已经更新");

                ctdone = true;
            }
        }
        static void Wx_Loged(Wc.Contact u)
        {
            outLog(u.NickName + "已经登陆");
            Rbt.LoadUser(u.Uin + "");
            nickname = u.NickName;
            uin = u.Uin + "";
            lg.SetLoged();
            Thread.Sleep(2 * 1000);
            lg.Quit();
        }
        static void Wx_OutLog(string log)
        {
            if (Rbt.user != null && Rbt.user.IsDebug)
            {
                try
                {
                    File.AppendAllText(Application.StartupPath + "\\log_" + DateTime.Now.ToString("yyyy-MM-dd") + ".txt", "log@" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:ffff") + "\r\n" + log + "\r\n\r\n");
                }
                catch { }
            }
            Debug.WriteLine("log@" + DateTime.Now.ToString("HH:mm:ss.fff") + "->" + log);
        }
        static void Wx_LogonOut()
        {
            outLog("正在退出...");
            stop = true;
            Thread.Sleep(5 * 1000);
            Environment.Exit(0);
        }
        static void Wx_NewMsg(Wc.Msg m)
        {
            new Thread(p =>
            {
                if (Rbt.user.Contacts == null || m == null) return;
                var msg = p as Wc.Msg;
                var name = m.FromUserName;

                var u = Rbt.user.Contacts.FirstOrDefault(o => o.UserName == m.FromUserName);
                if (u == null && (msg.MsgType == 1 || msg.MsgType == 10000)) { outLog("收到无法识别的消息：" + m.Content); wx.LoadContact(null, false); return; }

                var cot = Tools.RemoveHtml(m.Content);
                Wc.Contact ur = null;

                if (m.FromUserName[1] == '@' && cot[0] == '@')
                {
                    var ct = cot.Substring(0, cot.IndexOf(":"));// cot.Split(':');
                    ur = u?.MemberList.FirstOrDefault(o => o.UserName == ct);
                    cot = cot.Replace(ct + ":", "");
                }

                if ((msg.MsgType == 10000 || ur == null) && (cot.Contains("加入了群聊") || cot.Contains("移出了群聊") || cot.Contains("修改群名为")))
                    wx.LoadContact(new List<object>(){
                        new {
                            EncryChatRoomId = u.EncryChatRoomId,
                            UserName = u.UserName
                        }
                    }, true);

                object mg = null;

                if (msg.MsgType != 1) return;//  main.OutLog(cot);

                //消息采集
                if (Rbt.user.Collect.Ids.Contains(u.NickName) && new Regex("(\r\n)").Matches(cot).Count >= 4)
                {
                    var c = false;
                    foreach (var k in Rbt.user.Collect.Keys.Split(' ')) if (cot.Contains(k)) { c = true; break; }
                    if (c)
                    {
                        var rsp = Sdk.Submit(cot, wx.user.Uin + "_" + Tools.RemoveHtml(u.NickName).Replace("_", "-") + (ur == null ? "" : "_" + Tools.RemoveHtml(ur.NickName).Replace("_", "-")));
                        if (rsp.RCode != "200")
                            lock (msg_qu)
                            {
                                if (!string.IsNullOrEmpty(Rbt.user.Reply.Identify_Fail))
                                {
                                    msg_qu.Enqueue(new Msg()
                                    {
                                        content = Rbt.user.Reply.Identify_Fail
                                            .Replace("[发送人]", ur == null ? Tools.RemoveHtml(u.NickName) : Tools.RemoveHtml(ur.NickName)).Replace("[错误信息]", rsp.RMessage),
                                        username = u.UserName
                                    });
                                }
                                if (Rbt.user.Reply.SendTpl_OnFail && !string.IsNullOrEmpty(Rbt.user.Reply.Msg_Tpl))
                                {
                                    msg_qu.Enqueue(new Msg()
                                    {
                                        content = Rbt.user.Reply.Msg_Tpl,
                                        username = u.UserName
                                    });
                                }
                            }
                        else
                        {
                            if (!string.IsNullOrEmpty(Rbt.user.Reply.Identify_Succ))
                            {
                                lock (msg_qu)
                                    msg_qu.Enqueue(new Msg()
                                    {
                                        content = Rbt.user.Reply.Identify_Succ
                                            .Replace("[城市]", Rbt.cfg.CityName)
                                            .Replace("[楼盘]", rsp.Data.build_name)
                                            .Replace("[发送人]", ur == null ? Tools.RemoveHtml(u.NickName) : Tools.RemoveHtml(ur.NickName))
                                            .Replace("[经纪人]", rsp.Data.agent_name)
                                            .Replace("[经纪人电话]", rsp.Data.agent_mobile)
                                            .Replace("[客户姓名]", rsp.Data.customer_name)
                                            .Replace("[客户电话]", rsp.Data.customer_mobile),
                                        username = u.UserName
                                    });
                            }
                        }
                    }
                }

                if (ur != null)
                {
                    var img = wx.GetHeadImage(new List<string>() { u.UserName, ur.UserName });
                    mg = new
                    {
                        body = cot,
                        u = new { name = ur.NickName, img = img != null && img.ContainsKey(ur.UserName) ? img[ur.UserName] : "", id = ur.UserName },
                        r = new { name = u.NickName, img = img != null && img.ContainsKey(u.UserName) ? img[u.UserName] : "", id = u.UserName }
                    };
                }

                if (mg == null)
                {
                    var img = wx.GetHeadImage(new List<string>() { u.UserName });
                    mg = new
                    {
                        body = cot,
                        u = new { name = u.NickName, img = img != null && img.ContainsKey(u.UserName) ? img[u.UserName] : "", id = u.UserName }
                    };
                }

                main.SetMsg(mg);

            }).Start(m);
        }
        static void Wx_Scaned(string hdimg)
        {
            headimg = hdimg;
            lg.SetHeadimg(hdimg);
        }
        static void Wx_LoadQr(string qrcode)
        {
            lg.SetQrcode(qrcode);
        }

        class Msg
        {
            public int msg_id { get; set; }
            public string username { get; set; }
            public string content { get; set; }
            public int status { get; set; }
        }

    }
}
