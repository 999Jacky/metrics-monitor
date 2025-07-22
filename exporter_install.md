#### 服務安裝

1. 下載exe執行檔 [github](https://github.com/prometheus-community/windows_exporter)
   * 推薦用[nssm](https://nssm.cc/download)處理以下設定,有gui可以使用,要使用pre-release版
   ```Shell
   nssm install windows_exporter 
   參數要填上 --config.file=D:\CarIN\pushGateWay\File\windowsExporterSetting.yaml
   nssm start windows_exporter
   ```
4. http://localhost:9182/metrics 確認服務啟動

+ 移除服務(要使用管理員權限)
```shell
nssm remove windows_exporter
```

#### 密碼
* 找線上bcrypt產生器,輸入密碼後貼到 Auth.yaml
``` text
basic_auth_users:
  userName1: 產生hash
  userName2: 產生hash2
```
* 這裡的密碼是要填到 prometheus的yaml設定檔裡,hash貼進windows_exporter設定檔裡


