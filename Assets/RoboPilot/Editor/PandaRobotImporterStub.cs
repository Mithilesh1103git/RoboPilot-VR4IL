
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PandaRobotImporterStub
{
    private const string RobotFolder =
        "Assets/RoboPilot/Robot/FrankaPanda";

    private const string XmlPath =
        RobotFolder + "/Source/panda.xml";

    private const string MeshFolder =
        RobotFolder + "/Meshes";

    private const string MaterialFolder =
        "Assets/RoboPilot/Materials/Generated";

    private const string PrefabFolder =
        "Assets/RoboPilot/Prefabs";

    // MuJoCo Z-up to Unity Y-up.
    // Apply this once at the robot root.
    private static readonly Quaternion MuJoCoToUnity =
        Quaternion.Euler(-90f, 0f, 0f);

    private static readonly Dictionary<string, Mesh> MeshCache =
        new Dictionary<string, Mesh>();

    private static readonly Dictionary<string, Material> MaterialCache =
        new Dictionary<string, Material>();

    private static readonly Dictionary<string, float> HomePositions =
        new Dictionary<string, float>();

    private static Vector3 defaultPandaAxis =
        new Vector3(0f, 0f, 1f);

    private static Vector3 defaultFingerAxis =
        new Vector3(0f, 1f, 0f);

    private static string defaultPandaJointType = "hinge";
    private static string defaultFingerJointType = "slide";

    [MenuItem("RoboPilot/Import/Build Franka Panda Visual Model")]
    public static void BuildPanda()
    {
        if (!File.Exists(XmlPath))
        {
            Debug.LogError("Panda XML not found: " + XmlPath);
            return;
        }

        MeshCache.Clear();
        MaterialCache.Clear();
        HomePositions.Clear();

        GameObject robotRoot = null;

        try
        {
            EnsureFolder(MaterialFolder);
            EnsureFolder(PrefabFolder);

            XmlDocument document = new XmlDocument();
            document.Load(XmlPath);

            XmlNode worldBody =
                document.SelectSingleNode("/mujoco/worldbody");

            if (worldBody == null)
                throw new Exception("Missing MuJoCo worldbody.");

            XmlNode pandaBody = null;

            foreach (XmlNode child in worldBody.ChildNodes)
            {
                if (child.Name == "body" &&
                    GetAttribute(child, "name") == "link0")
                {
                    pandaBody = child;
                    break;
                }
            }

            if (pandaBody == null)
                throw new Exception("Could not find body link0.");

            ReadDefaultJointSettings(document);
            ReadHomeKeyframe(document);
            LoadMaterials(document);

            robotRoot = new GameObject("FrankaPanda");

            // Coordinate conversion happens once, at the root.
            robotRoot.transform.rotation = MuJoCoToUnity;

            BuildBody(pandaBody, robotRoot.transform);

            string prefabPath =
                PrefabFolder + "/FrankaPanda.prefab";

            PrefabUtility.SaveAsPrefabAsset(
                robotRoot,
                prefabPath
            );

            Selection.activeGameObject = robotRoot;

            EditorSceneManager.MarkSceneDirty(robotRoot.scene);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "Panda import completed. " +
                "Home keyframe joints loaded: " +
                HomePositions.Count +
                ". Prefab: " + prefabPath
            );
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);

            if (robotRoot != null)
                UnityEngine.Object.DestroyImmediate(robotRoot);
        }
    }

    private static void ReadDefaultJointSettings(
        XmlDocument document)
    {
        XmlNode pandaDefault =
            document.SelectSingleNode(
                "/mujoco/default/default[@class='panda']"
            );

        if (pandaDefault != null)
        {
            XmlNode joint = pandaDefault.SelectSingleNode("joint");

            if (joint != null)
            {
                defaultPandaAxis = ParseVector3(
                    GetAttribute(joint, "axis"),
                    defaultPandaAxis
                );

                defaultPandaJointType =
                    GetAttribute(joint, "type", "hinge");
            }

            XmlNode fingerDefault =
                pandaDefault.SelectSingleNode(
                    "default[@class='finger']/joint"
                );

            if (fingerDefault != null)
            {
                defaultFingerAxis = ParseVector3(
                    GetAttribute(fingerDefault, "axis"),
                    defaultFingerAxis
                );

                defaultFingerJointType =
                    GetAttribute(fingerDefault, "type", "slide");
            }
        }
    }

    private static void ReadHomeKeyframe(
        XmlDocument document)
    {
        XmlNode home =
            document.SelectSingleNode(
                "/mujoco/keyframe/key[@name='home']"
            );

        if (home == null)
        {
            Debug.LogWarning(
                "No home keyframe found. Joint positions will default to zero."
            );
            return;
        }

        string qposText = GetAttribute(home, "qpos");

        if (string.IsNullOrWhiteSpace(qposText))
            return;

        string[] values = SplitValues(qposText);

        XmlNodeList joints =
            document.SelectNodes("/mujoco/worldbody//joint");

        if (joints == null)
            return;

        int qposIndex = 0;

        foreach (XmlNode joint in joints)
        {
            string jointName = GetAttribute(joint, "name");

            if (string.IsNullOrEmpty(jointName))
                continue;

            if (qposIndex >= values.Length)
            {
                Debug.LogWarning(
                    "Home qpos has no value for " + jointName
                );
                break;
            }

            HomePositions[jointName] =
                ParseFloat(values[qposIndex]);

            qposIndex++;
        }
    }

    private static void LoadMaterials(XmlDocument document)
    {
        XmlNodeList materials =
            document.SelectNodes("/mujoco/asset/material");

        if (materials == null)
            return;

        foreach (XmlNode node in materials)
        {
            string name = GetAttribute(node, "name");

            if (string.IsNullOrEmpty(name))
                continue;

            Color color = ParseColor(
                GetAttribute(node, "rgba")
            );

            MaterialCache[name] =
                CreateUrpMaterial(name, color);
        }
    }

    private static void BuildBody(
        XmlNode bodyNode,
        Transform parent)
    {
        string bodyName =
            GetAttribute(bodyNode, "name", "UnnamedBody");

        GameObject bodyObject = new GameObject(bodyName);

        Transform bodyTransform = bodyObject.transform;
        bodyTransform.SetParent(parent, false);

        Vector3 bodyPosition = ParseVector3(
            GetAttribute(bodyNode, "pos"),
            Vector3.zero
        );

        Quaternion bodyRotation = ParseQuaternion(
            GetAttribute(bodyNode, "quat")
        );

        // Apply this body's joint, if it has one.
        XmlNode jointNode = bodyNode.SelectSingleNode("joint");

        if (jointNode != null)
        {
            string jointName =
                GetAttribute(jointNode, "name");

            string jointClass =
                GetAttribute(jointNode, "class");

            bool isFinger =
                jointClass == "finger";

            Vector3 axis = ParseVector3(
                GetAttribute(jointNode, "axis"),
                isFinger ? defaultFingerAxis : defaultPandaAxis
            );

            string jointType = GetAttribute(
                jointNode,
                "type",
                isFinger
                    ? defaultFingerJointType
                    : defaultPandaJointType
            );

            float jointValue = 0f;

            if (!string.IsNullOrEmpty(jointName))
            {
                HomePositions.TryGetValue(
                    jointName,
                    out jointValue
                );
            }

            if (axis.sqrMagnitude > 0.000001f)
                axis.Normalize();

            if (jointType == "slide")
            {
                // MuJoCo slide joint: translation along its axis.
                bodyPosition +=
                    bodyRotation * (axis * jointValue);
            }
            else
            {
                // MuJoCo hinge joint: rotation around its axis.
                Quaternion jointRotation =
                    Quaternion.AngleAxis(
                        jointValue * Mathf.Rad2Deg,
                        axis
                    );

                bodyRotation =
                    bodyRotation * jointRotation;
            }
        }

        bodyTransform.localPosition = bodyPosition;
        bodyTransform.localRotation = bodyRotation;
        bodyTransform.localScale = Vector3.one;

        foreach (XmlNode child in bodyNode.ChildNodes)
        {
            if (child.Name == "geom")
            {
                BuildVisualGeometry(
                    child,
                    bodyTransform
                );
            }
            else if (child.Name == "body")
            {
                BuildBody(
                    child,
                    bodyTransform
                );
            }
        }
    }

    private static void BuildVisualGeometry(
        XmlNode geomNode,
        Transform parent)
    {
        if (GetAttribute(geomNode, "class") != "visual")
            return;

        string meshName =
            GetAttribute(geomNode, "mesh");

        if (string.IsNullOrEmpty(meshName))
            return;

        Mesh mesh = LoadMesh(meshName);

        if (mesh == null)
            return;

        GameObject meshObject =
            new GameObject(meshName);

        meshObject.transform.SetParent(parent, false);

        MeshFilter filter =
            meshObject.AddComponent<MeshFilter>();

        filter.sharedMesh = mesh;

        MeshRenderer renderer =
            meshObject.AddComponent<MeshRenderer>();

        string materialName =
            GetAttribute(geomNode, "material");

        if (MaterialCache.TryGetValue(
            materialName,
            out Material material))
        {
            renderer.sharedMaterial = material;
        }
    }

    private static Mesh LoadMesh(string meshName)
    {
        if (MeshCache.TryGetValue(
            meshName,
            out Mesh cachedMesh))
        {
            return cachedMesh;
        }

        string[] guids = AssetDatabase.FindAssets(
            meshName + " t:Model",
            new[] { MeshFolder }
        );

        foreach (string guid in guids)
        {
            string path =
                AssetDatabase.GUIDToAssetPath(guid);

            string assetName =
                Path.GetFileNameWithoutExtension(path);

            if (assetName != meshName)
                continue;

            GameObject model =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    path
                );

            if (model == null)
                continue;

            MeshFilter filter =
                model.GetComponentInChildren<MeshFilter>(true);

            if (filter == null || filter.sharedMesh == null)
                continue;

            MeshCache[meshName] = filter.sharedMesh;

            return filter.sharedMesh;
        }

        Debug.LogWarning(
            "Mesh not found: " + meshName +
            " in " + MeshFolder
        );

        return null;
    }

    private static Material CreateUrpMaterial(
        string name,
        Color color)
    {
        string path =
            MaterialFolder + "/" + name + ".mat";

        Material existing =
            AssetDatabase.LoadAssetAtPath<Material>(path);

        if (existing != null)
        {
            existing.color = color;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        Shader shader = Shader.Find(
            "Universal Render Pipeline/Lit"
        );

        if (shader == null)
            shader = Shader.Find("Standard");

        if (shader == null)
            throw new Exception("No compatible Unity shader found.");

        Material material = new Material(shader);

        material.name = name;
        material.color = color;

        AssetDatabase.CreateAsset(material, path);

        return material;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        string parent =
            Path.GetDirectoryName(folder).Replace("\\", "/");

        string child = Path.GetFileName(folder);

        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);

        AssetDatabase.CreateFolder(parent, child);
    }

    private static Vector3 ParseVector3(
        string value,
        Vector3 fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        string[] parts = SplitValues(value);

        if (parts.Length != 3)
            throw new Exception("Invalid MuJoCo vector: " + value);

        return new Vector3(
            ParseFloat(parts[0]),
            ParseFloat(parts[1]),
            ParseFloat(parts[2])
        );
    }

    private static Quaternion ParseQuaternion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Quaternion.identity;

        string[] parts = SplitValues(value);

        if (parts.Length != 4)
            throw new Exception("Invalid MuJoCo quaternion: " + value);

        // MuJoCo order is w, x, y, z.
        float w = ParseFloat(parts[0]);
        float x = ParseFloat(parts[1]);
        float y = ParseFloat(parts[2]);
        float z = ParseFloat(parts[3]);

        float magnitude = Mathf.Sqrt(
            x * x + y * y + z * z + w * w
        );

        if (magnitude < 0.000001f)
            return Quaternion.identity;

        return new Quaternion(
            x / magnitude,
            y / magnitude,
            z / magnitude,
            w / magnitude
        );
    }

    private static Color ParseColor(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Color.white;

        string[] parts = SplitValues(value);

        if (parts.Length < 3)
            return Color.white;

        float r = ParseFloat(parts[0]);
        float g = ParseFloat(parts[1]);
        float b = ParseFloat(parts[2]);
        float a = parts.Length > 3
            ? ParseFloat(parts[3])
            : 1f;

        return new Color(r, g, b, a);
    }

    private static string[] SplitValues(string value)
    {
        return value.Split(
            new[] { ' ', '\t', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries
        );
    }

    private static float ParseFloat(string value)
    {
        return float.Parse(
            value,
            CultureInfo.InvariantCulture
        );
    }

    private static string GetAttribute(
        XmlNode node,
        string name,
        string fallback = "")
    {
        XmlAttribute attribute = node.Attributes?[name];

        return attribute != null
            ? attribute.Value
            : fallback;
    }
}
