using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using System.IO;
using Newtonsoft.Json;

namespace BarrageGrab
{
    public class AutoReplyConfig
    {
        public Dictionary<string, List<string>> Rules { get; set; }
        public bool EnableAIMatching { get; set; }
    }

    public class AutoReplyService
    {
        private Dictionary<string, List<string>> replyRules;
        private bool enableAIMatching;
        private Random random;
        private readonly string configPath;

        public AutoReplyService()
        {
            replyRules = new Dictionary<string, List<string>>();
            enableAIMatching = false;
            random = new Random();
            
            // 配置文件保存在程序目录下的 config 文件夹中
            string configDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }
            configPath = Path.Combine(configDir, "autoreply.json");
            LoadConfig();
        }

        /// <summary>
        /// 添加回复规则
        /// </summary>
        public void AddReplyRule(string keyword, string reply)
        {
            if (!string.IsNullOrEmpty(keyword))
            {
                if (!replyRules.ContainsKey(keyword))
                {
                    replyRules[keyword] = new List<string>();
                }
                if (!replyRules[keyword].Contains(reply))
                {
                    replyRules[keyword].Add(reply);
                }
            }
        }

        /// <summary>
        /// 获取关键词的所有回复内容
        /// </summary>
        public Dictionary<string, List<string>> GetAllRules()
        {
            return replyRules;
        }

        /// <summary>
        /// 清除所有规则
        /// </summary>
        public void ClearRules()
        {
            replyRules.Clear();
        }

        /// <summary>
        /// 设置是否启用AI匹配
        /// </summary>
        public void SetAIMatching(bool enable)
        {
            enableAIMatching = enable;
        }

        /// <summary>
        /// 获取AI匹配状态
        /// </summary>
        public bool GetAIMatchingEnabled()
        {
            return enableAIMatching;
        }

        /// <summary>
        /// 保存配置到文件
        /// </summary>
        public void SaveConfig()
        {
            try
            {
                var config = new AutoReplyConfig
                {
                    Rules = replyRules,
                    EnableAIMatching = enableAIMatching
                };

                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存自动回复配置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从文件加载配置
        /// </summary>
        private void LoadConfig()
        {
            try
            {
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    var config = JsonConvert.DeserializeObject<AutoReplyConfig>(json);
                    if (config != null)
                    {
                        replyRules = config.Rules ?? new Dictionary<string, List<string>>();
                        enableAIMatching = config.EnableAIMatching;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载自动回复配置失败: {ex.Message}");
                replyRules = new Dictionary<string, List<string>>();
                enableAIMatching = false;
            }
        }

        /// <summary>
        /// 检查消息并获取回复
        /// </summary>
        public string GetReply(string message)
        {
            if (string.IsNullOrEmpty(message))
                return null;

            Console.WriteLine($"[自动回复] 收到消息: {message}");
            Console.WriteLine($"[自动回复] 当前规则数: {replyRules.Count}");

            // 1. 精确匹配特殊字符
            foreach (var rule in replyRules)
            {
                Console.WriteLine($"[自动回复] 尝试匹配关键字: {rule.Key}");
                if (message.Contains(rule.Key))
                {
                    var reply = GetRandomReply(rule.Value);
                    Console.WriteLine($"[自动回复] 匹配成功! 回复: {reply}");
                    return reply;
                }
            }

            // 2. AI近似匹配
            if (enableAIMatching)
            {
                foreach (var rule in replyRules)
                {
                    var similarity = CalculateSimilarity(message, rule.Key);
                    Console.WriteLine($"[自动回复] AI匹配 '{rule.Key}' 相似度: {similarity:F2}");
                    if (similarity > 0.8)
                    {
                        var reply = GetRandomReply(rule.Value);
                        Console.WriteLine($"[自动回复] AI匹配成功! 回复: {reply}");
                        return reply;
                    }
                }
            }

            Console.WriteLine("[自动回复] 没有找到匹配的回复");
            return null;
        }

        private string GetRandomReply(List<string> replies)
        {
            if (replies == null || replies.Count == 0)
                return null;
            
            int index = random.Next(replies.Count);
            return replies[index];
        }

        private double CalculateSimilarity(string str1, string str2)
        {
            int maxLength = Math.Max(str1.Length, str2.Length);
            if (maxLength == 0) return 1.0;

            int distance = LevenshteinDistance(str1.ToLower(), str2.ToLower());
            return 1.0 - ((double)distance / maxLength);
        }

        private int LevenshteinDistance(string str1, string str2)
        {
            int[,] matrix = new int[str1.Length + 1, str2.Length + 1];

            for (int i = 0; i <= str1.Length; i++)
                matrix[i, 0] = i;
            for (int j = 0; j <= str2.Length; j++)
                matrix[0, j] = j;

            for (int i = 1; i <= str1.Length; i++)
            {
                for (int j = 1; j <= str2.Length; j++)
                {
                    int cost = (str1[i - 1] == str2[j - 1]) ? 0 : 1;
                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost
                    );
                }
            }

            return matrix[str1.Length, str2.Length];
        }
    }
}
