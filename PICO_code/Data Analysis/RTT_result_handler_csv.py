import csv
from statistics import mean, stdev
from pathlib import Path

def result_hander(csv_filename, rtt_filename):
    packet_ids = []
    receive_intervals = []

    with open(csv_filename, encoding="utf-8-sig", newline="") as file:
        reader = csv.DictReader(file)

        for row in reader:
            packet_id = row["packet_id"].strip()

            if packet_id.isdigit():
                packet_ids.append(int(packet_id))

                interval_text = row["receive_interval_ms"].strip()
                if interval_text:
                    receive_intervals.append(float(interval_text))

    print("First packet:", packet_ids[0])
    print("Last packet:", packet_ids[-1])
    print("Total:", len(packet_ids))
    print("Receive interval samples:", len(receive_intervals))
    print(f"Mean receive interval (ms): {mean(receive_intervals):.3f}")


    pico_by_id = {}

    with open(rtt_filename, encoding="utf-8-sig", newline="") as file:
        reader = csv.DictReader(file)

        for row in reader:
            packet_id = row["packet_id"].strip()

            if packet_id.isdigit():
                pico_by_id[int(packet_id)] = row


    matched_rows = []

    for packet_id in packet_ids:
        #if packet_id in pico_by_id:
        matched_rows.append(pico_by_id[packet_id])

    print("Matched packets:", len(matched_rows))
    print("First matched RTT (ms):", matched_rows[0]["rtt_ms"])


    rtt_values = []

    for row in matched_rows:
        if row["status"] == "ok" and row["rtt_ms"]:
            rtt_values.append(float(row["rtt_ms"]))

    print("RTT samples:", len(rtt_values))
    print(f"Mean RTT (ms): {mean(rtt_values):.3f}")
    print(f"RTT jitter (ms): {stdev(rtt_values):.3f}")


    first_packet_id = min(packet_ids)
    last_packet_id = max(packet_ids)
    timeouts = 0

    for packet_id, row in pico_by_id.items():
        if first_packet_id <= packet_id <= last_packet_id:
            if row["status"] in ("timeout", "late_ack"):
                timeouts += 1

    print("Timeouts:", timeouts)
    print("\n")

    return {
    "matched_packets": len(matched_rows),
    "mean_receive_interval_ms": mean(receive_intervals),
    "mean_rtt_ms": mean(rtt_values),
    "rtt_jitter_ms": stdev(rtt_values),
    "timeouts": timeouts,
    }

#unity_filename_csv = "metrics_20261105_170621_RTT_5Hz_01.csv"
#RTT_filename_csv = "rtt_5Hz_001.csv"
#
#result = result_hander(unity_filename_csv, RTT_filename_csv)

#print("Matched packets:", result["matched_packets"])
#print("Mean receive interval (ms):", result["mean_receive_interval_ms"])
#print(f"Mean RTT (ms): {result['mean_rtt_ms']:.3f}")
#print(f"RTT jitter (ms): {result['rtt_jitter_ms']:.3f}")
#print(f"Timeouts: {result['timeouts']}")


base_dir = Path(__file__).resolve().parent
# 如果原始 CSV 与 Python 文件位于同一目录
data_dir = base_dir

# 如果原始 CSV 位于单独的文件夹，例如 rtt_trials：
# data_dir = base_dir / "rtt_trials"

# Each tuple contains: frequency (Hz), trial number, Unity CSV, Pico CSV.
trials = [
    (5, 1, "metrics_20261105_170621_RTT_5Hz_01.csv", "rtt_5Hz_001.csv"),
    (5, 2, "metrics_20261105_170809_RTT_5Hz_02.csv", "rtt_5Hz_002.csv"),
    (5, 3, "metrics_20261105_171009_RTT_5Hz_03.csv", "rtt_5Hz_003.csv"),
    (5, 4, "metrics_20261106_121656_RTT_5Hz_04.csv", "rtt_5Hz_004.csv"),
    (5, 5, "metrics_20261106_125509_RTT_5Hz_05.csv", "rtt_5Hz_005.csv"),
    (10, 1, "metrics_20261105_171550_RTT_10Hz_01.csv", "rtt_10Hz_001.csv"),
    (10, 2, "metrics_20261105_171720_RTT_10Hz_02.csv", "rtt_10Hz_002.csv"),
    (10, 3, "metrics_20261105_171913_RTT_10Hz_03.csv", "rtt_10Hz_003.csv"),
    (10, 4, "metrics_20261106_125743_RTT_10Hz_04.csv", "rtt_10Hz_004.csv"),
    (10, 5, "metrics_20261106_130014_RTT_10Hz_05.csv", "rtt_10Hz_005.csv"),
]

results = []

for rate_hz, trial, unity_csv, pico_csv in trials:
    print(f"Processing {rate_hz} Hz, trial {trial}")
    # metrics = result_hander(unity_csv, pico_csv)
    metrics = result_hander(
    data_dir / unity_csv,
    data_dir / pico_csv
    )

    row = {"transmission_rate_hz": rate_hz, "trial": trial}
    row.update(metrics)
    results.append(row)

output_file = base_dir / "rtt_summary.csv"

with output_file.open("w", encoding="utf-8-sig", newline="") as file:
    writer = csv.DictWriter(file, fieldnames=list(results[0]))
    writer.writeheader()
    writer.writerows(results)

print(f"Saved {len(results)} trials to rtt_summary.csv")