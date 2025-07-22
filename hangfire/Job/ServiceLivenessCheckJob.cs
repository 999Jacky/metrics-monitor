using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Text;
using hangfire.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Threading;
using hangfire.Helpers;

namespace hangfire.Job {
    public class ServiceLivenessCheckJob: IJob<int> {
        private readonly ILogger<ServiceLivenessCheckJob> _logger;
        private readonly HangFireConst _options;
        private readonly IHttpClientFactory _httpClientFactory;

        public ServiceLivenessCheckJob(ILogger<ServiceLivenessCheckJob> logger, IOptions<HangFireConst> options, IHttpClientFactory httpClientFactory) {
            _logger = logger;
            _options = options.Value;
            _httpClientFactory = httpClientFactory;
        }

        public async Task Run(int arg) {
            if (_options.ServiceLivenessCheck == null || _options.ServiceLivenessCheck?.Services == null || !_options.ServiceLivenessCheck.Services.Any()) {
                _logger.LogInformation("未設定服務存活檢查");
                return;
            }

            var serviceResults = new StringBuilder();

            // 定義服務存活指標
            serviceResults.AppendLine("# HELP service_liveness_status 服務存活狀態 (1=在線, 0=離線)");
            serviceResults.AppendLine("# TYPE service_liveness_status gauge");
            // 定義上次失敗時間指標
            serviceResults.AppendLine("# HELP service_last_failure_time 服務上次失敗時間 (Unix時間戳，秒)");
            serviceResults.AppendLine("# TYPE service_last_failure_time gauge");
            // 定義服務失敗次數指標
            serviceResults.AppendLine("# HELP service_failure_count 服務失敗次數 (每日重置)");
            serviceResults.AppendLine("# TYPE service_failure_count counter");

            // 使用SemaphoreSlim限制最多同時3個ping檢查
            using var semaphore = new SemaphoreSlim(3);
            var tasks = new List<Task>();

            foreach (var service in _options.ServiceLivenessCheck.Services) {
                tasks.Add(Task.Run(async () => {
                    try {
                        await semaphore.WaitAsync();
                        _logger.LogInformation($"開始檢查服務存活狀態: {service.ServiceName} - {service.Url}");
                        var healthResult = await CheckServiceHealth(service.Url);
                        bool isAlive = healthResult.Item1;
                        string failureReason = healthResult.Item2;
                        int statusValue = isAlive ? 1 : 0;

                        // 獲取服務失敗信息
                        var failureInfo = _options.ServiceLivenessCheck.GetFailureInfo(service.ServiceName, service.Url);

                        // 更新失敗計數和時間
                        if (!isAlive) {
                            failureInfo.FailureCount++;
                            failureInfo.LastFailureTime = DateTime.Now;

                            // 記錄失敗資訊到當日的文字檔
                            LogHelper.LogPingFailure(service.ServiceName, service.Url, failureReason);
                        }

                        string cleanServiceName = CleanLabelValue(service.ServiceName);
                        // 建立本次檢查的結果
                        var result = new StringBuilder();
                        // 添加服務存活狀態指標
                        result.AppendLine($"service_liveness_status{{service_name=\"{cleanServiceName}\"}} {statusValue}");


                        // 添加失敗次數指標
                        result.AppendLine($"service_failure_count{{service_name=\"{cleanServiceName}\"}} {failureInfo.FailureCount}");

                        // 添加上次失敗時間指標（如果有）
                        if (failureInfo.LastFailureTime.HasValue) {

                            double unixTimestamp = new DateTimeOffset(failureInfo.LastFailureTime.Value).ToUnixTimeSeconds();
                            result.AppendLine($"service_last_failure_time{{service_name=\"{cleanServiceName}\"}} {unixTimestamp}");
                        }

                        if (isAlive) {
                            _logger.LogInformation($"服務存活檢查成功: {service.ServiceName} - {service.Url}");
                        }
                        else {
                            _logger.LogWarning($"服務存活檢查失敗: {service.ServiceName} - {service.Url}，累計失敗次數: {failureInfo.FailureCount}");
                        }

                        // 使用鎖來安全地將結果添加到總結果中
                        lock (serviceResults) {
                            serviceResults.Append(result);
                        }
                    }
                    catch (Exception ex) {
                        _logger.LogError(ex, $"服務存活檢查發生錯誤: {service.ServiceName} - {service.Url}");

                        // 獲取服務失敗信息
                        var failureInfo = _options.ServiceLivenessCheck.GetFailureInfo(service.ServiceName, service.Url);
                        failureInfo.FailureCount++;
                        failureInfo.LastFailureTime = DateTime.Now;

                        // 記錄異常資訊到當日的文字檔
                        LogHelper.LogPingFailure(service.ServiceName, service.Url, $"執行錯誤: {ex.Message}");

                        string cleanServiceName = CleanLabelValue(service.ServiceName);

                        var errorResult = new StringBuilder();
                        errorResult.AppendLine($"service_liveness_status{{service_name=\"{cleanServiceName}\", error=\"true\"}} 0");
                        errorResult.AppendLine($"service_failure_count{{service_name=\"{cleanServiceName}\"}} {failureInfo.FailureCount}");

                        double unixTimestamp = new DateTimeOffset(failureInfo.LastFailureTime.Value).ToUnixTimeSeconds();
                        errorResult.AppendLine($"service_liveness_last_failure_time{{service_name=\"{cleanServiceName}\"}} {unixTimestamp}");

                        // 使用鎖來安全地將錯誤結果添加到總結果中
                        lock (serviceResults) {
                            serviceResults.Append(errorResult);
                        }
                    }
                    finally {
                        // 釋放信號量
                        semaphore.Release();
                    }
                }));
            }

            // 等待所有檢查任務完成
            await Task.WhenAll(tasks);

            // 確保指標資料以換行符結束
            if (!serviceResults.ToString().EndsWith("\n")) {
                serviceResults.AppendLine();
            }

            // 將結果推送到 PushGateway
            await PushMetricsToGateway(serviceResults.ToString().Replace(Environment.NewLine, "\n"));
        }

