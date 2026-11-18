
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class RoboPilotScriptedRobot : MonoBehaviour
{
    [Header("Scene References")]
    public Transform robotBase;
    public Transform demoCube;
    public Collider targetZone;

    [Header("Movement")]
    public float actionDuration = 1.5f;
    public float graspHeight = 0.10f;
    public float liftHeight = 0.35f;
    public float armRadius = 0.055f;

    private Transform shoulder;
    private Transform elbow;
    private Transform wrist;
    private Transform gripper;
    private Transform upperArm;
    private Transform forearm;
    private Transform leftFinger;
    private Transform rightFinger;

    private Vector3 initialCubePosition;
    private Quaternion initialCubeRotation;

    private bool isRunning;
    private bool taskCompleted;
    private bool isHoldingCube;

    private float fingerOpening = 0.09f;
    private Coroutine activeRoutine;

    private const float FingerOpen = 0.09f;
    private const float FingerClosed = 0.025f;

    private Vector3 armBasePosition;

    private void Start()
    {
        if (demoCube == null)
        {
            GameObject cube = GameObject.Find("DemoObject");
            if (cube != null) demoCube = cube.transform;
        }

        if (targetZone == null)
        {
            GameObject target = GameObject.Find("TargetZone");
            if (target != null)
                targetZone = target.GetComponent<Collider>();
        }

        if (robotBase == null)
        {
            GameObject panda = GameObject.Find("panda_home");
            if (panda == null)
                panda = GameObject.Find("FrankaPanda_Articulated");

            if (panda != null)
                robotBase = panda.transform;
        }

        if (demoCube == null || targetZone == null)
        {
            Debug.LogError("RoboPilot: DemoObject or TargetZone missing.");
            enabled = false;
            return;
        }

        if (robotBase == null)
            robotBase = transform;

        initialCubePosition = demoCube.position;
        initialCubeRotation = demoCube.rotation;

        // Position the demonstration arm near the workspace,
        // independently of the Panda mesh pivot.
        armBasePosition = robotBase.position;

        CreateRobotArm();
        MoveArmTo(GetRestPosition());
        SetFingerOpening(FingerOpen);

        Debug.Log("RoboPilot: Visible scripted gripper initialized.");
    }

    private void Update()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.spaceKey.wasPressedThisFrame)
            ExecuteRobot();

        if (Keyboard.current.backspaceKey.wasPressedThisFrame)
            ResetRobot();
    }

    private Vector3 GetRestPosition()
    {
        return demoCube.position +
               Vector3.up * (liftHeight + graspHeight + 0.3f);
    }

    private void CreateRobotArm()
    {
        shoulder = CreateSphere("Demo_Shoulder", armRadius * 1.7f);
        elbow = CreateSphere("Demo_Elbow", armRadius * 1.5f);
        wrist = CreateSphere("Demo_Wrist", armRadius * 1.3f);
        gripper = CreateSphere("Demo_Gripper", armRadius * 1.5f);

        upperArm = CreateCylinder("Demo_UpperArm");
        forearm = CreateCylinder("Demo_Forearm");

        leftFinger = CreateCube(
            "Demo_LeftFinger",
            new Vector3(0.035f, 0.12f, 0.035f),
            new Color(0.1f, 0.85f, 1f)
        );

        rightFinger = CreateCube(
            "Demo_RightFinger",
            new Vector3(0.035f, 0.12f, 0.035f),
            new Color(0.1f, 0.85f, 1f)
        );

        SetMaterial(gripper.gameObject, Color.cyan);
    }

    private Transform CreateSphere(string objectName, float radius)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        obj.name = objectName;
        obj.transform.localScale = Vector3.one * radius * 2f;
        RemoveCollider(obj);
        SetMaterial(obj, new Color(0.35f, 0.38f, 0.42f));
        return obj.transform;
    }

    private Transform CreateCylinder(string objectName)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        obj.name = objectName;
        RemoveCollider(obj);
        SetMaterial(obj, new Color(0.55f, 0.57f, 0.60f));
        return obj.transform;
    }

    private Transform CreateCube(
        string objectName,
        Vector3 scale,
        Color color)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = objectName;
        obj.transform.localScale = scale;
        RemoveCollider(obj);
        SetMaterial(obj, color);
        return obj.transform;
    }

    private void RemoveCollider(GameObject obj)
    {
        Collider col = obj.GetComponent<Collider>();
        if (col != null) Destroy(col);
    }

    private void SetMaterial(GameObject obj, Color color)
    {
        Renderer r = obj.GetComponent<Renderer>();
        if (r != null) r.material.color = color;
    }

    private void SetLink(Transform link, Vector3 start, Vector3 end)
    {
        Vector3 direction = end - start;

        link.position = (start + end) * 0.5f;
        link.rotation = Quaternion.FromToRotation(
            Vector3.up, direction.normalized);

        link.localScale = new Vector3(
            armRadius,
            direction.magnitude * 0.5f,
            armRadius
        );
    }

    private void MoveArmTo(Vector3 target)
    {
        Vector3 shoulderPosition =
            armBasePosition + Vector3.up * 0.75f;

        Vector3 direction = target - shoulderPosition;

        float distance = Mathf.Max(0.05f, direction.magnitude);
        direction.Normalize();

        Vector3 elbowPosition =
            shoulderPosition + direction * (distance * 0.5f);

        shoulder.position = shoulderPosition;
        elbow.position = elbowPosition;
        wrist.position = target;
        gripper.position = target;

        SetLink(upperArm, shoulderPosition, elbowPosition);
        SetLink(forearm, elbowPosition, target);

        UpdateFingerPositions();
    }

    private void SetFingerOpening(float opening)
    {
        fingerOpening = opening;
        UpdateFingerPositions();
    }

    private void UpdateFingerPositions()
    {
        if (gripper == null) return;

        // Keep the two fingers visibly separated along the X axis.
        leftFinger.position =
            gripper.position + Vector3.left * fingerOpening;

        rightFinger.position =
            gripper.position + Vector3.right * fingerOpening;

        leftFinger.rotation = Quaternion.identity;
        rightFinger.rotation = Quaternion.identity;
    }

    public void ExecuteRobot()
    {
        if (isRunning || taskCompleted) return;

        if (activeRoutine != null)
            StopCoroutine(activeRoutine);

        activeRoutine = StartCoroutine(PickAndPlace());
    }

    private IEnumerator PickAndPlace()
    {
        isRunning = true;

        Vector3 cubePosition = demoCube.position;

        Renderer cubeRenderer = demoCube.GetComponent<Renderer>();
        float cubeHalfHeight = cubeRenderer != null
            ? cubeRenderer.bounds.extents.y
            : 0.05f;

        Vector3 approach = cubePosition +
            Vector3.up * (graspHeight + 0.25f);

        Vector3 grasp = cubePosition +
            Vector3.up * graspHeight;

        Vector3 lift = cubePosition +
            Vector3.up * (liftHeight + graspHeight);

        Bounds bounds = targetZone.bounds;

        Vector3 destination = new Vector3(
            bounds.center.x,
            bounds.max.y + cubeHalfHeight + graspHeight,
            bounds.center.z
        );

        // Approach.
        yield return MoveArmSmooth(approach, actionDuration);

        // Descend.
        yield return MoveArmSmooth(grasp, actionDuration * 0.6f);

        // Close fingers.
        SetFingerOpening(FingerClosed);
        yield return new WaitForSeconds(0.3f);

        // Attach cube to the moving gripper.
        isHoldingCube = true;

        // Lift.
        yield return MoveArmSmooth(lift, actionDuration);

        // Transfer.
        yield return MoveArmSmooth(
            destination + Vector3.up * 0.2f,
            actionDuration
        );

        // Lower.
        yield return MoveArmSmooth(destination, actionDuration * 0.6f);

        // Release.
        isHoldingCube = false;
        demoCube.position = gripper.position -
            Vector3.up * graspHeight;

        SetFingerOpening(FingerOpen);

        taskCompleted = true;
        isRunning = false;
        activeRoutine = null;

        Debug.Log("RoboPilot: Scripted pick-and-place completed.");
    }

    private IEnumerator MoveArmSmooth(Vector3 target, float duration)
    {
        Vector3 start = gripper.position;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / duration);
            t = Mathf.SmoothStep(0f, 1f, t);

            Vector3 position = Vector3.Lerp(start, target, t);

            MoveArmTo(position);

            if (isHoldingCube)
            {
                demoCube.position =
                    gripper.position - Vector3.up * graspHeight;
            }

            yield return null;
        }

        MoveArmTo(target);

        if (isHoldingCube)
        {
            demoCube.position =
                gripper.position - Vector3.up * graspHeight;
        }
    }

    public void ResetRobot()
    {
        if (activeRoutine != null)
        {
            StopCoroutine(activeRoutine);
            activeRoutine = null;
        }

        isRunning = false;
        taskCompleted = false;
        isHoldingCube = false;

        if (demoCube != null)
        {
            demoCube.SetParent(null);
            demoCube.position = initialCubePosition;
            demoCube.rotation = initialCubeRotation;
        }

        if (gripper != null)
        {
            MoveArmTo(GetRestPosition());
            SetFingerOpening(FingerOpen);
        }

        Debug.Log("RoboPilot: Robot demo reset.");
    }

    private void OnGUI()
    {
        float panelX = Screen.width - 265f;
        float panelY = 15f;

        GUI.Box(
            new Rect(panelX, panelY, 250, 145),
            "RoboPilot - Robot Demo"
        );

        GUI.enabled = !isRunning && !taskCompleted;

        if (GUI.Button(
            new Rect(panelX + 15, panelY + 35, 220, 35),
            "EXECUTE ROBOT"))
        {
            ExecuteRobot();
        }

        GUI.enabled = true;

        if (GUI.Button(
            new Rect(panelX + 15, panelY + 77, 220, 30),
            "RESET"))
        {
            ResetRobot();
        }

        string status = isRunning
            ? "EXECUTING"
            : taskCompleted
                ? "TASK COMPLETED"
                : "READY";

        GUI.Label(
            new Rect(panelX + 15, panelY + 112, 220, 25),
            "Status: " + status
        );
    }
}
