using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using Hangfire;
using hangfire.Job;
using Hangfire.Storage;
using Microsoft.Extensions.Options;

namespace hangfire.Model {
    public class HangFireConst {
        [Required]
        public string PushGatewayUrl { get; set; }

        public BasicAuth? BasicAuth { get; set; }

        public ClientSetting? ClientSetting { get; set; }

        public ServiceLivenessCheck? ServiceLivenessCheck { get; set; }

        // 保留舊屬性以向後兼容
        [Obsolete("使用 ServiceLivenessCheck 代替")]
        public ServiceLivenessCheck? ServiceHealthCheck { 
            get => ServiceLivenessCheck; 
            set => ServiceLivenessCheck = value; 
        }
    }

    public class ServiceLivenessCheck {
        public string CheckCron { get; set; } = "*/5 * * * *";
        public List<ServiceInfo> Services { get; set; } = new List<ServiceInfo>();

        // 儲存每個服務的失敗計數，key為ServiceName:Url
        [NonSerialized]
        private ConcurrentDictionary<string, ServiceFailureInfo> _failureCounters = new ConcurrentDictionary<string, ServiceFailureInfo>();

        // 獲取服務失敗信息
        public ServiceFailureInfo GetFailureInfo(string serviceName, string url) {
            string key = $"{serviceName}:{url}";
            if (!_failureCounters.ContainsKey(key)) {
                _failureCounters[key] = new ServiceFailureInfo();
            }
            return _failureCounters[key];
        }

        // 重置所有服務的失敗計數
        public void ResetAllFailureCounts() {
            foreach (var key in _failureCounters.Keys.ToList()) {
                _failureCounters[key].FailureCount = 0;
                _failureCounters[key].LastResetTime = DateTime.Now;
            }
        }
    }

    public class ServiceFailureInfo {
        public int FailureCount { get; set; } = 0;
        public DateTime LastResetTime { get; set; } = DateTime.Now;
        public DateTime? LastFailureTime { get; set; } = null;
    }

    public class ServiceInfo {
        public string ServiceName { get; set; }
        public string Url { get; set; }
    }
    public class ClientSetting {
        public string JobName { get; set; }
        public string Instance { get; set; }

        public string PushJobCron { get; set; }
        public string[] ExporterUrls { get; set; }
        public string? ExecFile { get; set; }
        public string? ExecArgs { get; set; }
        public Dictionary<string, string>? Labels { get; set; }
    }

    public class BasicAuth {
        public string UserName { get; set; }
        public string Password { get; set; }
    }

    public class OnHangFireSettingChange {
        private readonly JobStorage _jobStorage;
        private readonly IDisposable? _onChangeToken;

        // private readonly StartupService _startupService;
        public OnHangFireSettingChange(IOptionsMonitor<HangFireConst> constMonitor, JobStorage jobStorage) {
            _jobStorage = jobStorage;
            // _startupService = startupService;
            _onChangeToken = constMonitor.OnChange(OnChange);
            this.RegisterJob(constMonitor.CurrentValue);
        }

        private void OnChange(HangFireConst option) {
            foreach (var job in _jobStorage.GetConnection().GetRecurringJobs()) {
                RecurringJob.RemoveIfExists(job.Id);
            }
            // _ = _startupService.Restart();
            RegisterJob(option);
        }

        private void RegisterJob(HangFireConst option) {
            if (option.ClientSetting == null) {
                throw new Exception("缺少Client設定");
            }
            AddJob<PushMetricToServer, int>(option.ClientSetting.PushJobCron, 0);

            // 註冊服務存活檢查工作
            if (option.ServiceLivenessCheck != null) {
                AddJob<ServiceLivenessCheckJob, int>(option.ServiceLivenessCheck.CheckCron, 0);

                // 每天凌晨0點重置所有服務的失敗計數
                RecurringJob.AddOrUpdate("ResetServiceFailureCounters", 
                    () => option.ServiceLivenessCheck.ResetAllFailureCounts(), 
                    "0 0 * * *", // 每天凌晨0點執行
                    new RecurringJobOptions() {
                        TimeZone = TimeZoneInfo.Local
                    });
            }
        }

        private void AddJob<T, Targs>(string cron, Targs args) where T : IJob<Targs> {
            var jobName = typeof(T).Name;
            RecurringJob.AddOrUpdate<T>(jobName, j => j.Run(args), cron, new RecurringJobOptions() {
                TimeZone = TimeZoneInfo.Local
            });
        }
    }
}
