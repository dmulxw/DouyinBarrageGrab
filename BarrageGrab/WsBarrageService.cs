using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Fleck;
using Newtonsoft.Json;
using BarrageGrab.Modles;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.IO.Compression;
using System.IO;
using BarrageGrab.Modles.JsonEntity;
using BarrageGrab.Modles.ProtoEntity;
using System.Drawing;
using System.Collections.Concurrent;
using System.Threading; 
using System.Runtime.InteropServices; 
using System.Windows.Forms;

namespace BarrageGrab
{
    /// <summary>
    /// 弹幕服务长时间无操作，已暂停播放
    //累计节能13分钟
    //使用客户端免弹窗
    //继续播放
    /// </summary>
    public class WsBarrageService
    {
        WebSocketServer socketServer;
        Dictionary<string, UserState> socketList = new Dictionary<string, UserState>();
        //礼物计数缓存
        ConcurrentDictionary<string, Tuple<int, DateTime>> giftCountCache = new ConcurrentDictionary<string, Tuple<int, DateTime>>();
        System.Timers.Timer dieout = new System.Timers.Timer(10000);
        System.Timers.Timer giftCountTimer = new System.Timers.Timer(10000);
        WssBarrageGrab grab = new WssBarrageGrab();
        Appsetting Appsetting = Appsetting.Current;
        bool debug = false;
        private AutoReplyService autoReplyService;  
        private System.Timers.Timer keepAliveTimer;

        /// <summary>
        /// 自动回复服务
        /// </summary>
        public AutoReplyService AutoReplyService
        {
            get
            {
                if (autoReplyService == null)
                {
                    autoReplyService = new AutoReplyService();
                }
                return autoReplyService;
            }
        }

        /// <summary>
        /// 服务关闭后触发
        /// </summary>
        public event EventHandler OnClose;

        /// <summary>
        /// WS服务器启动地址
        /// </summary>
        public string ServerLocation { get { return socketServer.Location; } }

        /// <summary>
        /// 数据内核
        /// </summary>
        public WssBarrageGrab Grab { get { return grab; } }

        /// <summary>
        /// 控制台打印事件
        /// </summary>
        public event EventHandler<PrintEventArgs> OnPrint;

        public WsBarrageService()
        {
#if DEBUG
            debug = true;
#endif
            var socket = new WebSocketServer($"ws://0.0.0.0:{Appsetting.WsProt}");
            socket.RestartAfterListenError = true;//异常重启

            dieout.Elapsed += Dieout_Elapsed;
            giftCountTimer.Elapsed += GiftCountTimer_Elapsed;

            this.grab.OnChatMessage += Grab_OnChatMessage;
            this.grab.OnLikeMessage += Grab_OnLikeMessage;
            this.grab.OnMemberMessage += Grab_OnMemberMessage;
            this.grab.OnSocialMessage += Grab_OnSocialMessage;
            this.grab.OnSocialMessage += Grab_OnShardMessage;
            this.grab.OnGiftMessage += Grab_OnGiftMessage;
            this.grab.OnRoomUserSeqMessage += Grab_OnRoomUserSeqMessage;
            this.grab.OnFansclubMessage += Grab_OnFansclubMessage; ;
            this.grab.OnControlMessage += Grab_OnControlMessage;

            this.socketServer = socket;
            //dieout.Start();
            giftCountTimer.Start();

            // 启用AI匹配
            AutoReplyService.SetAIMatching(true);

            // 初始化保活定时器
            keepAliveTimer = new System.Timers.Timer(5000); // 每5秒检查一次
            keepAliveTimer.Elapsed += KeepAlive_Elapsed;
            keepAliveTimer.Start();
        }

        private void GiftCountTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            var now = DateTime.Now;
            var timeOutKeys = giftCountCache.Where(w => w.Value.Item2 < now.AddSeconds(-10) || w.Value == null).Select(s => s.Key).ToList();

            //淘汰过期的礼物计数缓存
            lock (giftCountCache)
            {
                timeOutKeys.ForEach(key =>
                {
                    giftCountCache.TryRemove(key, out _);

                });
            }
        }

        private bool CheckRoomId(long roomid)
        {
            if (!Appsetting.Current.WebRoomIds.Any()) return true;

            var webrid = AppRuntime.RoomCaches.GetCachedWebRoomid(roomid.ToString());
            if (webrid <= 0) return true;
            
            return Appsetting.Current.WebRoomIds.Contains(webrid);
        }

