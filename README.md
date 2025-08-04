# Prometheus & windowsExporter & PushGateway

## 需求

收集&監控Windows PC上各個指標(ram,disk...等)

## 地端部屬步驟

1. 目標電腦部署windows_export([readme](./exporter_install.md))
2. 再部屬smartctl_exporter([repo](https://github.com/prometheus-community/smartctl_exporter))
3. 部屬hangfire專案(從各個exporter取得指標後上傳)

### server部屬(victoriametrics)
> vm原生支援主動push指標
> 
> 地端指標request -> nginx(:3001) -> vm(:8428)
> 
> 透過nginx把原始地端push的url替換成vm的路徑,並加上basic auth
1. [docker compose](./DockerCompose) 修改{ip}為主機ip
2. docker compose up
3. (選用)node_exporter + vmAgent取自身指標

### server部屬(pushgateway + prometheus)
> 使用pushgateway + prometheus 透過pushgateway暫存所有指標再讓prom去抓,但暫存所有指標會消耗大量記憶體
1. 安裝Prometheus
    * 透過docker
    * 注意會是prom主動去打目標電腦的api去撈資料,所以設定--net設成host方便測試,測試完成可以加上-d或改用docker compose
    ```shell
    docker run \
        -p 9090:9090 \
        -v /home/jacky/dockerCmd/prometheus/config/:/etc/prometheus/ \
        -v /home/jacky/dockerCmd/prometheus/data/:/prometheus \
        --name prom \
        --net=host \
        --rm \
      prom/prometheus
    ````
2. 設定prom(參考[yaml](./prom/prometheus.yml))
    + 這裡有兩種設定方法
        1. 共用job,只要把ip寫在另一個文件上[scrape_local.yaml](./prom/scrape_local.yml)
            ``` yaml
            scrape_configs:
            - job_name: "prometheus"
              basic_auth:
                  username: "user"
                  password: "userpwd"
              file_sd_configs:
                - files:
                    - /etc/prometheus/scrape*.yml
                  refresh_interval: 10m
            ```
        2. 分開job,可以更詳細設定[job_local.yaml](./prom/job_local.yml)
            * 但有修改時,要重啟prometheus服務重新讀取
            ``` yaml
            scrape_config_files:
                - /etc/prometheus/job*.yml
            ```
3. 重啟prometheus服務重新讀取(或是透過hot reload)
4. 根據需求設定[Gateway](./pushGateway.md)

在Target會看到有endpoint了
![prom1.png](img/prom1.png)
在grafana中找一個支援windowsExporter的dashboard匯入就可以看到各項指標
![grafana.png](img/grafana.png)