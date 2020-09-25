using System;
using System.Collections.Generic;
using System.IO;
using mbdbdump;
using System.Text.RegularExpressions;
using System.Linq;
using System.Data.SQLite;
using System.Text;

using System.Diagnostics;
using System.Runtime.Serialization.Plists;
using System.Reflection;
using iphonebackupbrowser;
using System.Xml;
using System.Runtime.Serialization.Json;
using System.Runtime.Serialization;

namespace WechatExport
{
    class WeChatInterface
    {
        public class DisplayItem
        {
            public string pic;
            public string text;
            public string link;
            public long lastMessageTime;
        }

        public interface ILogger
        {
            void AddLog(String log);
            void Debug(string log);
        }

        [DataContract]
        public class Message64
        {
            [DataMember(Name = "msgContent")]
            public string msgContent { get; set; }
        }

        public Dictionary<string, string> fileDict = null;
        private string currentBackup;
        private List<MBFileRecord> files92;
        private Dictionary<string, string> templates;
        private ILogger logger;

        public static string AssemblyDirectory
        {
            get
            {
                string codeBase = Assembly.GetExecutingAssembly().CodeBase;
                UriBuilder uri = new UriBuilder(codeBase);
                string path = Uri.UnescapeDataString(uri.Path);
                return Path.GetDirectoryName(path);
            }
        }

        public static bool Export(string backupPath, string saveBase, string indexPath, bool outputHtml, List<MBFileRecord> files92, ILogger logger)
        {
            Directory.CreateDirectory(saveBase);
            logger.AddLog("分析文件夹结构");
            WeChatInterface wechat = new WeChatInterface(backupPath, files92, logger);
            wechat.BuildFilesDictionary();
            logger.AddLog("查找UID");
            var UIDs = wechat.FindUIDs();
            logger.AddLog("找到" + UIDs.Count + "个账号的消息记录");
            var uidList = new List<WeChatInterface.DisplayItem>();
            foreach (var uid in UIDs)
            {
#if DEBUG
                if (!uid.Equals("ed93c38987566a06ce6430aa8bb5a1ef"))
                {
                    // continue;
                }
#endif
                var userBase = Path.Combine("Documents", uid);
                logger.AddLog("开始处理UID: " + uid);
                logger.AddLog("读取账号信息");
                if (wechat.GetUserBasics(uid, userBase, out Friend myself))
                {
                    logger.AddLog("微信号：" + myself.ID() + " 昵称：" + myself.DisplayName());
                }
                else
                {
                    // logger.AddLog("没有找到本人信息，用默认值替代，可以手动替换正确的头像文件：" + Path.Combine("res", "DefaultProfileHead@2x-Me.png").ToString());
                }
                var userSaveBase = Path.Combine(saveBase, myself.ID());
                Directory.CreateDirectory(userSaveBase);
                logger.AddLog("正在打开数据库");
                var emojidown = new HashSet<DownloadTask>();
                var chatList = new List<WeChatInterface.DisplayItem>();
                Dictionary<string, Friend> friends = null;
                int friendcount = 0;

                List<string> dbs = wechat.GetMMSqlites(userBase);
                foreach (string db in dbs)
                {
                    if (!wechat.OpenMMSqlite(userBase, db, out SQLiteConnection conn))
                    {
                        logger.AddLog("打开MM.sqlite失败，跳过");
                        continue;
                    }

                    if (db.Equals("MM.sqlite"))
                    {
                        if (wechat.OpenWCDBContact(userBase, out SQLiteConnection wcdb))
                            logger.AddLog("存在WCDB，与旧版好友列表合并使用");
                        logger.AddLog("读取好友列表");
                        if (!wechat.GetFriendsDict(conn, wcdb, myself, out friends, out friendcount))
                        {
                            logger.AddLog("读取好友列表失败，跳过");
                            continue;
                        }
                        logger.AddLog("找到" + friendcount + "个好友/聊天室");
                    }
                    
                    logger.AddLog("查找对话");
                    wechat.GetChatSessions(conn, out List<string> chats);
                    logger.AddLog("找到" + chats.Count + "个对话");
                    
                    foreach (var chat in chats)
                    {
                        var hash = chat;
                        string displayname = chat, id = displayname;
                        Friend friend = null;
                        if (friends.ContainsKey(hash))
                        {
                            friend = friends[hash];
                            displayname = friend.DisplayName();
                            logger.AddLog("处理与" + displayname + "的对话");
                            id = friend.ID();
                        }
                        else logger.AddLog("未找到好友信息，用默认名字代替");
#if DEBUG
                        if (!"23069688360@chatroom".Equals(id))
                        {
                            continue;
                        }
#endif
                        if (outputHtml)
                        {
                            long lastMsgTime = 0;
                            if (wechat.SaveHtmlRecord(conn, userBase, userSaveBase, displayname, id, myself, chat, friend, friends, out int count, out HashSet<DownloadTask> _emojidown, out lastMsgTime))
                            {
                                logger.AddLog("成功处理" + count + "条");
                                chatList.Add(new WeChatInterface.DisplayItem() { pic = "Portrait/" + (friend != null ? friend.FindPortrait() : "DefaultProfileHead@2x.png"), text = displayname, link = id + ".html", lastMessageTime = lastMsgTime });
                            }
                            else logger.AddLog("失败");
                            emojidown.UnionWith(_emojidown);

                        }
                        else
                        {
                            if (wechat.SaveTextRecord(conn, Path.Combine(userSaveBase, id + ".txt"), displayname, id, myself, chat, friend, friends, out int count)) logger.AddLog("成功处理" + count + "条");
                            else logger.AddLog("失败");
                        }
                    }
                    conn.Close();
                }

                if (outputHtml)
                {
                    // 最后一条消息的时间倒叙
                    chatList.Sort((x, y) => { return y.lastMessageTime.CompareTo(x.lastMessageTime); });
                    wechat.MakeListHTML(chatList, Path.Combine(userSaveBase, "聊天记录.html"));
                }
                var portraitdir = Path.Combine(userSaveBase, "Portrait");
                Directory.CreateDirectory(portraitdir);
                var downlist = new HashSet<DownloadTask>();
                foreach (var item in friends)
                {
                    var tfriend = item.Value;
                    // Console.WriteLine(tfriend.ID());
#if DEBUG
                    if (!"25926707592@chatroom".Equals(tfriend.ID()))
                    {
                        // continue;
                    }
#endif
                    if (!tfriend.PortraitRequired) continue;
                    if (tfriend.Portrait != null && tfriend.Portrait != "") downlist.Add(new DownloadTask() { url = tfriend.Portrait, filename = tfriend.ID() + ".jpg" });
                    //if (tfriend.PortraitHD != null && tfriend.PortraitHD != "") downlist.Add(new DownloadTask() { url = tfriend.PortraitHD, filename = tfriend.ID() + "_hd.jpg" });
                }
                var downloader = new Downloader(6);
                if (downlist.Count > 0)
                {
                    logger.AddLog("下载" + downlist.Count + "个头像");
                    foreach (var item in downlist)
                    {
                        downloader.AddTask(item.url, Path.Combine(portraitdir, item.filename));
                    }
                    try
                    {
                        File.Copy(Path.Combine("res", "DefaultProfileHead@2x.png"), Path.Combine(portraitdir, "DefaultProfileHead@2x.png"), true);
                    }
                    catch (Exception) { }
                }
                var emojidir = Path.Combine(userSaveBase, "Emoji");
                Directory.CreateDirectory(emojidir);
                if (emojidown != null && emojidown.Count > 0)
                {
                    logger.AddLog("下载" + emojidown.Count + "个表情");
                    foreach (var item in emojidown)
                    {
                        downloader.AddTask(item.url, Path.Combine(emojidir, item.filename));
                    }
                }
                string displayName = myself.DisplayName();
                if (displayName == "我" && myself.alias != null && myself.alias.Length != 0)
                {
                    displayName = myself.alias;
                }
                uidList.Add(new WeChatInterface.DisplayItem() { pic = myself.ID() + "/Portrait/" + myself.FindPortrait(), text = displayName, link = myself.ID() + "/聊天记录.html" });
                downloader.StartDownload();
                System.Threading.Thread.Sleep(16);
                downloader.WaitToEnd();
                logger.AddLog("完成当前账号");
            }
            if (outputHtml) wechat.MakeListHTML(uidList, indexPath);
            logger.AddLog("任务结束");
            
            wechat = null;
            return true;
        }

