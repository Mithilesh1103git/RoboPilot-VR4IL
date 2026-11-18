
using UnityEngine;

public class PandaJointDriver : MonoBehaviour
{
    public enum JointType
    {
        Hinge,
        Slide
    }

    [Header("Joint")]
    public string JointName;
    public JointType Type;

    [Header("Joint Configuration")]
    public Vector3 Axis = Vector3.up;
    public float Minimum;
    public float Maximum;
    public bool Limited = true;
    public float HomePosition;

    [Header("Driven Transform")]
    public Transform DrivenBody;

    private Quaternion baseRotation;
    private Vector3 basePosition;
    private float currentValue;

    public float CurrentValue => currentValue;

    private void Awake()
    {
        CaptureBaseTransform();
        SetPosition(HomePosition);
    }

    public void Initialize()
    {
        CaptureBaseTransform();
        SetPosition(HomePosition);
    }

    private void CaptureBaseTransform()
    {
        baseRotation = transform.localRotation;

        if (DrivenBody != null)
            basePosition = DrivenBody.localPosition;

        currentValue = HomePosition;
    }

    public void SetPosition(float value)
    {
        if (Limited)
            value = Mathf.Clamp(value, Minimum, Maximum);

        currentValue = value;

        if (Type == JointType.Hinge)
        {
            transform.localRotation =
                baseRotation *
                Quaternion.AngleAxis(
                    value * Mathf.Rad2Deg,
                    Axis.normalized
                );
        }
        else if (Type == JointType.Slide)
        {
            if (DrivenBody != null)
            {
                DrivenBody.localPosition =
                    basePosition + Axis.normalized * value;
            }
        }
    }

    public void ResetToHome()
    {
        SetPosition(HomePosition);
    }
}