        //解析用户
        private MsgUser GetUser(User data)
        {
            if (data == null) return null;
            MsgUser user = new MsgUser()
            {
                DisplayId = data.displayId,
                ShortId = data.shortId,
                Gender = data.Gender,
                Id = data.Id,
                Level = data.Level,
                PayLevel = (int)(data.payGrade?.Level ?? -1),
                Nickname = data.Nickname ?? "用户" + data.displayId,
                HeadImgUrl = data.avatarThumb?.urlLists?.FirstOrDefault() ?? "",
                SecUid = data.sec_uid,
                FollowerCount = data.followInfo?.followerCount ?? -1,
                FollowingCount = data.followInfo?.followingCount ?? -1,
                FollowStatus = data.followInfo?.followStatus ?? -1,
            };
            user.FansClub = new FansClubInfo()
            {
                ClubName = "",
                Level = 0
            };

            if (data.fansClub != null && data.fansClub.Data != null)
            {
                user.FansClub.ClubName = data.fansClub.Data.clubName;
                user.FansClub.Level = data.fansClub.Data.Level;
            }

            return user;
        }

        static int count = 0;
        private void PrintMsg(Msg msg, PackMsgType barType)
        {
            var rinfo = AppRuntime.RoomCaches.GetCachedWebRoomInfo(msg.RoomId.ToString());
            var roomName = (rinfo?.Owner?.Nickname ?? ("直播间" + (msg.WebRoomId == 0 ? msg.RoomId : msg.WebRoomId)));
            var text = $"{DateTime.Now.ToString("HH:mm:ss")} [{roomName}] [{barType}]" ;

            if (msg.User != null)
            {
                text += $" [{msg.User?.GenderToString()}] ";
            }

            ConsoleColor color = Appsetting.Current.ColorMap[barType].Item1;
            var append = msg.Content;
            switch (barType)
            {
                case PackMsgType.弹幕消息: append = $"{msg?.User?.Nickname}: {msg.Content}"; break;
                case PackMsgType.下播: append = $"直播已结束"; break;
                default: break;
            }

            text += append;

            if (Appsetting.Current.BarrageLog)
            {
                Logger.LogBarrage(barType, msg);
            }

            if (!Appsetting.PrintBarrage) return;
            if (Appsetting.Current.PrintFilter.Any() && !Appsetting.Current.PrintFilter.Contains(barType.GetHashCode())) return;

            OnPrint?.Invoke(this, new PrintEventArgs()
            {
                Color = color,
                Message = text,
                MsgType = barType
            });

            if (++count > 10000)
            {
                Console.Clear();
                Logger.PrintColor("控制台已清理");
                count = 0;
            }
            Logger.PrintColor(text + "\n", color);
        }

        //粉丝团
        private void Grab_OnFansclubMessage(object sender, WssBarrageGrab.RoomMessageEventArgs<FansclubMessage> e)
        {
            var msg = e.Message;
            if (!CheckRoomId(msg.Common.roomId)) return;
            var enty = new FansclubMsg()
            {
                MsgId = msg.Common.msgId,
                Content = msg.Content,
                RoomId = msg.Common.roomId,
                WebRoomId = AppRuntime.RoomCaches.GetCachedWebRoomid(msg.Common.roomId.ToString()),
                Type = msg.Type,
                User = GetUser(msg.User)
            };
            enty.Level = enty.User.FansClub.Level;

            var msgType = PackMsgType.粉丝团消息;
            PrintMsg(enty, msgType);
            Broadcast(new BarrageMsgPack(enty.ToJson(), msgType, e.Process));
        }

        //统计消息
        private void Grab_OnRoomUserSeqMessage(object sender, WssBarrageGrab.RoomMessageEventArgs<RoomUserSeqMessage> e)
        {
            var msg = e.Message;
            if (!CheckRoomId(msg.Common.roomId)) return;
            var enty = new UserSeqMsg()
            {
                MsgId = msg.Common.msgId,
                OnlineUserCount = msg.Total,
                TotalUserCount = msg.totalUser,
                TotalUserCountStr = msg.totalPvForAnchor,
                OnlineUserCountStr = msg.onlineUserForAnchor,
                RoomId = msg.Common.roomId,
                WebRoomId = AppRuntime.RoomCaches.GetCachedWebRoomid(msg.Common.roomId.ToString()),
                Content = $"当前直播间人数 {msg.onlineUserForAnchor}，累计直播间人数 {msg.totalPvForAnchor}",
                User = null
            };

            var msgType = PackMsgType.直播间统计;
            PrintMsg(enty, msgType);
            var pack = new BarrageMsgPack(enty.ToJson(), msgType, e.Process);
            Broadcast(pack);
        }

