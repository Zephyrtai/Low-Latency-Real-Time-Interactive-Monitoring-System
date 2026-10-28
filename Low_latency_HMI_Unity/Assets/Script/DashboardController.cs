using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using Zephyr.UnityLowLatencyHMI;
using Debug = UnityEngine.Debug;

/// <summary>
/// 
/// </summary>

[RequireComponent(typeof(UIDocument))] //
public class DashboardController : MonoBehaviour
{
    [SerializeField] private UDPReceiver receiver;
    [SerializeField, Range(1f, 60f)] private float uiUpdateRate = 30f;
    [SerializeField, Min(0.1f)] private float dataTimeoutSeconds = 1.5f;

    private static readonly List<string> FilterChoices = new List<string> { "Raw", "Median", "Median + LPF" };
    private static readonly List<string> RateChoices = new List<string> { "1", "5", "10", "15", "30", "60" };
    private Label rawValue;
    private Label outputValue;
    private Label graphScale;
    private Label alphaValue;
    private DropdownField filterDropdown;
    private DropdownField rateDropdown;
    private Slider alphaSlider;
    private int filterMode;
    private double nextRefreshTime;
    private DistanceGraph graph;
    private bool showingUnavailable;

    private void OnEnable()
    {
        VisualElement root = GetComponent<UIDocument>().rootVisualElement;
        rawValue = root.Q<Label>("Raw_value");
        outputValue = root.Q<Label>("Output_value");
        graphScale = root.Q<Label>("graph_scale");
        alphaValue = root.Q<Label>("Alpha_value");
        filterDropdown = root.Q<DropdownField>("Filter_dropdown");
        rateDropdown = root.Q<DropdownField>("UI_rate_dropdown");
        alphaSlider = root.Q<Slider>("Alpha_Slider");
        VisualElement graphHost = root.Q<VisualElement>("graph_host");

        if (receiver == null || rawValue == null || outputValue == null || graphScale == null
            || alphaValue == null || filterDropdown == null || rateDropdown == null
            || alphaSlider == null || graphHost == null)
        {
            Debug.LogError("Dashboard: assign UDPReceiver and check if updated DashboardUI.uxml.", this);
            enabled = false;
            return;
        }

        //**This is a UI element, not a GameObject component.**
        graph = new DistanceGraph();
        graphHost.Add(graph);
        filterDropdown.choices = new List<string>(FilterChoices);
        filterDropdown.SetValueWithoutNotify(FilterChoices[filterMode]);
        filterDropdown.RegisterValueChangedCallback(OnFilterChanged);

        alphaSlider.lowValue = 0.01f;
        alphaSlider.highValue = 1f;
        alphaSlider.SetValueWithoutNotify(receiver.FilterAlpha);
        alphaSlider.RegisterValueChangedCallback(OnAlphaChanged);
        UpdateAlphaControl();

        uiUpdateRate = Mathf.Clamp(uiUpdateRate, 1f, 60f);
        string currentRate = uiUpdateRate.ToString("R", CultureInfo.InvariantCulture);
        rateDropdown.choices = new List<string>(RateChoices);
        if (!rateDropdown.choices.Contains(currentRate)) rateDropdown.choices.Add(currentRate);
        rateDropdown.SetValueWithoutNotify(currentRate);
        rateDropdown.RegisterValueChangedCallback(OnRateChanged);

        rawValue.text = "-- cm";
        outputValue.text = "-- cm";
        graphScale.text = "Waiting for data";
        showingUnavailable = true;
        nextRefreshTime = Time.unscaledTimeAsDouble;
    }

    private void Update()
    {
        double now = Time.unscaledTimeAsDouble;
        if (now < nextRefreshTime) return;

        double interval = 1.0 / Mathf.Clamp(uiUpdateRate, 1f, 60f);
        nextRefreshTime += interval;
        //**After a long frame, skip missed refreshes rather than replaying them.**
        if (nextRefreshTime <= now) nextRefreshTime = now + interval;

        if (!receiver.isActiveAndEnabled || !receiver.TryGetLatestSample(out FilteredDistanceSample sample))
        {
            ShowUnavailable("Waiting for data");
            return;
        }
        double ageSeconds = (MetricsCollector.GetTimeStamp() - sample.ReceivedTicks) / (double)Stopwatch.Frequency;
        if (ageSeconds > dataTimeoutSeconds)
        {
            ShowUnavailable("No recent data");
            return;
        }
        if (!sample.IsValid)
        {
            ShowUnavailable("Invalid distance (expected 3-250 cm)");
            return;
        }

        if (showingUnavailable) graph.ClearSamples();
        showingUnavailable = false;
        float output = filterMode == 0 ? sample.RawDistance
            : filterMode == 1 ? sample.MedianDistance : sample.MedianLpfDistance;
        rawValue.text = sample.RawDistance.ToString("F2", CultureInfo.InvariantCulture) + " cm";
        outputValue.text = output.ToString("F2", CultureInfo.InvariantCulture) + " cm";
        graphScale.text = FilterChoices[filterMode] + " | 0-250 cm | Last 10 s";

        graph.AddSample(now, output);
    }

    private void ShowUnavailable(string message)
    {
        rawValue.text = "-- cm";
        outputValue.text = "-- cm";
        graphScale.text = message;
        if (!showingUnavailable) graph.ClearSamples();
        showingUnavailable = true;
    }

    private void OnFilterChanged(ChangeEvent<string> evt)
    {
        filterMode = FilterChoices.IndexOf(evt.newValue);
        if (filterMode < 0) filterMode = 0;
        graph.ClearSamples();
        UpdateAlphaControl();
        nextRefreshTime = Time.unscaledTimeAsDouble;
    }

    private void OnAlphaChanged(ChangeEvent<float> evt)
    {
        float alpha = Mathf.Clamp(Mathf.Round(evt.newValue * 100f) / 100f, 0.01f, 1f);
        alphaSlider.SetValueWithoutNotify(alpha);
        receiver.SetFilterAlpha(alpha);
        UpdateAlphaControl();
        graph.ClearSamples();
    }

    private void UpdateAlphaControl()
    {
        alphaValue.text = receiver.FilterAlpha.ToString("F2", CultureInfo.InvariantCulture);
        // Alpha affects only Median + LPF; all three branches still run continuously.
        alphaSlider.SetEnabled(filterMode == 2);
    }

    private void OnRateChanged(ChangeEvent<string> evt)
    {
        if (float.TryParse(evt.newValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float rate))
            uiUpdateRate = Mathf.Clamp(rate, 1f, 60f);
        nextRefreshTime = Time.unscaledTimeAsDouble;
    }

    private void OnDisable()
    {
        filterDropdown?.UnregisterValueChangedCallback(OnFilterChanged);
        rateDropdown?.UnregisterValueChangedCallback(OnRateChanged);
        alphaSlider?.UnregisterValueChangedCallback(OnAlphaChanged);
        graph?.RemoveFromHierarchy();
        graph = null;
    }
}