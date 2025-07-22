# https://gist.githubusercontent.com/kubosuke/c8bbc83367d1084ae26e07bc2487e0d5/raw/828a4ef2d93a5ae5f620f74f32efa1c994cc8a10/delete-stale-metrics-from-pushgateway.py
# python -m pip install requests prometheus-client
from datetime import datetime, timedelta
import os
import requests
from prometheus_client.parser import text_string_to_metric_families
from requests.auth import HTTPBasicAuth

PUSHGATEWAY_METRICS_URL = ""
BASIC_AUTH_USERNAME = ""
BASIC_AUTH_PASSWORD = ""


PUSH_TIME_METRICS_NAME = "push_time_seconds"


def is_metrics_stale(timestamp: float, retention_period_minete: int = 1) -> bool:
    """Return True if the metrics is stale.

    Args:
        timestamp (float): epoch timestamp in prom format. ex: 1701841866.00605802

    Returns:
        bool: True if the stale than RETENTION_PERIOD_DAYS(days).
    """
    return datetime.fromtimestamp(timestamp) < datetime.now() - timedelta(minutes=retention_period_minete)


if __name__ == "__main__":
    auth = HTTPBasicAuth(BASIC_AUTH_USERNAME, BASIC_AUTH_PASSWORD)
    with requests.session() as session:
        session.auth = auth
        metrics_list_response = session.get(PUSHGATEWAY_METRICS_URL)
        metrics_list_response.raise_for_status()
        for family in text_string_to_metric_families(metrics_list_response.text):
            for sample in family.samples:
                if sample.name.startswith(PUSH_TIME_METRICS_NAME):
                    if is_metrics_stale(sample.value):
                        # 構建基本 URL 確保 job 優先，使用小寫標籤名稱
                        url = f"{PUSHGATEWAY_METRICS_URL}/job/{sample.labels['job']}"

                        for label_name, label_value in sample.labels.items():
                            if label_name.lower() not in ['job']:
                                url += f"/{label_name}/{label_value}"

                        res = session.delete(url)
                        res.raise_for_status()
                        print(f"DELETE {url}")