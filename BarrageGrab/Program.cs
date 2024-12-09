using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace BarrageGrab
{
    public class Program
    {
        static void Main(string[] args)
        {
            if (CheckAlreadyRunning())
            {
                Logger.PrintColor("已经有一个监听程序在运行，按任意键退出...");
                Console.ReadKey();
                return;
            }

            // 启用 Windows Forms 的单线程单元
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            WinApi.SetConsoleCtrlHandler(cancelHandler, true);//捕获控制台关闭
            WinApi.DisableQuickEditMode();//禁用控制台快速编辑模式
            if (WinApi.GetConsoleWindow() != IntPtr.Zero)
            {
                Console.Title = "抖音弹幕监听推送";
            }
            AppRuntime.DisplayConsole(!Appsetting.Current.HideConsole);
            AppRuntime.WssService.Grab.Proxy.SetUpstreamProxy(Appsetting.Current.UpstreamProxy);

            bool exited = false;
            AppRuntime.WssService.StartListen();

            Console.ForegroundColor = ConsoleColor.Green;
            Logger.LogSucc($"{AppRuntime.WssService.ServerLocation} 弹幕服务已启动...");
            Console.ForegroundColor = ConsoleColor.Gray;
            if (WinApi.GetConsoleWindow() != IntPtr.Zero)
            {
                Console.Title = $"抖音弹幕监听推送 [{AppRuntime.WssService.ServerLocation}]";
            }

            FormView mainForm = null;

            mainForm = new FormView();
            
            // 注册服务关闭事件
            AppRuntime.WssService.OnClose += (s, e) =>
            {
                exited = true;
                if (!mainForm.IsDisposed)
                {
                    mainForm.Invoke(new Action(() =>
                    {
                        mainForm.Close();
                    }));
                }
            };

            // 在主线程中运行窗口
            Application.Run(mainForm);

            Logger.PrintColor("服务器已关闭...");
        }

        private static WinApi.ControlCtrlDelegate cancelHandler = new WinApi.ControlCtrlDelegate((CtrlType) =>
        {
            switch (CtrlType)
            {
                case 0:
                    //Logger.PrintColor("0工具被强制关闭"); //Ctrl+C关闭
                    //server.Close();
                    break;
                case 2:
                    Logger.PrintColor("2工具被强制关闭");//按控制台关闭按钮关闭
                    AppRuntime.WssService.Close();
                    break;
            }
            return false;
        });

        private static bool CheckAlreadyRunning()
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(System.Diagnostics.Process.GetCurrentProcess().ProcessName);
            return processes.Length > 1;
        }
    }
}
