import csv
from statistics import stdev, median
import re
from pathlib import Path


def analyze_static(csv_filename, a_entry_s):
    rows = []

    with open(csv_filename, encoding="utf-8-sig", newline="") as file:
        reader = csv.DictReader(file)

        for row in reader:
            if row["packet_id"].strip().isdigit():
                rows.append(row)

    first_time_ms = float(rows[0]["receive_since_start_ms"])

    window_start_s = a_entry_s + 5
    window_end_s = a_entry_s + 20

    raw_values = []
    median_values = []
    lpf_values = []

    for row in rows:
        time_s = (
            float(row["receive_since_start_ms"]) - first_time_ms
        ) / 1000

        if window_start_s <= time_s < window_end_s:
            if row["is_valid"] == "1":
                raw_values.append(float(row["raw_distance"]))
                median_values.append(float(row["median_distance"]))
                lpf_values.append(float(row["median_lpf_distance"]))
    
    # Standard deviation measures the degree of variation in a signal when the target is stationary 
    return {
        "samples": len(raw_values),
        "raw_sd_cm": stdev(raw_values),
        "median_sd_cm": stdev(median_values),
        "lpf_sd_cm": stdev(lpf_values),
    }



def detect_transitions(csv_filename, threshold_cm=150, consecutive=3):
    samples = []

    with open(csv_filename, encoding="utf-8-sig", newline="") as file:
        reader = csv.DictReader(file)

        first_time_ms = None

        for row in reader:
            packet_id_text = row["packet_id"].strip()

            if not packet_id_text.isdigit():
                continue

            receive_time_ms = float(row["receive_since_start_ms"])

            if first_time_ms is None:
                first_time_ms = receive_time_ms

            if row["is_valid"].strip() != "1":
                continue

            samples.append({
                "packet_id": int(packet_id_text),
                "time_s": (receive_time_ms - first_time_ms) / 1000,
                "raw_distance": float(row["raw_distance"]),
            })

    b_to_a_s = None
    a_to_b_s = None

    for i in range(len(samples) - consecutive + 1):
        group = samples[i:i + consecutive]

        packet_ids_are_consecutive = all(
            group[j]["packet_id"] == group[j - 1]["packet_id"] + 1
            for j in range(1, consecutive)
        )

        if not packet_ids_are_consecutive:
            continue

        # B to A: 3 consecutive and valid sample > 150 & take the timestamp of the first sample
        if b_to_a_s is None:
            if all(sample["raw_distance"] > threshold_cm for sample in group):
                b_to_a_s = group[0]["time_s"]
        # A to B: 3 consecutive and valid sample < 150 & take the timestamp of the first sample
        elif all(sample["raw_distance"] < threshold_cm for sample in group):
            a_to_b_s = group[0]["time_s"]
            break

    return {
        "b_to_a_s": b_to_a_s,
        "a_to_b_s": a_to_b_s,
    }



def calculate_response_time(
    csv_filename,
    transition_s,
    direction,
    signal_column
):
    samples = []

    with open(csv_filename, encoding="utf-8-sig", newline="") as file:
        reader = csv.DictReader(file)

        first_time_ms = None

        for row in reader:
            packet_id_text = row["packet_id"].strip()

            if not packet_id_text.isdigit():
                continue

            receive_time_ms = float(row["receive_since_start_ms"])

            if first_time_ms is None:
                first_time_ms = receive_time_ms

            if row["is_valid"].strip() != "1":
                continue

            samples.append({
                "time_s": (receive_time_ms - first_time_ms) / 1000,
                "raw_distance": float(row["raw_distance"]),
                "value": float(row[signal_column]),
            })

    initial_values = [
        sample["raw_distance"]
        for sample in samples
        if transition_s - 6 <= sample["time_s"] < transition_s - 1
    ]

    final_values = [
        sample["raw_distance"]
        for sample in samples
        if transition_s + 5 <= sample["time_s"] < transition_s + 10
    ]

    if not initial_values or not final_values:
        return None

    initial_level = median(initial_values)
    final_level = median(final_values)

    threshold_90 = initial_level + 0.9 * (
        final_level - initial_level
    )

    response_time_ms = None

    for sample in samples:
        time_s = sample["time_s"]
        value = sample["value"]

        if not transition_s <= time_s <= transition_s + 10:
            continue

        threshold_reached = (
            direction == "up" and value >= threshold_90
        ) or (
            direction == "down" and value <= threshold_90
        )

        if threshold_reached:
            response_time_ms = (
                time_s - transition_s
            ) * 1000
            break

    return {
        "initial_level_cm": initial_level,
        "final_level_cm": final_level,
        "threshold_90_cm": threshold_90,
        "response_time_ms": response_time_ms,
    }



def analyze_trial(csv_file, alpha, trial):
    transitions = detect_transitions(csv_file)
    up_s = transitions["b_to_a_s"]
    down_s = transitions["a_to_b_s"]

    if up_s is None or down_s is None:
        raise ValueError(f"{csv_file.name}: No complete B→A→B transformation was detected.")

    if up_s + 20 > down_s:
        raise ValueError(f"{csv_file.name}: The stationary window at Point A extends into the return phase.")

    row = {
        "alpha": alpha,
        "trial": trial,
        "source_file": csv_file.name,
    }
    row.update(analyze_static(csv_file, a_entry_s=up_s))

    for direction, transition_s in [("up", up_s), ("down", down_s)]:
        for label, column in [
            ("median", "median_distance"),
            ("lpf", "median_lpf_distance"),
        ]:
            response = calculate_response_time(
                csv_file, transition_s, direction, column
            )
            time_ms = None if response is None else response["response_time_ms"]

            field = f"{label}_{direction}_t90_ms"
            row[field] = time_ms

            if time_ms is None:
                print(f"Please check {field} of {csv_file.name}，it will be blank in CSV.")

    return row

# Find the folder which is the file in
base_dir = Path(__file__).resolve().parent
trials_dir = base_dir / "filtering_trials"
rows = []

for alpha in ("0.2", "0.5", "0.8"):
    files_by_trial = {}

    for csv_file in (trials_dir / alpha).glob("*.csv"):
        match = re.search(r"TEST(\d+)", csv_file.stem, re.IGNORECASE)
        if match is None:
            raise ValueError(f"Can not find the TEST number in filename：{csv_file.name}")

        trial = int(match.group(1))
        if trial in files_by_trial:
            raise ValueError(f"α={alpha} has duplicate Trial {trial}")

        files_by_trial[trial] = csv_file

    if set(files_by_trial) != {1, 2, 3, 4, 5}:
        raise ValueError(f"α={alpha} should have had Trial 1–5，find {sorted(files_by_trial)}")

    for trial, csv_file in sorted(files_by_trial.items()):
        rows.append(analyze_trial(csv_file, alpha, trial))

output_file = base_dir / "filtering_trial_summary.csv"
with output_file.open("w", encoding="utf-8-sig", newline="") as file:
    writer = csv.DictWriter(file, fieldnames=list(rows[0]))
    writer.writeheader()
    writer.writerows(rows)

print(f"Have written {len(rows)} round：{output_file.name}")