        //礼物
        private void Grab_OnGiftMessage(object sender, WssBarrageGrab.RoomMessageEventArgs<GiftMessage> e)
        {
            var msg = e.Message;
            if (!CheckRoomId(msg.Common.roomId)) return;

            var key = msg.Common.roomId + "-" + msg.giftId + "-" + msg.groupId.ToString();

            int currCount = (int)msg.repeatCount;
            int lastCount = 0;
            //Combo 为1时，表示为可连击礼物
            if (msg.Gift.Combo)
            {
                //判断礼物重复
                if (msg.repeatEnd == 1)
                {
                    //清除缓存中的key
                    if (msg.groupId > 0 && giftCountCache.ContainsKey(key))
                    {
                        giftCountCache.TryRemove(key, out _);
                    }
                    return;
                }
                var backward = currCount <= lastCount;
                if (currCount <= 0) currCount = 1;

                if (giftCountCache.ContainsKey(key))
                {
                    lastCount = giftCountCache[key].Item1;
                    backward = currCount <= lastCount;
                    if (!backward)
                    {
                        lock (giftCountCache)
                        {
                            giftCountCache[key] = Tuple.Create(currCount, DateTime.Now);
                        }
                    }
                }
                else
                {
                    if (msg.groupId > 0 && !backward)
                    {
                        giftCountCache.TryAdd(key, Tuple.Create(currCount, DateTime.Now));
                    }
                }
                //比上次小，则说明先后顺序出了问题，直接丢掉，应为比它大的消息已经处理过了
                if (backward) return;
            }

            var count = currCount - lastCount;

            var enty = new GiftMsg()
            {
                MsgId = msg.Common.msgId,
                RoomId = msg.Common.roomId,
                WebRoomId = AppRuntime.RoomCaches.GetCachedWebRoomid(msg.Common.roomId.ToString()),
                Content = $"{msg.User.Nickname} 送出 {msg.Gift.Name}{(msg.Gift.Combo ? "(可连击)" : "")} x {msg.repeatCount}个，增量{count}个",
                DiamondCount = msg.Gift.diamondCount,
                RepeatCount = msg.repeatCount,
                GiftCount = count,
                GroupId = msg.groupId,
                GiftId = msg.giftId,
                GiftName = msg.Gift.Name,
                Combo = msg.Gift.Combo,
                ImgUrl = msg.Gift.Image?.urlLists?.FirstOrDefault()??"",
                User = GetUser(msg.User),                
                ToUser = GetUser(msg.toUser)
            };

            if (enty.ToUser != null)
            {
                enty.Content += "，给" + enty.ToUser.Nickname;
            }

            var msgType = PackMsgType.礼物消息;
            PrintMsg(enty, msgType);
            var pack = new BarrageMsgPack(enty.ToJson(), PackMsgType.礼物消息, e.Process);
            Broadcast(pack);
        }

        //关注
        private void Grab_OnSocialMessage(object sender, WssBarrageGrab.RoomMessageEventArgs<SocialMessage> e)
        {
            var msg = e.Message;
            if (!CheckRoomId(msg.Common.roomId)) return;
            if (msg.Action != 1) return;
            var enty = new Msg()
            {
                MsgId = msg.Common.msgId,
                Content = $"{msg.User.Nickname} 关注了主播",
                RoomId = msg.Common.roomId,
                WebRoomId = AppRuntime.RoomCaches.GetCachedWebRoomid(msg.Common.roomId.ToString()),
                User = GetUser(msg.User)
            };

            var msgType = PackMsgType.关注消息;
            PrintMsg(enty, msgType);
            var pack = new BarrageMsgPack(enty.ToJson(), msgType, e.Process);
            Broadcast(pack);
        }

