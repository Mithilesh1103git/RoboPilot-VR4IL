
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class PandaRobotImporter
{
	private const string OutputName = "FrankaPanda_Articulated";

	private const string GeneratedFolder =
		"Assets/RoboPilot/Robot/FrankaPanda/Generated/Articulated/";

	private const string ManifestPath =
		GeneratedFolder + "panda_articulated.json";

	[MenuItem("RoboPilot/Franka Panda/Import Articulated Robot")]
	public static void ImportRobot()
	{
		try
		{
			Import();
		}
		catch (Exception exception)
		{
			Debug.LogError(
				"[Panda Importer] Import failed:\n" + exception
			);
			EditorUtility.DisplayDialog(
				"Franka Panda Import Failed",
				exception.Message,
				"OK"
			);
		}
	}

	private static void Import()
	{
		// ---------------------------------------------------------
		// 1. Validate manifest
		// ---------------------------------------------------------

		if (!File.Exists(ManifestPath))
		{
			throw new FileNotFoundException(
				"Could not find the articulated Panda manifest.",
				ManifestPath
			);
		}

		string json = File.ReadAllText(ManifestPath);

		PandaManifest manifest =
			JsonUtility.FromJson<PandaManifest>(json);

		if (manifest == null ||
			manifest.bodies == null ||
			manifest.bodies.Length == 0)
		{
			throw new Exception(
				"The Panda manifest is empty or invalid."
			);
		}

		ValidateManifest(manifest);

		// ---------------------------------------------------------
		// 2. Remove previous generated robot BEFORE creating new one
		// ---------------------------------------------------------

		RemovePreviousRobot();

		// ---------------------------------------------------------
		// 3. Create new robot root
		// ---------------------------------------------------------

		GameObject root = new GameObject(OutputName);

		Undo.RegisterCreatedObjectUndo(
			root,
			"Import Franka Panda"
		);

		root.transform.position = Vector3.zero;
		root.transform.rotation = Quaternion.identity;
		root.transform.localScale = Vector3.one;

		// ---------------------------------------------------------
		// 4. Build body hierarchy
		// ---------------------------------------------------------

		Dictionary<string, Transform> bodyTransforms =
			new Dictionary<string, Transform>();

		int importedBodies = 0;
		int importedJoints = 0;

		// Import in manifest order. The exporter writes parents
		// before their children.
		foreach (BodyData body in manifest.bodies)
		{
			Transform parentTransform;

			if (body.parent == "world")
			{
				parentTransform = root.transform;
			}
			else if (!bodyTransforms.TryGetValue(
						 body.parent,
						 out parentTransform))
			{
				throw new Exception(
					"Parent body '" + body.parent +
					"' was not imported before child '" +
					body.name + "'."
				);
			}

			Transform bodyTransform;

            if (body.joint != null && !string.IsNullOrWhiteSpace(body.joint.name))
            {
				bodyTransform = CreateJointBody(
					body,
					parentTransform
				);

				importedJoints++;
			}
			else
			{
				GameObject bodyObject =
					new GameObject(body.name);

				Undo.RegisterCreatedObjectUndo(
					bodyObject,
					"Create Panda Body"
				);

				bodyTransform = bodyObject.transform;

				bodyTransform.SetParent(
					parentTransform,
					false
				);

				ApplyLocalTransform(
					bodyTransform,
					body.local_position_unity,
					body.local_rotation_unity_xyzw
				);
			}

			bodyTransforms.Add(
				body.name,
				bodyTransform
			);

			ImportBodyMesh(
				body,
				bodyTransform
			);

			importedBodies++;
		}

		// ---------------------------------------------------------
		// 5. Select the completed root
		// ---------------------------------------------------------

		Selection.activeGameObject = root;

		EditorSceneManagerMarkDirty();

		Debug.Log(
			"[Panda Importer] Import completed.\n" +
			"Bodies: " + importedBodies + "\n" +
			"Joints: " + importedJoints + "\n" +
			"Root: " + OutputName
		);
	}

	private static void RemovePreviousRobot()
	{
		// Clear selection before destroying anything. This avoids
		// leaving the Inspector targeting deleted components.
		Selection.activeObject = null;

		GameObject previous = GameObject.Find(OutputName);

		if (previous == null)
			return;

		Undo.DestroyObjectImmediate(previous);

		Debug.Log(
			"[Panda Importer] Removed previous generated robot."
		);
	}

	private static Transform CreateJointBody(
		BodyData body,
		Transform parentTransform)
	{
		JointData joint = body.joint;

		Vector3 bodyPosition =
			ToVector3(body.local_position_unity);

		Quaternion bodyRotation =
			ToQuaternion(body.local_rotation_unity_xyzw);

		Vector3 pivotPosition =
			ToVector3(joint.pivot_unity);

		Vector3 axis =
			ToVector3(joint.axis_unity);

		// The pivot is positioned relative to the parent body.
		GameObject pivotObject =
			new GameObject(joint.name + "_Pivot");

		Undo.RegisterCreatedObjectUndo(
			pivotObject,
			"Create Panda Joint"
		);

		Transform pivotTransform = pivotObject.transform;

		pivotTransform.SetParent(
			parentTransform,
			false
		);

		pivotTransform.localPosition =
			bodyPosition + bodyRotation * pivotPosition;

		pivotTransform.localRotation =
			bodyRotation;

		pivotTransform.localScale =
			Vector3.one;

		// The actual body is a child of the pivot.
		// Its local offset compensates for the joint pivot.
		GameObject bodyObject =
			new GameObject(body.name);

		Undo.RegisterCreatedObjectUndo(
			bodyObject,
			"Create Panda Body"
		);

		Transform bodyTransform = bodyObject.transform;

		bodyTransform.SetParent(
			pivotTransform,
			false
		);

		bodyTransform.localPosition =
			-pivotPosition;

		bodyTransform.localRotation =
			Quaternion.identity;

		bodyTransform.localScale =
			Vector3.one;

		// Configure the runtime joint driver.
		PandaJointDriver driver =
			pivotObject.AddComponent<PandaJointDriver>();

		driver.JointName = joint.name;

		driver.Type =
			string.Equals(
				joint.type,
				"slide",
				StringComparison.OrdinalIgnoreCase
			)
				? PandaJointDriver.JointType.Slide
				: PandaJointDriver.JointType.Hinge;

		driver.Axis =
			axis.sqrMagnitude > 0.000001f
				? axis.normalized
				: Vector3.up;

		driver.Limited = joint.limited;

		if (joint.range_radians_or_metres != null &&
			joint.range_radians_or_metres.Length >= 2)
		{
			driver.Minimum =
				joint.range_radians_or_metres[0];

			driver.Maximum =
				joint.range_radians_or_metres[1];
		}

		driver.HomePosition =
			joint.home_position;

		driver.DrivenBody =
			bodyTransform;

		// Initialize only after all transforms and driver fields
		// have been assigned. This captures the neutral transform
		// and applies the joint's home position once.
		driver.Initialize();

		return bodyTransform;
	}

	private static void ImportBodyMesh(
		BodyData body,
		Transform bodyTransform)
	{
		if (body.mesh == null ||
			string.IsNullOrWhiteSpace(body.mesh.file))
		{
			Debug.LogWarning(
				"[Panda Importer] No mesh specified for body: " +
				body.name
			);

			return;
		}

		string meshPath =
			GeneratedFolder + body.mesh.file;

		GameObject meshAsset =
			AssetDatabase.LoadAssetAtPath<GameObject>(
				meshPath
			);

		if (meshAsset == null)
		{
			throw new FileNotFoundException(
				"Could not load OBJ mesh for body '" +
				body.name + "'. Expected asset: " +
				meshPath
			);
		}

		GameObject meshInstance =
			PrefabUtility.InstantiatePrefab(
				meshAsset
			) as GameObject;

		if (meshInstance == null)
		{
			meshInstance =
				UnityEngine.Object.Instantiate(
					meshAsset
				);
		}

		if (meshInstance == null)
		{
			throw new Exception(
				"Could not instantiate mesh for body: " +
				body.name
			);
		}

		meshInstance.name = body.name + "_Visual";

		meshInstance.transform.SetParent(
			bodyTransform,
			false
		);

		meshInstance.transform.localPosition =
			Vector3.zero;

		meshInstance.transform.localRotation =
			Quaternion.identity;

		meshInstance.transform.localScale =
			Vector3.one;
	}

	private static void ApplyLocalTransform(
		Transform target,
		float[] position,
		float[] rotation)
	{
		target.localPosition =
			ToVector3(position);

		target.localRotation =
			ToQuaternion(rotation);

		target.localScale =
			Vector3.one;
	}

	private static Vector3 ToVector3(float[] values)
	{
		if (values == null || values.Length < 3)
			return Vector3.zero;

		return new Vector3(
			values[0],
			values[1],
			values[2]
		);
	}

    private static Quaternion ToQuaternion(float[] values)
    {
        if (values == null || values.Length < 4)
            return Quaternion.identity;

        // Manifest quaternion order is Unity XYZW.
        Quaternion result = new Quaternion(
            values[0],
            values[1],
            values[2],
            values[3]
        );

        float magnitudeSquared =
            result.x * result.x +
            result.y * result.y +
            result.z * result.z +
            result.w * result.w;

        if (magnitudeSquared < 0.000001f)
            return Quaternion.identity;

        return result.normalized;
    }

    private static void ValidateManifest(PandaManifest manifest)
	{
		HashSet<string> bodyNames =
			new HashSet<string>();

		foreach (BodyData body in manifest.bodies)
		{
			if (body == null)
				throw new Exception(
					"Manifest contains a null body entry."
				);

			if (string.IsNullOrWhiteSpace(body.name))
				throw new Exception(
					"Manifest contains a body with no name."
				);

			if (!bodyNames.Add(body.name))
				throw new Exception(
					"Duplicate body name: " + body.name
				);

			if (body.local_position_unity == null ||
				body.local_position_unity.Length < 3)
			{
				throw new Exception(
					"Invalid local position for body: " +
					body.name
				);
			}

			if (body.local_rotation_unity_xyzw == null ||
				body.local_rotation_unity_xyzw.Length < 4)
			{
				throw new Exception(
					"Invalid local rotation for body: " +
					body.name
				);
			}

            if (body.joint != null &&
!string.IsNullOrWhiteSpace(body.joint.name))
            {

                if (body.joint.axis_unity == null ||
					body.joint.axis_unity.Length < 3)
				{
					throw new Exception(
						"Invalid joint axis for: " +
						body.joint.name
					);
				}

				if (body.joint.pivot_unity == null ||
					body.joint.pivot_unity.Length < 3)
				{
					throw new Exception(
						"Invalid joint pivot for: " +
						body.joint.name
					);
				}
			}
		}

		// Ensure all parents exist and appear before their children.
		HashSet<string> importedNames =
			new HashSet<string>();

		foreach (BodyData body in manifest.bodies)
		{
			if (body.parent != "world" &&
				!importedNames.Contains(body.parent))
			{
				throw new Exception(
					"Parent '" + body.parent +
					"' is missing or appears after child '" +
					body.name + "'."
				);
			}

			importedNames.Add(body.name);
		}
	}

	private static void EditorSceneManagerMarkDirty()
	{
		if (UnityEditor.SceneManagement.EditorSceneManager
			.GetActiveScene().IsValid())
		{
			UnityEditor.SceneManagement.EditorSceneManager
				.MarkSceneDirty(
					UnityEditor.SceneManagement.EditorSceneManager
						.GetActiveScene()
				);
		}
	}

	// -------------------------------------------------------------
	// JSON manifest classes
	// -------------------------------------------------------------

	[Serializable]
	private class PandaManifest
	{
		public string model;
		public string source_xml;
		public string coordinate_conversion;
		public string reference_configuration;

		public int body_count;
		public int joint_count;
		public int degrees_of_freedom;
		public int position_coordinates;

		public float[] home_qpos;

		public BodyData[] bodies;
	}

	[Serializable]
	private class BodyData
	{
		public int id;
		public string name;
		public string parent;

		public float[] local_position_unity;
		public float[] local_rotation_unity_xyzw;

		public MeshData mesh;
		public JointData joint;
	}

	[Serializable]
	private class MeshData
	{
		public string file;
		public int geometries;
		public int vertices;
	}

	[Serializable]
	private class JointData
	{
		public string name;
		public string type;

		public float[] axis_unity;
		public float[] pivot_unity;

		public bool limited;
		public float[] range_radians_or_metres;

		public float home_position;

		public int qpos_address;
		public int dof_address;
	}
}
