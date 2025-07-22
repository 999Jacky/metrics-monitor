using System.Net.Http.Headers;
using System.Text;
using Hangfire;
using hangfire.Model;
using hangfire.Helpers;
using Microsoft.Extensions.Options;

namespace hangfire.Job {
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = [10])]
    public class PushMetricToServer: IJob<int> {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOptionsMonitor<HangFireConst> _options;
        public PushMetricToServer(IHttpClientFactory httpClientFactory, IOptionsMonitor<HangFireConst> options) {
            _httpClientFactory = httpClientFactory;
            _options = options;
        }
        public async Task Run(int args) {
            var httpClient = _httpClientFactory.CreateClient();

            // 獲取所有指標來源URL
            var exporterUrls = _options.CurrentValue.ClientSetting!.ExporterUrls;

            if (exporterUrls == null || exporterUrls.Length == 0) {
                throw new Exception("未設定指標來源URL");
            }

            var failureCount = 0;
            var exceptionMessages = new List<string>();

            // 設定基本身份驗證 (如果有)
            if (_options.CurrentValue.BasicAuth != null && !string.IsNullOrWhiteSpace(_options.CurrentValue.BasicAuth.UserName)) {
                var byteArray = Encoding.ASCII.GetBytes($"{_options.CurrentValue.BasicAuth.UserName}:{_options.CurrentValue.BasicAuth.Password}");
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
            }

            // 構建URL參數
            var urlPamas = $"job/{_options.CurrentValue.ClientSetting.JobName}";
            urlPamas += $"/instance/{_options.CurrentValue.ClientSetting.Instance}";
            foreach (var label in _options.CurrentValue?.ClientSetting?.Labels ?? new Dictionary<string, string>()) {
                urlPamas += $"/{label.Key}/{label.Value}";
            }
            var ip = await GetIp();
            if (ip != null) {
                urlPamas += $"/ip/{ip}";
            }

            // 從每個來源抓取指標並分別推送
            foreach (var url in exporterUrls) {
                if (string.IsNullOrWhiteSpace(url)) continue;

                try {
                    // 獲取指標
                    var exporterResponse = await httpClient.GetStringAsync(url);
                    // LogHelper.WriteLog("metric_fetch", $"成功從 {url} 獲取指標");

                    // 直接推送該指標
                    using (var content = new StringContent(exporterResponse.Replace(Environment.NewLine, "\n"), Encoding.UTF8, "text/plain")) {
                        var resp = await httpClient.PostAsync($"{_options.CurrentValue.PushGatewayUrl}/metrics/{urlPamas}", content);
                        var code = resp.StatusCode;
                        var body = await resp.Content.ReadAsStringAsync();
                        if (code != System.Net.HttpStatusCode.OK) {
                            throw new Exception($"上傳指標失敗,來源: {url},錯誤: {body}");
                        }
                    }
                } catch (Exception ex) {
                    failureCount++;
                    var errorMessage = $"處理來源 {url} 的指標失敗: {ex.Message}";
                    LogHelper.WriteLog("metric_fetch_error", errorMessage);
                    exceptionMessages.Add(errorMessage);
                }
            }

            // 檢查是否所有來源都失敗了
            if (failureCount == exporterUrls.Length) {
                throw new Exception($"所有指標來源都失敗: {string.Join("; ", exceptionMessages)}");
            }
        }

        private async Task<string?> GetIp() {
            var url = "https://api.myip.com";
            var httpClient = _httpClientFactory.CreateClient();
            var res = await httpClient.GetFromJsonAsync<MyIpAddress>(url);
            if (res != null && !string.IsNullOrWhiteSpace(res.ip)) {
                return res.ip;
            }
            return null;
        }
    }
}