        //直播间分享
        private void Grab_OnShardMessage(object sender, WssBarrageGrab.RoomMessageEventArgs<SocialMessage> e)
        {
            var msg = e.Message;
            if (!CheckRoomId(msg.Common.roomId)) return;
            if (msg.Action != 3) return;
            ShareType type = ShareType.未知;
            if (Enum.IsDefined(type.GetType(), int.Parse(msg.shareTarget)))
            {
                type = (ShareType)int.Parse(msg.shareTarget);
            }

            var enty = new ShareMessage()
            {
                MsgId = msg.Common.msgId,
                Content = $"{msg.User.Nickname} 分享了直播间到{type}",
                RoomId = msg.Common.roomId,
                WebRoomId = AppRuntime.RoomCaches.GetCachedWebRoomid(msg.Common.roomId.ToString()),
                ShareType = type,
                User = GetUser(msg.User)
            };

            var msgType = PackMsgType.直播间分享;
            PrintMsg(enty, msgType);

            //shareTarget: (112:好友),(1微信)(2朋友圈)(3微博)(5:qq)(4:qq空间),shareType: 1            
            var pack = new BarrageMsgPack(enty.ToJson(), msgType, e.Process);
            Broadcast(pack);
        }

        //来了
        private void Grab_OnMemberMessage(object sender, WssBarrageGrab.RoomMessageEventArgs<Modles.ProtoEntity.MemberMessage> e)
        {
            var msg = e.Message;
            if (!CheckRoomId(msg.Common.roomId)) return;

            var enty = new Modles.JsonEntity.MemberMessage()
            {
                MsgId = msg.Common.msgId,
                Content = $"{msg.User.Nickname} 来了 直播间人数:{msg.memberCount}",
                RoomId = msg.Common.roomId,
                WebRoomId = AppRuntime.RoomCaches.GetCachedWebRoomid(msg.Common.roomId.ToString()),
                CurrentCount = msg.memberCount,
                User = GetUser(msg.User)
            };

            var msgType = PackMsgType.进直播间;
            PrintMsg(enty, msgType);
            var pack = new BarrageMsgPack(enty.ToJson(), msgType, e.Process);
            Broadcast(pack);
        }

        //点赞
        private void Grab_OnLikeMessage(object sender, WssBarrageGrab.RoomMessageEventArgs<LikeMessage> e)
        {
            var msg = e.Message;
            if (!CheckRoomId(msg.Common.roomId)) return;

            var enty = new LikeMsg()
            {
                MsgId = msg.Common.msgId,
                Count = msg.Count,
                Content = $"{msg.User.Nickname} 为主播点了{msg.Count}个赞，总点赞{msg.Total}",
                RoomId = msg.Common.roomId,
                WebRoomId = AppRuntime.RoomCaches.GetCachedWebRoomid(msg.Common.roomId.ToString()),
                Total = msg.Total,
                User = GetUser(msg.User)
            };

            var msgType = PackMsgType.点赞消息;
            PrintMsg(enty, msgType);
            var pack = new BarrageMsgPack(enty.ToJson(), msgType, e.Process);
            Broadcast(pack);
        }

        //弹幕
        private void Grab_OnChatMessage(object sender, WssBarrageGrab.RoomMessageEventArgs<ChatMessage> e)
        {
            var msg = e.Message;
            if (!CheckRoomId(msg.Common.roomId)) return;

            var enty = new Msg()
            {
                MsgId = msg.Common.msgId,
                Content = msg.Content,
                RoomId = msg.Common.roomId,
                WebRoomId = AppRuntime.RoomCaches.GetCachedWebRoomid(msg.Common.roomId.ToString()),
                User = GetUser(msg.User),
            };

            var msgType = PackMsgType.弹幕消息;
            PrintMsg(enty, msgType);

            Console.WriteLine($"[自动回复] 检查弹幕: {msg.Content}");
            string reply = AutoReplyService.GetReply(msg.Content);
            if (!string.IsNullOrEmpty(reply))
            {
                Console.WriteLine($"[自动回复] 准备发送回复: {reply}");
                // 创建回复消息
                var replyMsg = new Msg()
                {
                    MsgId = DateTime.Now.Ticks,  // 使用时间戳作为消息ID
                    Content = reply,
                    RoomId = msg.Common.roomId,
                    WebRoomId = AppRuntime.RoomCaches.GetCachedWebRoomid(msg.Common.roomId.ToString()),
                    User = new MsgUser { 
                        Nickname = "自动回复",
                        DisplayId = "0",
                        Id = 0,
                        Level = 0
                    },
                };

                // 发送回复到浏览器
                try
                {
                    // 查找包含"抖音直播间"的窗口
                    IntPtr hWnd = IntPtr.Zero;
                    WinApi.EnumWindows((hwnd, lParam) =>
                    {
                        StringBuilder title = new StringBuilder(256);
                        WinApi.GetWindowText(hwnd, title, 256);
                        if (title.ToString().Contains("抖音直播间"))
                        {
                            hWnd = hwnd;
                            return false; // 停止枚举
                        }
                        return true; // 继续枚举
                    }, IntPtr.Zero);

                    if (hWnd != IntPtr.Zero)
                    {
                        // 激活浏览器窗口
                        WinApi.SetForegroundWindow(hWnd);
                        System.Threading.Thread.Sleep(100);

                        // 模拟输入文本
                        SendKeys.SendWait(reply);
                        System.Threading.Thread.Sleep(100);
                        SendKeys.SendWait("{ENTER}");
                        
                        Console.WriteLine($"[自动回复] 已发送回复到浏览器");
                    }
                    else
                    {
                        Console.WriteLine("[自动回复] 未找到抖音直播间窗口");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"发送回复到浏览器失败: {ex.Message}");
                }

                // 广播回复消息
                var replyPack = new BarrageMsgPack(replyMsg.ToJson(), PackMsgType.弹幕消息, e.Process);
                Broadcast(replyPack);
            }

            var pack = new BarrageMsgPack(enty.ToJson(), msgType, e.Process);
            Broadcast(pack);
        }

