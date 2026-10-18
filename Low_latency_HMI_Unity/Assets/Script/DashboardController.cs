using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))] //
public class DashboardController : MonoBehaviour
{
    [SerializeField] private UDPReceiver receiver;
    [SerializeField, Min(1f)] private float uiUpdateRate = 30f;

    private Label rawValue;
    private Label outputValue;
    private float nextRefreshTime;

    private void OnEnable()
    {
        UIDocument document = GetComponent<UIDocument>();
        VisualElement root = document.rootVisualElement;

        rawValue = root.Q<Label>("Raw_value");
        outputValue = root.Q<Label>("Output_value");

        if (rawValue == null || outputValue == null)
        {
            Debug.LogError(
                "Dashboard: cannot find Raw_value or Output_value.", this);
            enabled = false;
            return;
        }

        if (receiver == null)
        {
            Debug.LogError(
                "Dashboard: assign the UDPReceiver in the Inspector.", this);
            enabled = false;
            return;
        }

        rawValue.text = "¡ª cm";
        outputValue.text = "¡ª cm";
        nextRefreshTime = 0f;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime) return;

        nextRefreshTime = Time.unscaledTime + 1f / Mathf.Max(1f, uiUpdateRate);

        if (!receiver.TryGetLatestDistance(out float distance)) return;

        string displayText = distance.ToString("F2", CultureInfo.InvariantCulture) + " cm";

        rawValue.text = displayText;

        //Raw mode for now: output equals the raw distance.
        outputValue.text = displayText;
    }
}