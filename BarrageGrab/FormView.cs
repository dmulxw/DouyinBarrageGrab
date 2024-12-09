using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

namespace BarrageGrab
{
    public class FormView : Form
    {
        private WsBarrageService barrageService;
        private AutoReplyService autoReplyService;
        private RichTextBox txtLog;
        private Panel contentPanel;
        private NotifyIcon trayIcon;
        private ToolStrip toolStrip;

        public FormView()
        {
            this.barrageService = AppRuntime.WssService;
            this.autoReplyService = barrageService.AutoReplyService ?? new AutoReplyService(); // 确保 autoReplyService 被初始化
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "抖音弹幕监听";
            this.Size = new Size(800, 600);

            // 创建工具栏
            toolStrip = new ToolStrip
            {
                Dock = DockStyle.Top,
                BackColor = Color.FromArgb(240, 240, 240),
                RenderMode = ToolStripRenderMode.System
            };

            var btnAutoReply = new ToolStripButton
            {
                Text = "自动回复设置",
                Image = null, // 这里可以设置一个图标
                DisplayStyle = ToolStripItemDisplayStyle.Text
            };
            btnAutoReply.Click += BtnAutoReply_Click;
            toolStrip.Items.Add(btnAutoReply);

            // 创建托盘图标
            trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application, // 使用应用程序默认图标
                Visible = true,
                Text = "抖音弹幕监听"
            };

            // 创建托盘图标的右键菜单
            var contextMenu = new ContextMenuStrip();
            var menuAutoReply = new ToolStripMenuItem("自动回复设置");
            menuAutoReply.Click += BtnAutoReply_Click;
            var menuExit = new ToolStripMenuItem("退出");
            menuExit.Click += (s, e) => Application.Exit();

            contextMenu.Items.AddRange(new ToolStripItem[]
            {
                menuAutoReply,
                new ToolStripSeparator(),
                menuExit
            });

            trayIcon.ContextMenuStrip = contextMenu;
            trayIcon.DoubleClick += (s, e) => this.Show();

            // 创建内容面板
            contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };

            // 创建日志文本框
            txtLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.White,
                Font = new Font("Microsoft YaHei", 9F),
                BorderStyle = BorderStyle.None
            };

            // 按照正确的顺序添加控件
            this.Controls.Add(contentPanel);
            contentPanel.Controls.Add(txtLog);
            this.Controls.Add(toolStrip); // 工具栏最后添加，确保在最上层

            // 窗口事件
            this.FormClosing += FormView_FormClosing;
            this.Resize += FormView_Resize;
        }

        private void FormView_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
        }

        private void FormView_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
            }
        }

        private void BtnAutoReply_Click(object sender, EventArgs e)
        {
            if (autoReplyService == null)
            {
                autoReplyService = barrageService.AutoReplyService;
            }

            // 创建设置窗口
            Form settingsForm = new Form
            {
                Text = "自动回复设置",
                Size = new Size(600, 500),
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            // 创建规则列表
            var lstRules = new ListView
            {
                Location = new Point(10, 180),
                Size = new Size(560, 230),
                View = View.Details,
                FullRowSelect = true
            };
            lstRules.Columns.Add("关键字", 150);
            lstRules.Columns.Add("回复内容", 380);

            // 创建AI匹配选项
            var chkAIMatch = new CheckBox
            {
                Text = "启用智能匹配",
                Location = new Point(10, 10),
                AutoSize = true,
                Checked = autoReplyService.GetAIMatchingEnabled()
            };
            chkAIMatch.CheckedChanged += (s, ev) => autoReplyService.SetAIMatching(chkAIMatch.Checked);

            // 创建关键字输入框
            var lblKeyword = new Label { Text = "关键字:", Location = new Point(10, 40), AutoSize = true };
            var txtKeyword = new TextBox { Location = new Point(100, 40), Width = 200 };

            // 创建回复内容输入框
            var lblReply = new Label { Text = "回复内容:", Location = new Point(10, 70), AutoSize = true };
            var txtReply = new TextBox { Location = new Point(100, 70), Width = 400, Height = 60, Multiline = true };

            // 创建添加按钮
            var btnAdd = new Button
            {
                Text = "添加回复",
                Location = new Point(100, 140),
                Width = 100
            };
            btnAdd.Click += (s, ev) =>
            {
                if (!string.IsNullOrWhiteSpace(txtKeyword.Text) && !string.IsNullOrWhiteSpace(txtReply.Text))
                {
                    autoReplyService.AddReplyRule(txtKeyword.Text.Trim(), txtReply.Text.Trim());
                    RefreshRulesList(lstRules, autoReplyService.GetAllRules());
                    txtKeyword.Clear();
                    txtReply.Clear();
                }
            };

            // 创建删除按钮
            var btnDelete = new Button
            {
                Text = "删除选中",
                Location = new Point(470, 420),
                Width = 100
            };
            btnDelete.Click += (s, ev) =>
            {
                if (lstRules.SelectedItems.Count > 0)
                {
                    var keyword = lstRules.SelectedItems[0].Text;
                    autoReplyService.ClearRules();
                    var rules = autoReplyService.GetAllRules();
                    rules.Remove(keyword);
                    foreach (var rule in rules)
                    {
                        foreach (var reply in rule.Value)
                        {
                            autoReplyService.AddReplyRule(rule.Key, reply);//文化发布事业
                        }
                    }
                    RefreshRulesList(lstRules, rules);
                }
            };

            // 添加所有控件
            settingsForm.Controls.AddRange(new Control[]
            {
                chkAIMatch,
                lblKeyword, txtKeyword,
                lblReply, txtReply,
                btnAdd,
                lstRules,
                btnDelete
            });

            // 初始化规则列表
            RefreshRulesList(lstRules, autoReplyService.GetAllRules());

            // 窗口关闭时保存配置
            settingsForm.FormClosing += (s, ev) => autoReplyService.SaveConfig();

            settingsForm.ShowDialog();
        }

        private void RefreshRulesList(ListView listView, Dictionary<string, List<string>> rules)
        {
            listView.Items.Clear();
            foreach (var rule in rules)
            {
                var replies = string.Join(" | ", rule.Value);
                var item = new ListViewItem(new[] { rule.Key, replies });
                listView.Items.Add(item);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                trayIcon?.Dispose();
            }
            base.Dispose(disposing);
        }

        // 添加消息显示方法
        public void AppendMessage(string message)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action(() => AppendMessage(message)));
                return;
            }

            txtLog.AppendText(message + Environment.NewLine);
            txtLog.ScrollToCaret();
        }
    }
}