        //直播间状态变更
        private void Grab_OnControlMessage(object sender, WssBarrageGrab.RoomMessageEventArgs<ControlMessage> e)
        {
            var msg = e.Message;
            if (!CheckRoomId(msg.Common.roomId)) return;
            BarrageMsgPack pack = null;
            //下播
            if (msg.Status == 3)
            {
                var enty = new Msg()
                {
                    MsgId = msg.Common.msgId,
                    Content = "直播已结束",
                    RoomId = msg.Common.roomId,
                    WebRoomId = AppRuntime.RoomCaches.GetCachedWebRoomid(msg.Common.roomId.ToString()),
                    User = null
                };

                var msgType = PackMsgType.下播;
                PrintMsg(enty, msgType);
                pack = new BarrageMsgPack(enty.ToJson(), PackMsgType.下播, e.Process);
            }

            if (pack != null)
            {
                Broadcast(pack);
            }
        }

        //static int count = 0;
        //private void Print(string msg, ConsoleColor color, PackMsgType bartype)
        //{
        //    if (!Appsetting.PrintFilter.Any(a => a == bartype.GetHashCode())) return;
        //    if (Appsetting.PrintBarrage)
        //    {
        //        if (++count > 1000)
        //        {
        //            Console.Clear();
        //            Logger.PrintColor("控制台已清理");
        //        }
        //        Logger.PrintColor($"{DateTime.Now.ToString("HH:mm:ss")} [{bartype.ToString()}] " + msg + "\n", color);
        //        count = 0;
        //    }
        //}

        private void Dieout_Elapsed(object sender, ElapsedEventArgs e)
        {
            var now = DateTime.Now;
            var dieoutKvs = socketList.Where(w => w.Value.LastPing.AddSeconds(dieout.Interval * 3) < now).ToList();
            dieoutKvs.ForEach(f => f.Value.Socket.Close());
        }

        private void Listen(IWebSocketConnection socket)
        {
            //客户端url
            string clientUrl = socket.ConnectionInfo.ClientIpAddress + ":" + socket.ConnectionInfo.ClientPort;
            if (!socketList.ContainsKey(clientUrl))
            {
                socketList.Add(clientUrl, new UserState(socket));
                Logger.PrintColor($"{DateTime.Now.ToLongTimeString()} 已经建立与[{clientUrl}]的连接", ConsoleColor.Green);
            }
            else
            {
                socketList[clientUrl].Socket = socket;
            }

            //接收指令
            socket.OnMessage = (message) =>
            {
                try
                {
                    var cmdPack = JsonConvert.DeserializeObject<Command>(message);
                    if (cmdPack == null) return;

                    switch (cmdPack.Cmd)
                    {
                        case CommandCode.Close:
                            this.Close();
                            break;
                        case CommandCode.EnableProxy:
                            {
                                var enable = (bool)cmdPack.Data;
                                if (enable)
                                    this.grab.Proxy.RegisterSystemProxy();
                                else
                                    this.grab.Proxy.CloseSystemProxy();
                                break;
                            }
                        case CommandCode.DisplayConsole:
                            {
                                var display = (bool)cmdPack.Data;
                                AppRuntime.DisplayConsole(display);
                                break;
                            }
                    }
                }
                catch (Exception) { }
            };

            socket.OnClose = () =>
            {
                socketList.Remove(clientUrl);
                Logger.PrintColor($"{DateTime.Now.ToLongTimeString()} 已经关闭与[{clientUrl}]的连接", ConsoleColor.Red);
            };

            socket.OnPing = (data) =>
            {
                socketList[clientUrl].LastPing = DateTime.Now;
                socket.SendPong(Encoding.UTF8.GetBytes("pong"));
            };
        }

