
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

public class VRObjectGrabber : MonoBehaviour
{
    [Header("VR Input")]
    public Transform RightHandAnchor;
    public bool EnableVRInput = true;

    [Header("Desktop Input")]
    public Camera DesktopCamera;
    public bool EnableDesktopMouseInput = true;

    [Header("Recording")]
    public float SampleInterval = 0.05f;

    [Header("Demo Interface")]
    public bool ShowDemoPanel = true;

    private Rigidbody body;
    private Collider objectCollider;

    private bool isVRGrabbing;
    private bool wasVRGrabbing;
    private bool isMouseDragging;
    private bool isRecording;
    private bool isReplaying;
    private bool manualRecording;

    private Vector3 handPositionOffset;
    private Quaternion handRotationOffset;

    private Plane dragPlane;
    private float recordingElapsed;
    private float sampleTimer;
    private float playbackElapsed;

    private Vector3 initialPosition;
    private Quaternion initialRotation;

    private string lastSavedPath = "No trajectory saved yet.";
    private string status = "IDLE";

    private readonly List<PoseSample> samples = new();

    private readonly Rect panelRect = new Rect(12, 12, 380, 340);

    [Serializable]
    public class PoseSample
    {
        public float time;
        public Vector3 position;
        public Quaternion rotation;
    }

    [Serializable]
    public class Trajectory
    {
        public string objectName;
        public List<PoseSample> samples;
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        objectCollider = GetComponent<Collider>();

        if (body != null)
            body.isKinematic = true;

        initialPosition = transform.position;
        initialRotation = transform.rotation;

        if (DesktopCamera == null)
            DesktopCamera = Camera.main;
    }

    private void Update()
    {
        HandleDesktopInput();
        HandleVRInput();
        HandleKeyboardInput();

        if (isRecording)
            UpdateRecording();

        if (isReplaying)
            UpdateReplay();
    }

    private bool IsPointerOverPanel()
    {
        if (!ShowDemoPanel || Mouse.current == null)
            return false;

        Vector2 mouse = Mouse.current.position.ReadValue();

        Vector2 guiPosition = new Vector2(
            mouse.x,
            Screen.height - mouse.y
        );

        return panelRect.Contains(guiPosition);
    }

    private void HandleDesktopInput()
    {
        if (!EnableDesktopMouseInput ||
            Mouse.current == null ||
            DesktopCamera == null ||
            isReplaying)
            return;

        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Ray ray = DesktopCamera.ScreenPointToRay(mousePosition);

        if (Mouse.current.leftButton.wasPressedThisFrame &&
            !IsPointerOverPanel())
        {
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                bool hitThisObject =
                    hit.transform == transform ||
                    hit.transform.IsChildOf(transform);

                if (hitThisObject)
                {
                    dragPlane = new Plane(
                        Vector3.up,
                        new Vector3(0f, transform.position.y, 0f)
                    );

                    isMouseDragging = true;
                    isVRGrabbing = false;

                    if (!manualRecording)
                        StartRecording();
                }
            }
        }

        if (isMouseDragging &&
            Mouse.current.leftButton.isPressed)
        {
            if (dragPlane.Raycast(ray, out float distance))
            {
                Vector3 point = ray.GetPoint(distance);

                transform.position = new Vector3(
                    point.x,
                    transform.position.y,
                    point.z
                );
            }
        }

