package main

import (
	"flag"
	"fmt"
	"github.com/prometheus/common/expfmt"
	"log"
	"net/http"
	"time"
)

const (
	pushTimeMetricsName = "push_time_seconds"
)

func isMetricsStale(timestamp float64, retentionPeriodMinutes int) bool {
	metricsTime := time.Unix(int64(timestamp), 0)
	return metricsTime.Before(time.Now().Add(-time.Duration(retentionPeriodMinutes) * time.Minute))
}

func main() {
	pushGatewayUrl := flag.String("url", "", "The URL of the pushgateway metrics")
	retentionPeriodMinutes := flag.Int("timeout", 60, "Retention period in minutes (default 1 hour)")
	userName := flag.String("username", "", "Username to authenticate to push metrics")
	password := flag.String("password", "", "Password to authenticate to push metrics")
	flag.Parse()

	if *pushGatewayUrl == "" {
		log.Fatal("URL is required")
	}

	client := &http.Client{
		Timeout: time.Duration(1) * time.Minute, // Request timeout set to 10 minutes
	}

	req, err := http.NewRequest("GET", *pushGatewayUrl+"/metrics", nil)
	if err != nil {
		log.Fatal(err)
	}
	if *userName != "" {
		req.SetBasicAuth(*userName, *password)
	}

	resp, err := client.Do(req)
	if err != nil {
		log.Fatal(err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		log.Fatalf("Unexpected status code: %d", resp.StatusCode)
	}

	var parser expfmt.TextParser
	metricFamilies, err := parser.TextToMetricFamilies(resp.Body)
	if err != nil {
		log.Fatalf("Failed to parse metrics: %v", err)
	}

	for _, family := range metricFamilies {
		if family.GetName() == pushTimeMetricsName {
			for _, metric := range family.Metric {
				timestamp := metric.GetGauge().GetValue()
				if isMetricsStale(timestamp, *retentionPeriodMinutes) {
					job := ""
					deleteURL := *pushGatewayUrl + "/metrics"

					for _, lbl := range metric.Label {
						if lbl.GetName() == "job" {
							job = lbl.GetValue()
							deleteURL = fmt.Sprintf("%s/job/%s", deleteURL, job)
						}
					}

					for _, lbl := range metric.Label {
						if lbl.GetName() != "job" {
							deleteURL = fmt.Sprintf("%s/%s/%s", deleteURL, lbl.GetName(), lbl.GetValue())
						}
					}

					req, err := http.NewRequest("DELETE", deleteURL, nil)
					if err != nil {
						log.Fatalf("Failed to create DELETE request: %v", err)
					}
					if *userName != "" {
						req.SetBasicAuth(*userName, *password)
					}
					
					delResp, err := client.Do(req)
					if err != nil {
						log.Fatalf("Failed to delete stale metrics: %v", err)
					}
					defer delResp.Body.Close()

					if delResp.StatusCode != 202 {
						log.Fatalf("Failed to delete metrics, unexpected status code: %d", delResp.StatusCode)
					}

					fmt.Printf("DELETE %s\n", deleteURL)
				}
			}
		}
	}
}
