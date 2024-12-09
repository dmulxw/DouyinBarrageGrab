using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Linq;

namespace BarrageGrab
{
    public class FormAutoReply : Form
    {
        private AutoReplyService autoReplyService;
        private DataGridView dgvRules;
        private TextBox txtKeyword;
        private TextBox txtReply;
        private Button btnAdd;
        private Button btnDelete;
        private Button btnSave;
        private CheckBox chkAIMatch;

        public FormAutoReply(AutoReplyService service)
        {
            InitializeComponent();
            this.autoReplyService = service;
            LoadRules();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            
            this.ClientSize = new System.Drawing.Size(600, 500);
            this.Name = "FormAutoReply";
            this.Text = "自动回复设置";
            this.StartPosition = FormStartPosition.CenterParent;

            // 创建AI匹配选项
            chkAIMatch = new CheckBox
            {
                Text = "启用智能匹配",
                Location = new System.Drawing.Point(10, 10),
                AutoSize = true,
                Checked = autoReplyService.GetAIMatchingEnabled()
            };
            this.Controls.Add(chkAIMatch);

            // 创建输入区域
            var lblKeyword = new Label
            {
                Text = "关键词:",
                Location = new System.Drawing.Point(10, 40),
                AutoSize = true
            };
            this.Controls.Add(lblKeyword);

            txtKeyword = new TextBox
            {
                Location = new System.Drawing.Point(70, 37),
                Width = 200
            };
            this.Controls.Add(txtKeyword);

            var lblReply = new Label
            {
                Text = "回复:",
                Location = new System.Drawing.Point(280, 40),
                AutoSize = true
            };
            this.Controls.Add(lblReply);

            txtReply = new TextBox
            {
                Location = new System.Drawing.Point(320, 37),
                Width = 200
            };
            this.Controls.Add(txtReply);

            btnAdd = new Button
            {
                Text = "添加规则",
                Location = new System.Drawing.Point(530, 35),
                Width = 60,
                Height = 25
            };
            btnAdd.Click += BtnAdd_Click;
            this.Controls.Add(btnAdd);

            // 创建数据网格
            dgvRules = new DataGridView
            {
                Location = new System.Drawing.Point(10, 70),
                Size = new System.Drawing.Size(580, 380),
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AllowUserToAddRows = false
            };
            dgvRules.Columns.Add("Keyword", "关键词");
            dgvRules.Columns.Add("Replies", "回复内容（多个回复用逗号分隔）");
            this.Controls.Add(dgvRules);

            // 创建底部按钮
            btnDelete = new Button
            {
                Text = "删除规则",
                Location = new System.Drawing.Point(10, 460),
                Width = 80,
                Height = 30
            };
            btnDelete.Click += BtnDelete_Click;
            this.Controls.Add(btnDelete);

            btnSave = new Button
            {
                Text = "保存规则",
                Location = new System.Drawing.Point(510, 460),
                Width = 80,
                Height = 30
            };
            btnSave.Click += BtnSave_Click;
            this.Controls.Add(btnSave);

            this.ResumeLayout(false);
        }

        private void LoadRules()
        {
            dgvRules.Rows.Clear();
            var rules = autoReplyService.GetAllRules();
            foreach (var rule in rules)
            {
                dgvRules.Rows.Add(rule.Key, string.Join("，", rule.Value));
            }
        }

        private void BtnAdd_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtKeyword.Text))
            {
                MessageBox.Show("请输入关键词", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(txtReply.Text))
            {
                MessageBox.Show("请输入回复内容", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 查找是否已存在相同关键词
            foreach (DataGridViewRow row in dgvRules.Rows)
            {
                if (row.Cells[0].Value?.ToString() == txtKeyword.Text)
                {
                    var replies = row.Cells[1].Value?.ToString().Split('，').ToList() ?? new List<string>();
                    if (!replies.Contains(txtReply.Text))
                    {
                        replies.Add(txtReply.Text);
                        row.Cells[1].Value = string.Join("，", replies);
                    }
                    txtKeyword.Clear();
                    txtReply.Clear();
                    return;
                }
            }

            // 添加新规则
            dgvRules.Rows.Add(txtKeyword.Text, txtReply.Text);
            txtKeyword.Clear();
            txtReply.Clear();
        }

        private void BtnDelete_Click(object sender, EventArgs e)
        {
            if (dgvRules.SelectedRows.Count > 0)
            {
                dgvRules.Rows.Remove(dgvRules.SelectedRows[0]);
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            autoReplyService.ClearRules();
            autoReplyService.SetAIMatching(chkAIMatch.Checked);

            foreach (DataGridViewRow row in dgvRules.Rows)
            {
                if (row.Cells[0].Value != null && row.Cells[1].Value != null)
                {
                    string keyword = row.Cells[0].Value.ToString();
                    string[] replies = row.Cells[1].Value.ToString().Split('，');
                    foreach (var reply in replies)
                    {
                        if (!string.IsNullOrWhiteSpace(reply))
                        {
                            autoReplyService.AddReplyRule(keyword, reply.Trim());
                        }
                    }
                }
            }

            // 保存配置到文件
            autoReplyService.SaveConfig();
            MessageBox.Show("规则保存成功！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
