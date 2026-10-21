using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 
/// </summary>

[RequireComponent(typeof(UIDocument))] //
public class DashboardController : MonoBehaviour
{
    [SerializeField] private UDPReceiver receiver;
    [SerializeField, Range(1f, 60f)] private float uiUpdateRate = 30f;

    private Label rawValue;
    private Label outputValue;
    private double nextRefreshTime;
    private DistanceGraph graph;

    private void OnEnable()
    {
        UIDocument document = GetComponent<UIDocument>();
        VisualElement root = document.rootVisualElement;

        rawValue = root.Q<Label>("Raw_value");
        outputValue = root.Q<Label>("Output_value");
        VisualElement graphHost = root.Q<VisualElement>("graph_host");

        if (rawValue == null || outputValue == null || graphHost == null)
        {
            Debug.LogError("Dashboard: cannot find Raw_value, Output_value or graph_host.", this);
            enabled = false;
            return;
        }

        if (receiver == null)
        {
            Debug.LogError("Dashboard: assign the UDPReceiver in the Inspector.", this);
            enabled = false;
            return;
        }

        // This is a UI element, not a GameObject component.
        graph = new DistanceGraph();
        graphHost.Add(graph);

        rawValue.text = "¡ª cm";
        outputValue.text = "¡ª cm";
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

        if (!receiver.TryGetLatestDistance(out float distance)) return;
        if (float.IsNaN(distance) || float.IsInfinity(distance)) return;

        string displayText = distance.ToString("F2", CultureInfo.InvariantCulture) + " cm";
        rawValue.text = displayText;

        // Raw mode for now: output equals the raw distance.
        outputValue.text = displayText;

        // One display sample per UI refresh, not one sample per UDP packet.
        graph.AddSample(now, distance);
    }

    private void OnDisable()
    {   // Avoid duplicate graphs when this component is enabled again.
        if (graph == null) return;
        graph.RemoveFromHierarchy();
        graph = null;
    }
}