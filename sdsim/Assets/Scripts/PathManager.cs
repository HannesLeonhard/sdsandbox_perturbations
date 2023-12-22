using UnityEngine;
using System.Collections;
using System.Globalization;
using System.Collections.Generic;

public class PathManager : MonoBehaviour
{

	public CarPath path;

	public GameObject prefab;

	public Transform startPos;

	Vector3 span = Vector3.zero;

	public float spanDist;

	public int numSpans;

	public float turnInc;

	public bool sameRandomPath = true;

	public int randSeed = 2;

	public bool doMakeRandomPath = false;

	public bool doLoadScriptPath = false;

	public bool doLoadPointPath = true;

	public bool doBuildRoad = false;

	public bool doChangeLanes = false;

	public int smoothPathIter = 0;

	public bool doShowPath = false;

	public string pathToLoad = "none";

	public RoadBuilder roadBuilder;
	public RoadBuilder semanticSegRoadBuilder;

	public LaneChangeTrainer laneChTrainer;

	public GameObject locationMarkerPrefab;

	public int markerEveryN = 2;

	void Awake()
	{
		numSpans = 200;
		spanDist = 2f;
		turnInc = 1f;

		if (sameRandomPath)
			Random.InitState(randSeed);

		InitNewRoad();
	}

	public void InitNewRoad(string[] wayPoints = null)
	{
		// Creates a new road
		// If we were given a scripted path in the form of a list of strings
		// we use this
		if (wayPoints != null && wayPoints.Length > 0)
		{
			MakePointPath(wayPoints);
		}
		else if (doMakeRandomPath)
		{
			MakeRandomPath();
		}
		else if (doLoadScriptPath)
		{
			MakeScriptedPath();
		}
		else if (doLoadPointPath)
		{
			MakePointPath();
		}

		if (smoothPathIter > 0)
			SmoothPath();

		//Should we build a road mesh along the path?
		if (doBuildRoad && roadBuilder != null)
			roadBuilder.InitRoad(path);

		if (doBuildRoad && semanticSegRoadBuilder != null)
			semanticSegRoadBuilder.InitRoad(path);

		if (laneChTrainer != null && doChangeLanes)
		{
			laneChTrainer.ModifyPath(ref path);
		}

		if (locationMarkerPrefab != null && path != null)
		{
			int iLocId = 0;
			for (int iN = 0; iN < path.nodes.Count; iN += markerEveryN)
			{
				Vector3 np = path.nodes[iN].pos;
				GameObject go = Instantiate(locationMarkerPrefab, np, Quaternion.identity) as GameObject;
				go.transform.parent = this.transform;
				go.GetComponent<LocationMarker>().id = iLocId;
				iLocId++;
				go.tag = "pathNode";
			}
		}

		if (doShowPath && path != null)
		{
			for (int iN = 0; iN < path.nodes.Count; iN++)
			{
				Vector3 np = path.nodes[iN].pos;
				GameObject go = Instantiate(prefab, np, Quaternion.identity) as GameObject;
				go.tag = "pathNode";
				go.transform.parent = this.transform;
			}
		}
	}

	public void DestroyRoad()
	{
		GameObject[] prev = GameObject.FindGameObjectsWithTag("pathNode");

		foreach (GameObject g in prev)
			Destroy(g);

		if (roadBuilder != null)
			roadBuilder.DestroyRoad();
	}

	public Vector3 GetPathStart()
	{
		return startPos.position;
	}

	public Vector3 GetPathEnd()
	{
		int iN = path.nodes.Count - 1;

		if (iN < 0)
			return GetPathStart();

		return path.nodes[iN].pos;
	}

	void SmoothPath()
	{
		while (smoothPathIter > 0)
		{
			path.SmoothPath();
			smoothPathIter--;
		}
	}