        if (isMouseDragging &&
            Mouse.current.leftButton.wasReleasedThisFrame)
        {
            isMouseDragging = false;

            if (!manualRecording)
                StopRecording();
        }
    }

    private void HandleVRInput()
    {
        if (!EnableVRInput ||
            RightHandAnchor == null ||
            isReplaying)
            return;

        bool grabbing = OVRInput.Get(
            OVRInput.Button.PrimaryHandTrigger,
            OVRInput.Controller.RTouch
        );

        if (grabbing && !wasVRGrabbing)
            StartVRGrab();

        if (grabbing && isVRGrabbing)
            FollowHand();

        if (!grabbing && wasVRGrabbing)
            StopVRGrab();

        wasVRGrabbing = grabbing;
    }

    private void StartVRGrab()
    {
        handPositionOffset =
            transform.position - RightHandAnchor.position;

        handRotationOffset =
            Quaternion.Inverse(RightHandAnchor.rotation) *
            transform.rotation;

        isVRGrabbing = true;

        if (!manualRecording)
            StartRecording();
    }

    private void FollowHand()
    {
        transform.position =
            RightHandAnchor.position + handPositionOffset;

        transform.rotation =
            RightHandAnchor.rotation * handRotationOffset;
    }

    private void StopVRGrab()
    {
        isVRGrabbing = false;

        if (!manualRecording)
            StopRecording();
    }

    private void HandleKeyboardInput()
    {
        if (Keyboard.current == null)
            return;

        if (Keyboard.current.pKey.wasPressedThisFrame)
        {
            if (isRecording)
            {
                manualRecording = false;
                StopRecording();
            }

            StartReplay();
        }

        if (Keyboard.current.rKey.wasPressedThisFrame)
            ResetObject();
    }

    public void StartManualRecording()
    {
        if (isRecording || isReplaying)
            return;

        manualRecording = true;
        StartRecording();
    }

    public void StopManualRecording()
    {
        manualRecording = false;
        isMouseDragging = false;
        StopRecording();
    }

    private void StartRecording()
    {
        if (isRecording || isReplaying)
            return;

        samples.Clear();

        recordingElapsed = 0f;
        sampleTimer = 0f;
        isRecording = true;
        status = "RECORDING";

        AddSample();

        Debug.Log("RoboPilot: Recording started.");
    }

    private void UpdateRecording()
    {
        recordingElapsed += Time.deltaTime;
        sampleTimer += Time.deltaTime;

        if (sampleTimer >= SampleInterval)
        {
            sampleTimer = 0f;
            AddSample();
        }
    }

    private void AddSample()
    {
        samples.Add(new PoseSample
        {
            time = recordingElapsed,
            position = transform.position,
            rotation = transform.rotation
        });
    }

    private void StopRecording()
    {
        if (!isRecording)
            return;

        AddSample();
        isRecording = false;

        SaveTrajectory();

        status = "SAVED";

        Debug.Log(
            $"RoboPilot: Recording stopped. Samples: {samples.Count}"
        );
    }

    private void SaveTrajectory()
    {
        if (samples.Count < 2)
        {
            Debug.LogWarning("RoboPilot: Not enough samples to save.");
            return;
        }

        string directory = Path.Combine(
            Application.persistentDataPath,
            "RoboPilot",
            "Trajectories"
        );

        Directory.CreateDirectory(directory);

        string filename =
            "trajectory_" +
            DateTime.Now.ToString("yyyyMMdd_HHmmss") +
            ".json";

        string path = Path.Combine(directory, filename);

        Trajectory trajectory = new Trajectory
        {
            objectName = gameObject.name,
            samples = new List<PoseSample>(samples)
        };

        File.WriteAllText(
            path,
            JsonUtility.ToJson(trajectory, true)
        );

        lastSavedPath = path;

        Debug.Log("RoboPilot: Trajectory saved to " + path);
    }

    public void StartReplay()
    {
        if (isRecording || samples.Count < 2)
        {
            Debug.LogWarning(
                "RoboPilot: Record a demonstration before replaying."
            );
            return;
        }

        isMouseDragging = false;
        isVRGrabbing = false;

        playbackElapsed = 0f;
        isReplaying = true;
        status = "REPLAYING";

        Debug.Log("RoboPilot: Replay started.");
    }

    private void UpdateReplay()
    {
        playbackElapsed += Time.deltaTime;

        if (playbackElapsed >= samples[samples.Count - 1].time)
        {
            transform.position = samples[samples.Count - 1].position;
            transform.rotation = samples[samples.Count - 1].rotation;

            isReplaying = false;
            status = "SAVED";

            Debug.Log("RoboPilot: Replay completed.");
            return;
        }

        int index = 0;

        while (index < samples.Count - 2 &&
               samples[index + 1].time < playbackElapsed)
        {
            index++;
        }

        PoseSample a = samples[index];
        PoseSample b = samples[index + 1];

        float duration = b.time - a.time;
        float t = duration > 0f
            ? (playbackElapsed - a.time) / duration
            : 0f;

        transform.position = Vector3.Lerp(
            a.position,
            b.position,
            t
        );

        transform.rotation = Quaternion.Slerp(
            a.rotation,
            b.rotation,
            t
        );
    }

    public void ResetObject()
    {
        isRecording = false;
        isReplaying = false;
        isMouseDragging = false;
        isVRGrabbing = false;
        manualRecording = false;

        transform.position = initialPosition;
        transform.rotation = initialRotation;

        TargetZoneDetector detector =
            GetComponent<TargetZoneDetector>();

        if (detector != null)
            detector.ResetTask();

        status = "IDLE";

        Debug.Log("RoboPilot: Object and task reset.");
    }

    private void OnGUI()
    {
        if (!ShowDemoPanel)
            return;

        GUI.Box(panelRect, "");

        GUILayout.BeginArea(
            new Rect(25, 20, 355, 325)
        );

        GUILayout.Label("ROBOPILOT | VR4IL");
        GUILayout.Label("Demonstration Capture System");
        GUILayout.Space(8);

        GUILayout.Label("STATUS: " + status);
        GUILayout.Label("SAMPLES: " + samples.Count);

        float duration = samples.Count > 0
            ? samples[samples.Count - 1].time
            : 0f;

        GUILayout.Label(
            "DURATION: " + duration.ToString("F2") + " sec"
        );

        GUILayout.Space(10);

        GUI.enabled = !isRecording && !isReplaying;
        if (GUILayout.Button("RECORD DEMONSTRATION", GUILayout.Height(30)))
            StartManualRecording();

        GUI.enabled = isRecording;
        if (GUILayout.Button("STOP AND SAVE", GUILayout.Height(30)))
            StopManualRecording();

        GUI.enabled = !isRecording &&
                      !isReplaying &&
                      samples.Count >= 2;

        if (GUILayout.Button("REPLAY DEMONSTRATION", GUILayout.Height(30)))
            StartReplay();

        GUI.enabled = !isRecording && !isReplaying;
        if (GUILayout.Button("RESET OBJECT", GUILayout.Height(25)))
            ResetObject();

        GUI.enabled = true;

        GUILayout.Space(6);
        GUILayout.Label("LATEST DATASET:");

        string shortPath = lastSavedPath;

        if (shortPath.Length > 55)
            shortPath = "..." + shortPath.Substring(shortPath.Length - 52);

        GUILayout.Label(shortPath);

        GUILayout.EndArea();
    }
}