        public static IPhoneBackup LoadManifest(string path)
        {
            IPhoneBackup backup = null;
            string filename = Path.Combine(path, "Info.plist");
            try
            {
                xdict dd = xdict.open(filename);
                if (dd != null)
                {
                    backup = new IPhoneBackup
                    {
                        path = path
                    };
                    foreach (xdictpair p in dd)
                    {
                        if (p.item.GetType() == typeof(string))
                        {
                            switch (p.key)
                            {
                                case "Device Name": backup.DeviceName = (string)p.item; break;
                                case "Display Name": backup.DisplayName = (string)p.item; break;
                                case "Last Backup Date":
                                    DateTime.TryParse((string)p.item, out backup.LastBackupDate);
                                    break;
                            }
                        }
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // MessageBox.Show(ex.InnerException.ToString());
                backup = null;
            }
            catch (Exception)
            {
                // MessageBox.Show(ex.ToString());
                backup = null;
            }
            return backup;
        }

        public WeChatInterface(string currentBackup, List<MBFileRecord> files92, ILogger logger)
        {
            this.currentBackup = currentBackup;
            this.files92 = files92;
            this.logger = logger;
            this.templates = new Dictionary<string, string>();

            loadTemplates();
        }

        private string getTemplate(string name)
        {
            return this.templates.ContainsKey(name) ? this.templates[name] : @"";
        }

        private void loadTemplates()
        {
            this.templates.Add("frame", loadTemplate("frame.html"));
            this.templates.Add("msg", loadTemplate("msg.html"));
            this.templates.Add("video", loadTemplate("video.html"));
            this.templates.Add("notice", loadTemplate("notice.html"));
            this.templates.Add("system", loadTemplate("system.html"));
            this.templates.Add("audio", loadTemplate("audio.html"));
            this.templates.Add("image", loadTemplate("image.html"));
            this.templates.Add("card", loadTemplate("card.html"));
            this.templates.Add("emoji", loadTemplate("emoji.html"));
            this.templates.Add("share", loadTemplate("share.html"));
            this.templates.Add("thumb", loadTemplate("thumb.html"));

            this.templates.Add("listframe", loadTemplate("listframe.html"));
            this.templates.Add("listitem", loadTemplate("listitem.html"));
        }

        private string loadTemplate(string name)
        {
            // Determine path
            var assembly = Assembly.GetExecutingAssembly();
            string codeBase = assembly.CodeBase;
            UriBuilder uri = new UriBuilder(codeBase);
            string path = Uri.UnescapeDataString(uri.Path);
            path = Path.GetDirectoryName(path);

            string resourcePath = Path.Combine(path, "res");
            resourcePath = Path.Combine(resourcePath, "templates");
            resourcePath = Path.Combine(resourcePath, name);

            if (File.Exists(resourcePath))
            {
                return System.IO.File.ReadAllText(resourcePath);
            }
            return "";
        }

        public List<string> GetMMSqlites(string userBase)
        {
            List<string> dbs = new List<string>();
            // if (File.Exists(MyPath.Combine(userBase, "DB", "MM.sqlite")))
            {
                dbs.Add("MM.sqlite");
            }

            string sourceDirectory = Path.Combine(userBase, "DB");

            var msgDbs = FindMessageDatabases(sourceDirectory);
            dbs.AddRange(msgDbs);

            return dbs;
        }

        public bool OpenMMSqlite(string userBase, string msgName, out SQLiteConnection conn)
        {
            bool succ = false;
            conn = null;
            try
            {
                conn = new SQLiteConnection
                {
                    ConnectionString = "data source=" + GetBackupFilePath(MyPath.Combine(userBase, "DB", msgName)) + ";version=3"
                };
                conn.Open();
                succ = true;
            }
            catch (Exception)
            {
                
            }
            return succ;
        }

        public bool OpenWCDBContact(string userBase, out SQLiteConnection conn)
        {
            bool succ = false;
            conn = null;
            try
            {
                conn = new SQLiteConnection
                {
                    ConnectionString = "data source=" + GetBackupFilePath(MyPath.Combine(userBase, "DB", "WCDB_Contact.sqlite")) + ";version=3"
                };
                conn.Open();
                succ = true;
            }
            catch (Exception ex)
            {
                logger.Debug(ex.ToString());
            }
            return succ;
        }

        public string GetStringFromMMSetting(byte[] data, byte[] key)
        {
            string value = null;
            
            int[] positions = ByteArrayLocater.Locate(data, key);

            if (positions != ByteArrayLocater.Empty)
            {
                foreach (int pos in positions)
                {
                    int length1 = data[pos + key.Length];
                    int length2 = data[pos + key.Length + 2];

                    if (length1 == length2 + 2)
                    {
                        value = Encoding.UTF8.GetString(data, pos + key.Length + 4, length2);
                        break;
                    }
                }
            }

            return value;
        }

        public string GetStringFromMMSetting2(byte[] data, byte[] key)
        {
            string value = null;

            int[] positions = ByteArrayLocater.Locate(data, key);

            if (positions != ByteArrayLocater.Empty)
            {
                foreach (int pos in positions)
                {
                    int length1 = data[pos + key.Length];
                    int length2 = data[pos + key.Length + 1];

                    if (length1 == (length2 + 1))
                    {
                        value = Encoding.UTF8.GetString(data, pos + key.Length + 2, length2);
                        break;
                    }
                }
            }

            return value;
        }

        public bool GetUserBasics(string uid, string userBase, out Friend friend)
        {
            friend = new Friend() { UsrName = uid, NickName = "我", alias = null, PortraitRequired=true };
            bool succ = false;
            try
            {
                var pr = new BinaryPlistReader();
                var mmsetting = GetBackupFilePath(Path.Combine(userBase, "mmsetting.archive"));
                if (File.Exists(mmsetting))
                {
                    using (var sw = new FileStream(mmsetting, FileMode.Open))
                    {
                        var dd = pr.ReadObject(sw);
                        var objs = dd["$objects"] as object[];
                        var dict = GetCFUID(objs[1] as Dictionary<object, object>);
                        if (dict.ContainsKey("UsrName") && dict.ContainsKey("NickName"))
                        {
                            friend.UsrName = objs[dict["UsrName"]] as string;
                            friend.NickName = objs[dict["NickName"]] as string;
                            succ = true;
                        }
                        if (dict.ContainsKey("AliasName"))
                        {
                            friend.alias = objs[dict["AliasName"]] as string;
                        }
                        for (int i = 0; i < objs.Length; i++)
                        {
                            if (objs[i].GetType() != typeof(string)) continue;
                            string obj = (objs[i] as string);
                            
                            if (obj.StartsWith("http://wx.qlogo.cn/mmhead/") || obj.StartsWith("https://wx.qlogo.cn/mmhead/"))
                            {
                                if (obj.EndsWith("/0")) friend.PortraitHD = obj;
                                else if (obj.EndsWith("/132")) friend.Portrait = obj;
                            }
                        }
                    }
                }
                else
                {
                    // Find it from MMappedKV
                    mmsetting = FindMMSettingFromMMappedKV(uid);
                    if (mmsetting != null && (mmsetting = GetBackupFilePath(mmsetting)) != null)
                    {
                        byte[] data = null;
                        try
                        {
                            data = File.ReadAllBytes(mmsetting);
                        }
                        catch (Exception) { }

                        if (data != null)
                        {
                            byte[] nameKey = { 56, 56 };
                            friend.NickName = GetStringFromMMSetting2(data, nameKey);
                            friend.alias = friend.NickName;

                            byte[] headImgUrl = Encoding.UTF8.GetBytes("headimgurl");
                            friend.Portrait = GetStringFromMMSetting(data, headImgUrl);

                            byte[] headHDImgUrl = Encoding.UTF8.GetBytes("headhdimgurl");
                            friend.PortraitHD = GetStringFromMMSetting(data, headHDImgUrl);

                            succ = true;
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                logger.Debug(ex.ToString());
            }

            return succ;
        }

        Dictionary<string,int> GetCFUID(Dictionary<object, object> dict)
        {
            var ret = new Dictionary<string, int>();
            foreach (var pair in dict)
            {
                if (pair.Value.GetType() != typeof(Dictionary<string, ulong>)) continue;
                var content = pair.Value as Dictionary<string, ulong>;
                foreach (var pair2 in content)
                {
                    if (pair2.Key != "CF$UID") continue;
                    ret.Add((string)pair.Key, (int)pair2.Value);
                }
            }
            return ret;
        }

        public bool GetFriends(SQLiteConnection conn, Friend myself, out List<Friend> friends)
        {
            bool succ = false;
            friends = new List<Friend>
            {
                myself
            };
            try
            {
                using (var cmd = new SQLiteCommand(conn))
                {
                    cmd.CommandText = "SELECT Friend.UsrName,NickName,ConRemark,ConChatRoomMem,ConStrRes2 FROM Friend JOIN Friend_Ext ON Friend.UsrName=Friend_Ext.UsrName";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                            try
                            {
                                var friend = new Friend
                                {
                                    UsrName = reader.GetString(0),
                                    NickName = reader.GetString(1),
                                    ConRemark = reader.GetString(2),
                                    ConChatRoomMem = reader.GetString(3),
                                    ConStrRes2 = reader.GetString(4)
                                };
                                friend.ProcessConStrRes2();
                                friends.Add(friend);
                            }
                            catch (Exception ex)
                            {
                                logger.Debug(ex.ToString());
                            }
                    }
                }
                succ = true;
            }
            catch (Exception ex)
            {
                logger.Debug(ex.ToString());
            }
            return succ;
        }

        public bool GetWCDBFriends(SQLiteConnection wcdb, out List<Friend> friends)
        {
            friends = new List<Friend>();
            bool succ = false;
            try
            {
                using(var cmd=new SQLiteCommand(wcdb))
                {
                    var buf = new byte[10000];
                    cmd.CommandText = "SELECT userName,dbContactRemark,dbContactChatRoom,dbContactHeadImage FROM Friend";
                    using (var reader = cmd.ExecuteReader())
                        while(reader.Read())
                            try
                            {
                                var friend = new Friend();
                                var username = reader.GetString(0);
                                var len = reader.GetBytes(1, 0, buf, 0, buf.Length);
                                var data = ReadBlob(buf, 0, (int)len);
                                friend.UsrName = username;

#if DEBUG
                                if ("25926707592@chatroom".Equals(username))
                                {
                                    // continue;
                                    // Console.WriteLine("");
                                }
#endif

                                if (data.ContainsKey(0x0a)) friend.NickName = data[0x0a];
                                if (data.ContainsKey(0x12)) friend.alias = data[0x12];
                                if (data.ContainsKey(0x1a)) friend.ConRemark = data[0x1a];
                                if(username.EndsWith("@chatroom"))
                                {
                                    friend.IsChatroom = true;
                                    friend.Members = new SortedDictionary<string, string>();
                                    try
                                    {
                                        //跳过第一个字符，是因为getstring按照utf-8读取，在和二进制混合的文件中，有可能前一个字符表示与它合并，导致读不出来
                                        //（现在还不完全确定这些BLOB当中字符串的存储结构）
                                        var match2 = Regex.Match(reader.GetString(2), @"RoomData>(.*?)<\/RoomData>", RegexOptions.Singleline);
                                        if (match2.Success) friend.dbContactChatRoom = match2.Groups[1].Value;
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.Debug(ex.ToString());
                                    }

                                    if ((friend.ConRemark == null || friend.ConRemark.Length == 0) && friend.dbContactChatRoom != null && friend.dbContactChatRoom.Length > 0)
                                    {
                                        XmlDocument xd = new XmlDocument();
                                        try
                                        {
                                            xd.LoadXml("<RoomData>" + friend.dbContactChatRoom + "</RoomData>");
                                            //查找固定名称 节点名要从根节点开始写
                                            XmlNodeList nodes = xd.DocumentElement.SelectNodes("/RoomData/Member");
                                            if (nodes != null)
                                            {
                                                foreach (XmlNode node in nodes)
                                                {
                                                    var nameAttr = node.Attributes.GetNamedItem("UserName");
                                                    if (nameAttr != null)
                                                    {
                                                        string memberName = nameAttr.Value;
                                                        XmlNode memberDisplayName = node.SelectSingleNode("/DisplayName");
                                                        if (memberDisplayName != null)
                                                        {
                                                            friend.Members.Add(memberName, memberDisplayName.Value != null ? memberDisplayName.Value : "");
                                                        }
                                                        else
                                                        {
                                                            friend.Members.Add(memberName, "");
                                                        }
                                                        // if (node.Attributes.GetNamedItem("UserName"))
                                                    }


                                                }
                                            }


                                        }
                                        catch (Exception ex)
                                        {
                                            logger.Debug(ex.ToString());
                                        }
                                            
                                    }
                                }
                                    
                                var str = reader.GetString(3);
                                var match = Regex.Match(str, @"(ttps?:\/\/wx.qlogo.cn\/(.+?)\/132)");
                                if (match.Success) friend.Portrait = "h" + match.Groups[1].Value;
                                match = Regex.Match(str, @"(ttps?:\/\/wx.qlogo.cn\/([\w\/_]+?)\/0)");
                                if (match.Success) friend.PortraitHD = "h" + match.Groups[1].Value;

                                if (friend.Portrait == null && friend.PortraitHD != null)
                                {
                                    friend.Portrait = friend.PortraitHD;
                                }

                                friends.Add(friend);
                            }
                            catch (Exception ex)
                            {
                                logger.Debug(ex.ToString());
                            }
                }
                succ = true;
            }
            catch (Exception ex)
            {
                logger.Debug(ex.ToString());
            }
            return succ;
        }

        public bool GetFriendsDict(SQLiteConnection conn, SQLiteConnection wcdb, Friend myself, out Dictionary<string,Friend> friends, out int count)
        {
            count = 0;
            friends = new Dictionary<string, Friend>();
            bool succ = GetFriends(conn, myself, out List<Friend> _friends);
            if (wcdb != null)
            {
                succ |= GetWCDBFriends(wcdb, out List<Friend> _friends2);
                _friends.AddRange(_friends2);
            }
            if (succ)
            {
                foreach (var friend in _friends)
                {
                    count++;
                    friends.AddSafe(friend.UsrName, friend);
                    friends.AddSafe(CreateMD5(friend.UsrName), friend);
                    if (friend.alias != null && friend.alias != "" && !friends.ContainsKeySafe(friend.alias))
                    {
                        friends.AddSafe(friend.alias, friend);
                        friends.AddSafe(CreateMD5(friend.alias), friend);
                    }
                }
            }
            return succ;
        }

        public bool GetChatSessions(SQLiteConnection conn, out List<string> sessions)
        {
            bool succ = false;
            sessions = new List<string>();
            try
            {
                using(var cmd=new SQLiteCommand(conn))
                {
                    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
                    using(var reader = cmd.ExecuteReader())
                    {
                        while(reader.Read())
                        {
                            try
                            {
                                var name = reader.GetString(0);
                                var match = Regex.Match(name, @"^Chat_([0-9a-f]{32})$");

                                if (match.Success) sessions.Add(match.Groups[1].Value);
                            }
                            catch (Exception ex)
                            {
                                logger.Debug(ex.ToString());
                            }
                        }
                            
                    }
                }
                succ = true;
            }
            catch (Exception ex)
            {
                logger.Debug(ex.ToString());
            }
            return succ;
        }

        public bool SaveTextRecord(SQLiteConnection conn, string path, string displayname, string id, Friend myself, string table, Friend friend, Dictionary<string, Friend> friends, out int count)
        {
            bool succ = false;
            count = 0;
            try
            {
                Dictionary<string, string> chatremark = null;
                if (id.EndsWith("@chatroom") && friend!=null && friend.dbContactChatRoom!=null)
                {
                    chatremark = ReadChatRoomRemark(friend.dbContactChatRoom);
                }
                using (var cmd = new SQLiteCommand(conn))
                {
                    cmd.CommandText = "SELECT CreateTime,Message,Des,Type FROM Chat_" + table;
                    using (var reader = cmd.ExecuteReader())
                    using (var sw = new StreamWriter(path))
                    {
                        while (reader.Read())
                            try
                            {
                                var unixtime = reader.GetInt32(0);
                                var message = reader.GetString(1);
                                var des = reader.GetInt32(2);
                                var type = reader.GetInt32(3);
                                var txtsender = (type == 10000 ? "[系统消息]" : (des == 1 ? displayname : myself.DisplayName()));
                                if (id.EndsWith("@chatroom") && type != 10000 && des == 1)
                                {
                                    var enter = message.IndexOf(":\n");
                                    if (enter > 0 && enter + 2 < message.Length)
                                    {
                                        txtsender = message.Substring(0, enter);
                                        message = message.Substring(enter + 2);
                                        if (chatremark.ContainsKey(txtsender)) txtsender = chatremark[txtsender];
                                        else if (friends.ContainsKey(txtsender)) txtsender = friends[txtsender].DisplayName();
                                    }
                                }
                                if(id.EndsWith("@chatroom") && des == 0)
                                {
                                    if (chatremark.ContainsKeySafe(myself.UsrName)) txtsender = chatremark[myself.UsrName];
                                    else if (chatremark.ContainsKeySafe(myself.alias)) txtsender = chatremark[myself.alias];
                                }
                                if (type == 34) message = "[语音]";
                                else if (type == 47) message = "[表情]";
                                else if (type == 62 || type == 43) message = "[小视频]";
                                else if (type == 50 || type == 64) message = "[视频/语音通话]";
                                else if (type == 3) message = "[图片]";
                                else if (type == 48) message = "[位置]";
                                else if (type == 49)
                                {
                                    if (message.Contains("<type>2001<")|| message.Contains("<type><![CDATA[2001]]><")) message = "[红包]";
                                    else if (message.Contains("<type>2000<") || message.Contains("<type><![CDATA[2000]]><")) message = "[转账]";
                                    else if (message.Contains("<type>17<") || message.Contains("<type><![CDATA[17]]><")) message = "[实时位置共享]";
                                    else if (message.Contains("<type>6<") || message.Contains("<type><![CDATA[6]]><")) message = "[文件]";
                                    else message = "[链接]";
                                }
                                else if (type == 42) message = "[名片]";

                                sw.WriteLine(txtsender + "(" + FromUnixTime(unixtime).ToLocalTime().ToString() + ")" + ": " + message);
                                count++;

                            }
                            catch (Exception)
                            {
                                
                            }
                    }
                }
                succ = true;
            }
            catch (Exception)
            {
                
            }
            return succ;
        }

        public bool SaveHtmlRecord(SQLiteConnection conn, string userBase, string path,string displayname,string id, Friend myself, string table, Friend friend, Dictionary<string, Friend> friends, out int count, out HashSet<DownloadTask> emojidown, out long lastMessageTime)
        {
            bool succ = false;
            lastMessageTime = 0;
            emojidown = new HashSet<DownloadTask>();
            count = 0;
            try
            {
                Dictionary<string, string> chatremark = new Dictionary<string, string>();
                if (id.EndsWith("@chatroom") && friend != null && friend.dbContactChatRoom != null)
                {
                    chatremark = ReadChatRoomRemark(friend.dbContactChatRoom);
                }
                using (var cmd = new SQLiteCommand(conn))
                {
                    cmd.CommandText = "SELECT CreateTime,Message,Des,Type,MesLocalID FROM Chat_" + table + " ORDER BY CreateTime";
                    using (var reader = cmd.ExecuteReader())
                    {
                        var assetsdir = Path.Combine(path, id + "_files");
                        Directory.CreateDirectory(assetsdir);
                        // var senderTemplate = @"<div class=""chat-receiver""><div><img src=""%%AVATAR%%"" width=""50"" height=""50""></div><div>%%TIME%% %%NAME%%</div><div><div class=""chat-right_triangle""></div><span>%%MESSAGE%%</span></div></div>";
                        // var receiverTemplate = @"<div class=""chat-sender""><div><img src=""%%AVATAR%%""/></div><div>%%NAME%% %%TIME%%</div><div><div class=""chat-left_triangle""></div><span>%%MESSAGE%%</span></div></div>";

                        StringBuilder sb = new StringBuilder(4096);
                        Dictionary<string, string> templateValues = new Dictionary<string, string>(8);
                        string templateKey = "msg";
                        // sw.WriteLine(@"<!doctype html>");
                        // sw.WriteLine(@"<html><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1,minimum-scale=1,maximum-scale=1,user-scalable=no,viewport-fit=cover""><meta name=""apple-mobile-web-app-capable"" content=""yes""><meta content=""yes"" name=""apple-touch-fullscreen""><link href=""../../styles/style.css"" rel=""stylesheet"" type=""text/css""><title>" + displayname + " - 微信聊天记录</title></head>");
                        // sw.WriteLine(@"<body>");
                        while (reader.Read())
                            try
                            {
                                var unixtime = reader.GetInt32(0);
                                var message = reader.GetString(1);
                                var des = reader.GetInt32(2);
                                var type = reader.GetInt32(3);
                                var msgid = reader.GetInt32(4);

                                if (lastMessageTime < unixtime)
                                {
                                    lastMessageTime = unixtime;
                                }

                                templateValues.Clear();
                                templateKey = "msg";

                                var ts = "";
                                if (type == 10000 || type == 10002)
                                {
                                    ts = getTemplate("system");
                                    ts = ts.Replace("%%MESSAGE%%", message);
                                    sb.Append(ts);
                                    // sw.WriteLine(@"<div class=""chat-notice""><span>系统消息: " + message + @"</span></div>");
                                    continue;
                                }
                                
                                if (id.EndsWith("@chatroom"))
                                {
                                    if (des == 0)
                                    {
                                        var txtsender = myself.DisplayName();
                                        if (chatremark.ContainsKeySafe(myself.UsrName)) txtsender = chatremark[myself.UsrName];
                                        else if (chatremark.ContainsKeySafe(myself.alias)) txtsender = chatremark[myself.alias];
                                        // ts += @"<tr><td width=""80"" align=""center""><img src=""Portrait/" + myself.FindPortrait() + @""" width=""50"" height=""50"" /><br />" + txtsender + @"</td>";
                                        templateValues["%%ALIGNMENT%%"] = "right";
                                        // templateValues.Add("%%NAME%%", txtsender);
                                        templateValues["%%NAME%%"] = "";
                                        templateValues["%%AVATAR%%"] = "Portrait/" + myself.FindPortrait();
                                    }
                                    else
                                    {
                                        templateValues["%%ALIGNMENT%%"] = "left";
                                        var enter = message.IndexOf(":\n");
                                        if (enter > 0 && enter + 2 < message.Length)
                                        {
                                            var txtsender = message.Substring(0, enter);
                                            var senderid = txtsender;
                                            message = message.Substring(enter + 2);
                                            if (chatremark.ContainsKeySafe(txtsender)) txtsender = chatremark[txtsender];
                                            else if (friends.ContainsKeySafe(txtsender)) txtsender = friends[txtsender].DisplayName();

                                            // if (friends.ContainsKeySafe(senderid)) ts += @"<tr><td width=""80"" align=""center""><img src=""Portrait/" + friends[senderid].FindPortrait() + @""" width=""50"" height=""50"" /><br />" + txtsender + @"</td>";
                                            // else ts += @"<tr><td width=""80"" align=""center""><img src=""Portrait/DefaultProfileHead@2x.png"" width=""50"" height=""50"" /><br />" + txtsender + @"</td>";
                                            templateValues["%%NAME%%"] = txtsender;
                                            templateValues["%%AVATAR%%"] = friends.ContainsKeySafe(senderid) ? ("Portrait/" + friends[senderid].FindPortrait()) : "Portrait/DefaultProfileHead@2x.png";
                                        }
                                        else
                                        {
                                            // ts = getTemplate("msg_friend");
                                            templateValues["%%NAME%%"] = "";
                                            templateValues["%%AVATAR%%"] = "";
                                        }
                                    }
                                }
                                else
                                {
                                    if (des == 0)
                                    {
                                        // ts += @"<tr><td width=""80"" align=""center""><img src=""Portrait/" + myself.FindPortrait() + @""" width=""50"" height=""50"" /><br />" + myself.DisplayName() + @"</td>";
                                        // ts = getTemplate("msg_me");
                                        // templateValues.Add("%%NAME%%", myself.DisplayName());
                                        templateValues["%%ALIGNMENT%%"] = "right";
                                        templateValues["%%NAME%%"] = "";
                                        templateValues["%%AVATAR%%"] = "Portrait/" + myself.FindPortrait();
                                    }
                                    else if (friend != null)
                                    {
                                        templateValues["%%ALIGNMENT%%"] = "left";
                                        templateValues["%%NAME%%"] = friend.DisplayName();
                                        templateValues["%%AVATAR%%"] = "Portrait/" + friend.FindPortrait();
                                    }
                                    else
                                    {
                                        // ts += @"<tr><td width=""80"" align=""center""><img src=""Portrait/DefaultProfileHead@2x.png"" width=""50"" height=""50"" /><br />" + displayname + @"</td>";
                                        // ts = getTemplate("msg_friend");
                                        templateValues["%%ALIGNMENT%%"] = "left";
                                        templateValues["%%NAME%%"] = displayname;
                                        templateValues["%%AVATAR%%"] = "Portrait/DefaultProfileHead@2x.png";
                                    }
                                }
                                if (type == 34)
                                {
                                    var voicelen = -1;
                                    var match = Regex.Match(message, @"voicelength=""(\d+?)""");
                                    if (match.Success) voicelen = int.Parse(match.Groups[1].Value);
                                    var audiosrc = GetBackupFilePath(MyPath.Combine(userBase, "Audio", table, msgid + ".aud"));
                                    if (audiosrc == null)
                                    {
                                        templateKey = "msg";
                                        // message = voicelen == -1 ? "[语音]" : "[语音 " + DisplayTime(voicelen) + "]";
                                        templateValues["%%MESSAGE%%"] = voicelen == -1 ? "[语音]" : "[语音 " + DisplayTime(voicelen) + "]";
                                    }
                                    else
                                    {
                                        if (Environment.OSVersion.Platform == PlatformID.Win32NT || Environment.OSVersion.Platform == PlatformID.Win32Windows)
                                        {
                                            ShellWait("lib\\silk_v3_decoder.exe", "\"" + audiosrc + "\" 1.pcm");
                                            ShellWait("lib\\lame.exe", "-r -s 24000 --preset voice 1.pcm \"" + Path.Combine(assetsdir, msgid + ".mp3") + "\"");
                                        }
                                        else if (Environment.OSVersion.Platform == PlatformID.MacOSX || Environment.OSVersion.Platform == PlatformID.Unix)
                                        {
                                            string audPath = Path.Combine("aud", "1.aud");
                                            File.Copy(audiosrc, audPath, true);
                                            string mp3Path = Path.Combine("mp3", "1.mp3");
                                            // string converterPath = Path.Combine("lib", "converter.sh");

                                            string converterPath = Path.Combine(AssemblyDirectory, "lib");
                                            converterPath = Path.Combine(converterPath, "converter.sh");

                                            // ShellWait("/bin/sh", converterPath + " " + Path.Combine(AssemblyDirectory, "1.aud") + " " + Path.Combine(AssemblyDirectory, "1.mp3") + "  mp3");
                                            ShellWait("/bin/sh", converterPath + " " + Path.Combine(AssemblyDirectory, "aud") + " " + Path.Combine(AssemblyDirectory, "mp3") + "  mp3");

                                            File.Copy(mp3Path, Path.Combine(assetsdir, msgid + ".mp3"), true);
                                            File.Delete(mp3Path);
                                            // ShellWait("lib\\lame.exe", "-r -s 24000 --preset voice 1.pcm \"" + Path.Combine(assetsdir, msgid + ".mp3") + "\"");
                                        }
                                        templateKey = "audio";
                                        // message = "<audio controls><source src=\"" + id + "_files/" + msgid + ".mp3\" type=\"audio/mpeg\"><a href=\"" + id + "_files/" + msgid + ".mp3\">播放</a></audio>";
                                        templateValues["%%AUDIOPATH%%"] = id + "_files/" + msgid + ".mp3";
                                    }
                                }
                                else if (type == 47)
                                {
                                    var match = Regex.Match(message, @"cdnurl ?= ?""(.+?)""");
                                    if (match.Success)
                                    {
                                        var localfile = RemoveCdata(match.Groups[1].Value);
                                        var match2 = Regex.Match(localfile, @"\/(\w+?)\/\w*$");
                                        if (!match2.Success) localfile = RandomString(10);
                                        else localfile = match2.Groups[1].Value;
                                        emojidown.Add(new DownloadTask() { url = match.Groups[1].Value, filename = localfile + ".gif" });
                                        // message = "<img src=\"Emoji/" + localfile + ".gif\" style=\"max-width:100px;max-height:60px\" />";
                                        templateKey = "emoji";
                                        // message = "[表情]";
                                        templateValues["%%EMOJIPATH%%"] = "Emoji/" + localfile + ".gif";
                                    }
                                    else
                                    {
                                        templateKey = "msg";
                                        // message = "[表情]";
                                        templateValues.Add("%%MESSAGE%%", "[表情]");
                                    }
                                }
                                else if (type == 62 || type == 43)
                                {
                                    var hasthum = RequireResource(MyPath.Combine(userBase, "Video", table, msgid + ".video_thum"), Path.Combine(assetsdir, msgid + "_thum.jpg"));
                                    var hasvid = RequireResource(MyPath.Combine(userBase, "Video", table, msgid + ".mp4"), Path.Combine(assetsdir, msgid + ".mp4"));

                                    if (hasthum && hasvid) message = "<video controls poster=\"" + id + "_files/" + msgid + "_thum.jpg\"><source src=\"" + id + "_files/" + msgid + ".mp4\" type=\"video/mp4\"><a href=\"" + id + "_files/" + msgid + ".mp4\">播放</a></video>";
                                    else if (hasthum) message = "<img src=\"" + id + "_files/" + msgid + "_thum.jpg\" /> （视频丢失）";
                                    else if (hasvid) message = "<video controls><source src=\"" + id + "_files/" + msgid + ".mp4\" type=\"video/mp4\"><a href=\"" + id + "_files/" + msgid + ".mp4\">播放</a></video>";
                                    else message = "[视频]";

                                    if (hasvid)
                                    {
                                        templateKey = "video";
                                        templateValues["%%THUMBPATH%%"] = hasthum ? (id + "_files/" + msgid + "_thum.jpg") : "";
                                        templateValues["%%VIDEOPATH%%"] = hasthum ? (id + "_files/" + msgid + ".mp4") : "";
                                    }
                                    else if (hasthum)
                                    {
                                        templateKey = "thumb";
                                        templateValues["%%IMGTHUMBPATH%%"] = hasthum ? (id + "_files/" + msgid + "_thum.jpg") : "";
                                        templateValues["%%MESSAGE%%"] = "（视频丢失）";
                                    }
                                    else
                                    {
                                        templateKey = "msg";
                                        templateValues["%%MESSAGE%%"] = "[视频]";
                                    }
                                    
                                }
                                else if (type == 50)
                                {
                                    templateKey = "msg";
                                    templateValues["%%MESSAGE%%"] = "[视频/语音通话]";
                                    // message = "[视频/语音通话]";
                                }
                                else if (type == 64)
                                {
                                    var serializer = new DataContractJsonSerializer(typeof(Message64));
                                    var stream = new MemoryStream(Encoding.UTF8.GetBytes(message));
                                    Message64 msg64 = (Message64)serializer.ReadObject(stream);

                                    templateKey = "notice";
                                    templateValues["%%MESSAGE%%"] = msg64.msgContent;
                                    // message = "[视频/语音通话]";
                                }
                                else if (type == 3)
                                {
                                    var hasthum = RequireResource(MyPath.Combine(userBase, "Img", table, msgid + ".pic_thum"), Path.Combine(assetsdir, msgid + "_thum.jpg"));
                                    var haspic = RequireResource(MyPath.Combine(userBase, "Img", table, msgid + ".pic"), Path.Combine(assetsdir, msgid + ".jpg"));
                                    // if (hasthum && haspic) message = "<a href=\"" + id + "_files/" + msgid + ".jpg\"><img src=\"" + id + "_files/" + msgid + "_thum.jpg\" class=\"image\" /></a>";
                                    // else if (hasthum) message = "<img src=\"" + id + "_files/" + msgid + "_thum.jpg\" class=\"image img_only_thumb\" />";
                                    // else if (haspic) message = "<img src=\"" + id + "_files/" + msgid + ".jpg\" class=\"image\" />";
                                    // else message = "[图片]";

                                    if (haspic)
                                    {
                                        templateKey = "image";
                                        templateValues["%%IMGPATH%%"] = id + "_files/" + msgid + ".jpg";
                                        templateValues["%%IMGTHUMBPATH%%"] = hasthum ? (id + "_files/" + msgid + "_thum.jpg") : (id + "_files/" + msgid + ".jpg");
                                    }
                                    else if (hasthum)
                                    {
                                        templateKey = "thumb";
                                        templateValues["%%IMGTHUMBPATH%%"] = hasthum ? (id + "_files/" + msgid + "_thum.jpg") : "";
                                        templateValues["%%MESSAGE%%"] = "";
                                    }
                                    else
                                    {
                                        templateKey = "msg";
                                        templateValues["%%MESSAGE%%"] = "[图片]";
                                    }

                                }
                                else if (type == 48)
                                {
                                    var match1 = Regex.Match(message, @"x ?= ?""(.+?)""");
                                    var match2 = Regex.Match(message, @"y ?= ?""(.+?)""");
                                    var match3 = Regex.Match(message, @"label ?= ?""(.+?)""");
                                    if (match1.Success && match2.Success && match3.Success) message = "[位置 (" + RemoveCdata(match2.Groups[1].Value) + "," + RemoveCdata(match1.Groups[1].Value) + ") " + RemoveCdata(match3.Groups[1].Value) + "]";
                                    else message = "[位置]";

                                    templateKey = "msg";
                                    templateValues.Add("%%MESSAGE%%", message);
                                }
                                else if (type == 49)
                                {
                                    if (message.Contains("<type>2001<")) templateValues.Add("%%MESSAGE%%", "[红包]");
                                    else if (message.Contains("<type>2000<")) templateValues.Add("%%MESSAGE%%", "[转账]");
                                    else if (message.Contains("<type>17<")) templateValues.Add("%%MESSAGE%%", "[实时位置共享]");
                                    else if (message.Contains("<type>6<")) templateValues.Add("%%MESSAGE%%", "[文件]");
                                    else
                                    {
                                        var match1 = Regex.Match(message, @"<title>(.+?)<\/title>");
                                        var match2 = Regex.Match(message, @"<des>(.*?)<\/des>");
                                        var match3 = Regex.Match(message, @"<url>(.+?)<\/url>");
                                        var match4 = Regex.Match(message, @"<thumburl>(.+?)<\/thumburl>");
                                        if (match1.Success && match3.Success)
                                        {
                                            templateKey = "share";

                                            //  < img src = "%%LINKIMGPATH%%" style = "float:left;max-width:100px;max-height:60px" />
                                            //  < a href = "LINKURL" >< b >%% LINKTITLE %%</ b ></ a >

                                            templateValues["%%SHARINGIMGPATH%%"] = "";
                                            templateValues["%%SHARINGURL%%"] = RemoveCdata(match3.Groups[1].Value);
                                            templateValues["%%SHARINGTITLE%%"] = RemoveCdata(match1.Groups[1].Value);
                                            templateValues["%%MESSAGE%%"] = "";

                                            if (match4.Success)
                                            {
                                                templateValues["%%SHARINGIMGPATH%%"] = RemoveCdata(match4.Groups[1].Value);
                                                // message += "<img src=\"" + RemoveCdata(match4.Groups[1].Value) + "\" style=\"float:left;max-width:100px;max-height:60px\" />";
                                            }
                                            // message += "<a href=\"" + RemoveCdata(match3.Groups[1].Value) + "\"><b>" + RemoveCdata(match1.Groups[1].Value) + "</b></a>";
                                            if (match2.Success)
                                            {
                                                // message += "<br />" + RemoveCdata(match2.Groups[1].Value);
                                                templateValues["%%MESSAGE%%"] = RemoveCdata(match2.Groups[1].Value);
                                            }
                                        }
                                        else
                                        {
                                            templateValues["%%MESSAGE%%"] = "[链接]";
                                        }
                                    }
                                }
                                else if (type == 42)
                                {
                                    var match1 = Regex.Match(message, "nickname ?= ?\"(.+?)\"");
                                    var match2 = Regex.Match(message, "smallheadimgurl ?= ?\"(.+?)\"");
                                    if (match1.Success)
                                    {
                                        message = "";
                                        if (match2.Success) message += "<img src=\"" + RemoveCdata(match2.Groups[1].Value) + "\" style=\"float:left;max-width:100px;max-height:60px\" />";
                                        message += "[名片] " + RemoveCdata(match1.Groups[1].Value);

                                        templateKey = "card";
                                        templateValues["%%CARDIMGPATH%%"] = (match2.Success) ? RemoveCdata(match2.Groups[1].Value) : "";
                                        templateValues["%%CARDNAME%%"] = RemoveCdata(match1.Groups[1].Value);
                                    }
                                    else
                                    {
                                        templateValues["%%MESSAGE%%"] = "[名片]";
                                    }
                                }
                                else
                                {
                                    templateValues["%%MESSAGE%%"] = SafeHTML(message);
                                }

                                templateValues.Add("%%TIME%%", FromUnixTime(unixtime).ToLocalTime().ToString().Replace(" ", "&nbsp;"));
                                ts = getTemplate(templateKey);
                                foreach (KeyValuePair<string, string> entry in templateValues)
                                {
                                    ts = ts.Replace(entry.Key, entry.Value);
                                }

                                // ts = ts.Replace(@"%%TIME%%", FromUnixTime(unixtime).ToLocalTime().ToString().Replace(" ", "&nbsp;"));
                                // ts = ts.Replace(@"%%MESSAGE%%", message);
                                // ts += @"<td width=""100"" align=""center"">" + FromUnixTime(unixtime).ToLocalTime().ToString().Replace(" ","<br />") + "</td>";
                                // ts += @"<td>" + message + @"</td></tr>";
                                // sw.WriteLine(ts);
                                sb.AppendLine(ts);
                                count++;
                            }
                            catch (Exception)
                            {
                            }


                        string html = this.getTemplate(@"frame");
                        html = html.Replace(@"%%DISPLAYNAME%%", displayname);
                        html = html.Replace(@"%%BODY%%", sb.ToString());
                        
                        using (var sw = new StreamWriter(Path.Combine(path, id + ".html")))
                        {
                            sw.Write(html);
                            sw.Close();
                        }
                    }
                }
                succ = true;
            }
            catch (Exception) {
            }
            return succ;
        }

        public void MakeListHTML(List<DisplayItem> list, string path)
        {
            string html = this.getTemplate(@"listframe");
            string itemTemplate = this.getTemplate(@"listitem");
            
            StringBuilder sb = new StringBuilder(4096);

            foreach (var item in list)
            {
                string itemHtml = itemTemplate.Replace("%%ITEMPICPATH%%", item.pic);
                itemHtml = itemHtml.Replace("%%ITEMLINK%%", item.link);
                itemHtml = itemHtml.Replace("%%ITEMTEXT%%", item.text);

                sb.AppendLine(itemHtml);
            }


            html = html.Replace(@"%%TBODY%%", sb.ToString());

            using (var sw = new StreamWriter(path))
            {
                sw.Write(html);
                sw.Close();
            }

        }

        public string GetBackupFilePath(string vpath)
        {
            vpath = vpath.Replace('\\', '/');
            if (!fileDict.ContainsKey(vpath)) return null;
            return Path.Combine(currentBackup, fileDict[vpath]);
        }

        public void BuildFilesDictionary()
        {
            var dict = new Dictionary<string, string>();
            foreach (var x in files92)
            {
                dict.Add(x.Path, x.key);
            }
            this.fileDict = dict;
        }

        public List<string> FindUIDs()
        {
            var UIDs = new HashSet<string>();
            foreach (var filename in fileDict)
            {
                var match = Regex.Match(filename.Key, @"Documents\/([0-9a-f]{32})\/");
                if (match.Success) UIDs.Add(match.Groups[1].Value);
            }
            var zeros = new string('0', 32);
            if (UIDs.Contains(zeros)) UIDs.Remove(zeros);
            return UIDs.ToList();
        }

        public List<string> FindMessageDatabases(string basePath)
        {
            string vpath = basePath.Replace('\\', '/');

            var dbs = new List<string>();
            foreach (var filename in fileDict)
            {
                if (filename.Key.StartsWith(vpath))
                {
                    var name = filename.Key.Substring(vpath.Length);
                    if (name.StartsWith("/"))
                    {
                        name = name.Substring(1);
                    }
                    var match = Regex.Match(name, @"^message_[0-9]{1,4}\.sqlite$");
                    if (match.Success) dbs.Add(name);
                }
                
            }
            return dbs;
        }

        public string FindMMSettingFromMMappedKV(string uid)
        {
            string mmsetting = null;
            const string MMSettingInMMappedKVHeader = "Documents/MMappedKV/mmsetting.archive.";
            const string MMSettingInMMappedKVTail = ".crc";
            foreach (var filename in fileDict)
            {
                if (filename.Key.StartsWith(MMSettingInMMappedKVHeader) && !filename.Key.EndsWith(MMSettingInMMappedKVTail))
                {
                    string uidInMmseting = filename.Key.Substring(MMSettingInMMappedKVHeader.Length);
                    if (uid.Equals(CreateMD5(uidInMmseting)))
                    {
                        mmsetting = filename.Key;
                        break;
                    }
                }

            }
            return mmsetting;
        }

        public bool RequireResource(string vpath,string dest)
        {
            vpath = vpath.Replace('\\', '/');
            if (fileDict.ContainsKey(vpath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                if(!File.Exists(dest)) File.Copy(GetBackupFilePath(vpath), dest);
                return true;
            }
            else return false;
        }

        public static string CreateMD5(string input)
        {
            // Use input string to calculate MD5 hash
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);

                // Convert the byte array to hexadecimal string
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    sb.Append(hashBytes[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }

        public static DateTime FromUnixTime(long unixTime)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddSeconds(unixTime);
        }

        public static Dictionary<byte,string> ReadBlob(byte[] blob, int offset, int len)
        {
            var ret = new Dictionary<byte, string>();
            var p = offset;
            var end = offset + len;
            while (p < end)
            {
                var abyte = blob[p++];
                if (p >= end) break;
                var asize = blob[p++];
                if (p + asize > end) break;
                var astring = Encoding.UTF8.GetString(blob, p, asize);
                ret.Add(abyte, astring);
                p += asize;
            }
            return ret;
        }

        public static Dictionary<string,string> ReadChatRoomRemark(string str)
        {
            var ret = new Dictionary<string, string>();
            var matches = Regex.Matches(str, @"<Member UserName=""(.+?)""(.+?)<\/Member>");
            foreach (Match match in matches)
            {
                var match2 = Regex.Match(match.Groups[2].Value, @"<DisplayName>(.+?)<\/DisplayName>");
                if (!match2.Success) continue;
                var username = match.Groups[1].Value;
                var displayname = match2.Groups[1].Value;
                ret.Add(username, displayname);
            }
            return ret;
        }

        public static string SafeHTML(string s)
        {
            s = s.Replace("&", "&amp;");
            s = s.Replace(" ", "&nbsp;");
            s = s.Replace("<", "&lt;");
            s = s.Replace(">", "&gt;");
            s = s.Replace("\r\n", "<br/>");
            s = s.Replace("\r", "<br/>");
            s = s.Replace("\n", "<br/>");
            return s;
        }

        public static string RemoveCdata(string str)
        {
            if (str.StartsWith("<![CDATA[") && str.EndsWith("]]>")) return str.Substring(9, str.Length - 12);
            return str;
        }

        public static string DisplayTime(int ms)
        {
            if (ms < 1000) return "1\"";
            return Math.Round((double)ms) + "\"";
        }

        public void ShellWait(string file,string args)
        {
            var p = new Process();
            p.StartInfo.FileName = file;
            p.StartInfo.Arguments = args;
            p.StartInfo.WindowStyle = (ProcessWindowStyle.Hidden);
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            p.OutputDataReceived += P_OutputDataReceived;
            p.ErrorDataReceived += P_OutputDataReceived;
            p.Start();
            p.BeginErrorReadLine();
            p.BeginOutputReadLine();
            p.WaitForExit();
        }

        private void P_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            // throw new NotImplementedException();
        }

        private static Random random = new Random();
        public static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
              .Select(s => s[random.Next(s.Length)]).ToArray());
        }
    }

    public class Friend
    {
        public string UsrName;
        public string NickName;
        public string ConRemark;
        public string ConChatRoomMem;
        public string dbContactChatRoom;
        public string ConStrRes2;
        public string Portrait;
        public string PortraitHD;
        public bool PortraitRequired;
        public string DefaultProfileHead = "DefaultProfileHead@2x.png";
        public bool IsChatroom = false;
        public SortedDictionary<string, string> Members = null;

        public string alias="";
        public void ProcessConStrRes2()
        {
            var match = Regex.Match(ConStrRes2, @"<alias>(.*?)<\/alias>");
            alias = match.Success ? match.Groups[1].Value : null;
            match = Regex.Match(ConStrRes2, @"<HeadImgUrl>(.+?)<\/HeadImgUrl>");
            if (match.Success) Portrait = match.Groups[1].Value;
            match = Regex.Match(ConStrRes2, @"<HeadImgHDUrl>(.+?)<\/HeadImgHDUrl>");
            if (match.Success) PortraitHD = match.Groups[1].Value;
        }
        public string DisplayName()
        {
            if (ConRemark != null && ConRemark != "") return ConRemark;
            if (NickName != null && NickName != "") return NickName;
            return ID();
        }
        public string ID()
        {
            if (alias != null && alias != "") return alias;
            if (UsrName != null && UsrName != "") return UsrName;
            return null;
        }
        public string FindPortrait()
        {
            PortraitRequired = true;
            if (Portrait != null && Portrait != "") return ID() + ".jpg";
            return DefaultProfileHead;
        }
        public string FindPortraitHD()
        {
            PortraitRequired = true;
            if (PortraitHD != null && PortraitHD != "") return ID() + "_hd.jpg";
            return FindPortrait();
        }
    }

    public static class DictionaryHelper
    {
        public static void AddSafe(this Dictionary<string, Friend> dict, string key, Friend value)
        {
            if (!dict.ContainsKey(key)) dict.Add(key, value);
        }
        public static bool ContainsKeySafe(this Dictionary<string, Friend> dict, string key)
        {
            if (key == null) return false;
            return dict.ContainsKey(key);
        }
        public static bool ContainsKeySafe(this Dictionary<string, string> dict, string key)
        {
            if (key == null) return false;
            return dict.ContainsKey(key);
        }
    }

    public class DownloadTask : IEquatable<DownloadTask>
    {
        public string url;
        public string filename;

        public bool Equals(DownloadTask other)
        {
            return url == other.url && filename == other.filename;
        }

        public override bool Equals(object other)
        {
            return other is DownloadTask && Equals((DownloadTask)other);
        }

        public override int GetHashCode()
        {
            return url.GetHashCode() * 53 + filename.GetHashCode();
        }
    }

}
