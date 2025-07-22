using System;
using System.IO;
using System.Text;

namespace hangfire.Helpers
{
    public static class LogHelper
    {
        // 用於同步對同一文件的訪問的對象鎖
        private static readonly object _fileLock = new object();

        /// <summary>
        /// 通用日誌記錄方法，將資訊寫入到指定的日誌檔案
        /// </summary>
        /// <param name="fileNamePrefix">日誌檔案名稱前綴</param>
        /// <param name="logContent">要記錄的內容</param>
        public static void WriteLog(string fileNamePrefix, string logContent)
        {
            try
            {
                // 建立logs目錄（如果不存在）
                string logsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logsDirectory))
                {
                    // 使用lock保護目錄創建
                    lock (_fileLock)
                    {
                        if (!Directory.Exists(logsDirectory))
                        {
                            Directory.CreateDirectory(logsDirectory);
                        }
                    }
                }

                // 取得今天的日期作為檔名的一部分
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                string logFileName = Path.Combine(logsDirectory, $"{fileNamePrefix}_{today}.txt");

                // 加入時間戳到內容前
                string timestampedContent = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {logContent}{Environment.NewLine}";

                // 使用lock確保多執行緒安全寫入
                lock (_fileLock)
                {
                    // 寫入日誌檔案（追加模式）
                    File.AppendAllText(logFileName, timestampedContent, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                // 如果記錄失敗，記錄到標準日誌
                Console.WriteLine($"記錄資訊到檔案時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 記錄服務Ping失敗的資訊到文字檔
        /// </summary>
        /// <param name="serviceName">服務名稱</param>
        /// <param name="serviceUrl">服務URL</param>
        /// <param name="reason">失敗原因</param>
        public static void LogPingFailure(string serviceName, string serviceUrl, string reason)
        {
            string logContent = $"服務: {serviceName}, IP/URL: {serviceUrl}, 失敗原因: {reason}";
            WriteLog("ping_failures", logContent);
        }
    }
}