        private async Task<Tuple<bool, string>> CheckServiceHealth(string url) {
            // 解析主機名稱
            Uri uri = new Uri(url);
            string host = uri.Host;

            // 使用Ping檢查服務存活狀態
            using Ping ping = new Ping();
            try {
                PingReply reply = await ping.SendPingAsync(host, TimeSpan.FromSeconds(3));
                if (reply.Status == IPStatus.Success) {
                    return new Tuple<bool, string>(true, string.Empty);
                } else {
                    return new Tuple<bool, string>(false, $"Ping狀態: {reply.Status}");
                }
            }
            catch (Exception ex) {
                return new Tuple<bool, string>(false, $"Ping異常: {ex.Message}");
            }
        }

        private async Task PushMetricsToGateway(string metricsData) {
            try {
                var httpClient = _httpClientFactory.CreateClient();

                var jobName = _options.ClientSetting?.JobName ?? "service_liveness";
                var instance = _options.ClientSetting?.Instance ?? "unknown";

                var urlParams = $"job/{jobName}/instance/{instance}";

                // 添加標籤
                foreach (var label in _options.ClientSetting?.Labels ?? new Dictionary<string, string>()) {
                    urlParams += $"/{label.Key}/{label.Value}";
                }

                // 添加IP地址（如果可用）
                var ip = await GetIp();
                if (ip != null) {
                    urlParams += $"/ip/{ip}";
                }

                // 設置Basic認證（如果有）
                if (_options.BasicAuth != null && !string.IsNullOrWhiteSpace(_options.BasicAuth.UserName)) {
                    var byteArray = Encoding.ASCII.GetBytes($"{_options.BasicAuth.UserName}:{_options.BasicAuth.Password}");
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                }

                var pushGatewayUrl = $"{_options.PushGatewayUrl}/metrics/{urlParams}";

                using (var content = new StringContent(metricsData, Encoding.UTF8, "text/plain")) {
                    var resp = await httpClient.PostAsync(pushGatewayUrl, content);

                    if (resp.IsSuccessStatusCode) {
                        _logger.LogInformation($"成功推送服務存活檢查結果到PushGateway");
                    }
                    else {
                        var responseBody = await resp.Content.ReadAsStringAsync();
                        _logger.LogWarning($"推送服務存活檢查結果到PushGateway失敗: {resp.StatusCode}, Response: {responseBody}");
                    }
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "推送服務存活檢查結果到PushGateway時發生錯誤");
            }
        }

        private async Task<string?> GetIp() {
            try {
                var url = "https://api.myip.com";
                var httpClient = _httpClientFactory.CreateClient();
                var res = await httpClient.GetFromJsonAsync<MyIpAddress>(url);
                if (res != null && !string.IsNullOrWhiteSpace(res.ip)) {
                    return res.ip;
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "獲取IP地址時發生錯誤");
            }
            return null;
        }

        // 新增方法：清理標籤值
        private string CleanLabelValue(string value) {
            if (string.IsNullOrEmpty(value)) return "";

            // 替換空格為底線
            value = value.Replace(" ", "_");

            // 轉義雙引號
            value = value.Replace("\"", "\\\"");

            // 轉義反斜線
            value = value.Replace("\\", "\\\\");

            // 轉義換行符
            value = value.Replace("\n", "\\n");
            value = value.Replace("\r", "\\r");

            return value;
        }
    }
}
