# PushGateway

### 使用場景

Prometheus 無法直接連線到目標抓取指標,
統一讓目標主動Push指標到gateway上,
Prometheus再從gateway上抓取所有指標.

所有的指標**不會**逾時,需要手動刪除(官方社群表示現在,未來不會增加此功能),避免裝置離線了,Prometheus一直抓到舊的值

### server端安裝

* 下載執行檔 [PushGateway](https://github.com/prometheus/pushgateway)
* 透過nssm 安裝windows服務

### 目標裝置安裝

* Label設定接在/job/{jobLabel}/labelName/labelValue後面(需要encode)
* 透過windows排程設定呼叫cmd,但是執行時會彈出cmd的視窗

```cmd push.bat
@echo off
set METRICS_URL=http://localhost:9182/metrics
set PUSHGATEWAY_URL=http://localhost:9091/metrics/job/12345/instance/PC

curl %METRICS_URL% | curl --data-binary @- %PUSHGATEWAY_URL%
```

+ 透過程式(hangfire)
  + 預設從已安裝exporter轉送指標

### 刪除PushGateway過時指標

* 透過windows排程呼叫python檔案來自動刪除
  參考[ClearJob.ps1](Script/ClearJob.ps1)

* golang寫的執行檔
  build
  ```shell
  go build
  # 跨平臺編譯可以用gox
  gox -osarch="linux/amd64"
  ```
  參考[main.go](./ClearTimeout/main.go),在執行檔傳入以下參數
  
  + -url pushGateway url
  + -timeout 逾時時間