	void MakePointPath(string[] waypoints = null)
	{
		string[] lines = new string[0];
		// If we do not have params fetch a scripted path
		if (waypoints != null && waypoints.Length > 0)
		{
			lines = waypoints;
		}
		else
		{
			string filename = pathToLoad;

			TextAsset bindata = Resources.Load(filename) as TextAsset;

			if (bindata == null)
				return;

			lines = bindata.text.Split('\n');
		}

		Debug.Log(string.Format("found {0} path points. to load", lines.Length));

		path = new CarPath();

		Vector3 np = Vector3.zero;

		float offsetY = -0.1f;

		foreach (string line in lines)
		{
			string[] tokens = line.Split(',');

			if (tokens.Length != 3)
				continue;
			np.x = float.Parse(tokens[0], CultureInfo.InvariantCulture.NumberFormat);
			np.y = float.Parse(tokens[1], CultureInfo.InvariantCulture.NumberFormat) + offsetY;
			np.z = float.Parse(tokens[2], CultureInfo.InvariantCulture.NumberFormat);
			PathNode p = new PathNode();
			p.pos = np;
			path.nodes.Add(p);
		}

	}

	void MakeScriptedPath(string[] waypoints = null)
	{
		TrackScript script = new TrackScript();

		bool flag = false;
		// If we were given a path we create it, otherwise we read the path
		if (waypoints != null && waypoints.Length > 0)
		{
			flag = script.CreatePath(waypoints);
		}
		else
		{
			flag = script.Read(pathToLoad);
		}

		if (flag)
		{
			path = new CarPath();
			TrackParams tparams = new TrackParams();
			tparams.numToSet = 0;
			tparams.rotCur = Quaternion.identity;
			tparams.lastPos = startPos.position;

			float dY = 0.0f;
			float turn = 0f;

			Vector3 s = startPos.position;
			s.y = 0.5f;
			span.x = 0f;
			span.y = 0f;
			span.z = spanDist;
			float turnVal = 10.0f;

			foreach (TrackScriptElem se in script.track)
			{
				if (se.state == TrackParams.State.AngleDY)
				{
					turnVal = se.value;
				}
				else if (se.state == TrackParams.State.CurveY)
				{
					turn = 0.0f;
					dY = se.value * turnVal;
				}
				else
				{
					dY = 0.0f;
					turn = 0.0f;
				}

				for (int i = 0; i < se.numToSet; i++)
				{

					Vector3 np = s;
					PathNode p = new PathNode();
					p.pos = np;
					path.nodes.Add(p);

					turn = dY;

					Quaternion rot = Quaternion.Euler(0.0f, turn, 0f);
					span = rot * span.normalized;
					span *= spanDist;
					s = s + span;
				}

			}
		}
	}

	void MakeRandomPath()
	{
		path = new CarPath();

		Vector3 s = startPos.position;
		Debug.Log(string.Format("Start pos is {0}", startPos.position));
		float turn = 0f;
		s.y = 0.5f;

		span.x = 0f;
		span.y = 0f;
		span.z = spanDist;

		for (int iS = 0; iS < numSpans; iS++)
		{
			Vector3 np = s;
			PathNode p = new PathNode();
			p.pos = np;
			path.nodes.Add(p);

			float t = UnityEngine.Random.Range(-1.0f * turnInc, turnInc);

			turn += t;

			Quaternion rot = Quaternion.Euler(0.0f, turn, 0f);
			span = rot * span.normalized;

			if (SegmentCrossesPath(np + (span.normalized * 100.0f), 90.0f))
			{
				//turn in the opposite direction if we think we are going to run over the path
				turn *= -0.5f;
				rot = Quaternion.Euler(0.0f, turn, 0f);
				span = rot * span.normalized;
			}

			span *= spanDist;

			s = s + span;
		}
		Debug.Log(string.Format("Generated track is {0}", startPos.position));

	}

	public bool SegmentCrossesPath(Vector3 posA, float rad)
	{
		foreach (PathNode pn in path.nodes)
		{
			float d = (posA - pn.pos).magnitude;

			if (d < rad)
				return true;
		}

		return false;
	}

	public void SetPath(CarPath p)
	{
		path = p;

		GameObject[] prev = GameObject.FindGameObjectsWithTag("pathNode");

		Debug.Log(string.Format("Cleaning up {0} old nodes. {1} new ones.", prev.Length, p.nodes.Count));

		DestroyRoad();

		foreach (PathNode pn in path.nodes)
		{
			GameObject go = Instantiate(prefab, pn.pos, Quaternion.identity) as GameObject;
			go.tag = "pathNode";
		}
	}
}