        /// <summary>
        /// 广播消息
        /// </summary>
        /// <param name="msg"></param>
        public void Broadcast(BarrageMsgPack pack)
        {
            if (pack == null) return;
            if (Appsetting.Current.PushFilter.Any() && !Appsetting.Current.PushFilter.Contains(pack.Type.GetHashCode())) return;

            var offLines = new List<string>();
            foreach (var item in socketList)
            {
                var state = item.Value;
                if (item.Value.Socket.IsAvailable)
                {
                    state.Socket.Send(pack.ToJson());
                }
                else
                {
                    offLines.Add(item.Key);
                }
            }
            //删除掉线的套接字        
            offLines.ForEach(key => socketList.Remove(key));
        }

        /// <summary>
        /// 开始监听
        /// </summary>
        public void StartListen()
        {
            this.grab.Start(); //启动代理
            this.socketServer.Start(Listen);//启动监听           
        }

        /// <summary>
        /// 关闭服务器连接，并关闭系统代理
        /// </summary>
        public void Close()
        {
            socketList.Values.ToList().ForEach(f => f.Socket.Close());
            socketList.Clear();
            socketServer.Dispose();
            grab.Dispose();

            this.OnClose?.Invoke(this, EventArgs.Empty);
            keepAliveTimer?.Stop();
            keepAliveTimer?.Dispose();
        }

        class UserState
        {
            /// <summary>
            /// 套接字
            /// </summary>
            public IWebSocketConnection Socket { get; set; }

            /// <summary>
            /// 上次发起心跳包时间
            /// </summary>
            public DateTime LastPing { get; set; } = DateTime.Now;

            public UserState()
            {

            }
            public UserState(IWebSocketConnection socket)
            {
                Socket = socket;
            }
        }

        public class PrintEventArgs : EventArgs
        {
            public string Message { get; set; }

            public ConsoleColor Color { get; set; }

            public PackMsgType MsgType { get; set; }
        }

        private const string PAUSE_TEXT = "长时间无操作，已暂停播放";
        private const string CONTINUE_TEXT = "继续播放";

        private void KeepAlive_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                // 查找直播间窗口
                IntPtr hWnd = IntPtr.Zero;
                WinApi.EnumWindows((hwnd, lParam) =>
                {
                    StringBuilder title = new StringBuilder(256);
                    WinApi.GetWindowText(hwnd, title, 256);
                    if (title.ToString().Contains("抖音直播间"))
                    {
                        hWnd = hwnd;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);

                if (hWnd != IntPtr.Zero)
                {
                    // 查找包含"继续播放"文本的子窗口
                    IntPtr continueButtonHwnd = IntPtr.Zero;
                    WinApi.EnumChildWindows(hWnd, (childHwnd, lParam) =>
                    {
                        StringBuilder text = new StringBuilder(256);
                        WinApi.GetWindowText(childHwnd, text, 256);
                        
                        if (text.ToString().Contains(CONTINUE_TEXT))
                        {
                            continueButtonHwnd = childHwnd;
                            return false;
                        }
                        return true;
                    }, IntPtr.Zero);

                    if (continueButtonHwnd != IntPtr.Zero)
                    {
                        // 获取按钮位置
                        WinApi.RECT buttonRect;
                        WinApi.GetWindowRect(continueButtonHwnd, out buttonRect);

                        // 计算按钮中心点
                        int buttonX = buttonRect.Left + (buttonRect.Right - buttonRect.Left) / 2;
                        int buttonY = buttonRect.Top + (buttonRect.Bottom - buttonRect.Top) / 2;

                        // 模拟鼠标点击
                        WinApi.SetForegroundWindow(hWnd);
                        System.Threading.Thread.Sleep(100);
                        WinApi.SetCursorPos(buttonX, buttonY);
                        WinApi.mouse_event(WinApi.MOUSEEVENTF_LEFTDOWN | WinApi.MOUSEEVENTF_LEFTUP, buttonX, buttonY, 0, 0);

                        Console.WriteLine("[保活] 找到并点击了继续播放按钮");
                    }
                    else
                    {
                        // 如果找不到按钮，可能还没有暂停，不需要处理
                        Console.WriteLine("[保活] 未发现暂停状态，无需处理");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[保活] 错误: {ex.Message}");
            }
        }
    }
}
