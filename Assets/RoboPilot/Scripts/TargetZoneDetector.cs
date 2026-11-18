
using UnityEngine;

public class TargetZoneDetector : MonoBehaviour
{
    [Header("Task Configuration")]
    public Collider TargetZone;
    public Collider DemoObjectCollider;

    [Header("Task Status")]
    public bool TaskCompleted { get; private set; }

    private void Update()
    {
        if (TaskCompleted ||
            TargetZone == null ||
            DemoObjectCollider == null)
            return;

        if (TargetZone.bounds.Intersects(DemoObjectCollider.bounds))
        {
            TaskCompleted = true;

            Debug.Log("RoboPilot: TASK COMPLETED - Object reached target zone!");
        }
    }

    private void OnGUI()
    {
        if (!TaskCompleted)
            return;

        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.fontSize = 22;
        style.fontStyle = FontStyle.Bold;
        style.normal.textColor = Color.green;
        style.alignment = TextAnchor.MiddleCenter;

        GUI.Box(
            new Rect(Screen.width - 310, 20, 290, 55),
            "TASK COMPLETED!",
            style
        );
    }

    public void ResetTask()
    {
        TaskCompleted = false;
    }
}
