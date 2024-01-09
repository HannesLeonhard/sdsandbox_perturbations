using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using System.Threading;


public class StatsDisplayer : MonoBehaviour
{

    // Flag to see if car started
    public static bool carStarted;

    // Script start time
    public static DateTime scriptStartTime;

    // Log informations / histories per Lap
    public static bool writeLog = true;
    private string frameLogPath;
    public static List<float> timesHistory;

    public static List<float> offTrackHistory;
    public static List<int> crashesHistory;

    public static List<float> xteAvgHistory;
    public static List<float> xteVarHistory;
    public static List<float> maxXteHistory;

    public static List<float> steersAvgHistory;
    public static List<float> steersVarsHistory;

    public static List<float> speedAvgHistory;
    public static List<float> speedVarHistory;

    // Displayable Stats
    public static int lap;
    public static int lapCrashes;
    public static float lapTime;
    public static float prevLapTime;
    public static int offTrackEpisodeCounter;
    public static float xte;
    public static float maxXte;
    public static float lastLapSteerVar;
    public static float curSpeed; // new
    public static float avgSpeed; // new

    // Lap frames
    public static List<float> lapXtes;
    public static List<float> lapSteers;
    public static List<float> lapSpeeds;

    // Parameters
    public float offTrackXTEThreshold; // 2.2
    private bool isOffTrack = false;
    public static float startTime;

    // Labels to update
    private Text lapLabel;
    private Text timeLabel;
    private Text prevTimeLabel;
    private Text outOfTrackLabel;
    private Text xteLabel;
    private Text xteMaxLabel;
    private Text currWaypointLabel;
    private Text currSpeedLabel; // new
    private Text avgSpeedLabel; // new
    private Text avgXteLabel; // new

    // Path manager for XTE
    private static PathManager pm;
    private static int currentWaypoint;

    // Car
    private static double epsilon = 0.01;
    private Vector3 startingCarPosition;
    public GameObject carObj;
    private Car car;

    public int frameId;

    public int limitFPS;

    float timeSinceLastCapture;

    public int frameCounter = 0;

    string GetLogPath()
    {
        if (GlobalState.log_path != "default")
            return GlobalState.log_path + "/";

        // Debug.Log("Application.dataPath: " + Application.dataPath);
        return Application.dataPath + "/../log/";
    }

    // Start is called before the first frame update
    void Start()
    {
        limitFPS = 21;
        timeSinceLastCapture = 0.0f;

        InitializeStats(true);

        carStarted = checkCarStarted();
    }

    private void InitializeStats(bool createFile)
    {
        currentWaypoint = 0;
        lap = 1;
        lapCrashes = 0;
        offTrackXTEThreshold = 2.2f;

        // Updating script start time
        scriptStartTime = DateTime.Now;
        frameId = 0;

        // Initializing stats
        lapTime = 0;
        prevLapTime = 0;
        offTrackEpisodeCounter = 0;
        xte = 0;
        maxXte = 0;
        lastLapSteerVar = 0;
        curSpeed = 0; // new
        avgSpeed = 0; // new

        lapXtes = new List<float>();
        lapSteers = new List<float>();
        lapSpeeds = new List<float>();

        // Initializing histories
        if (writeLog)
        {
            timesHistory = new List<float>();
            offTrackHistory = new List<float>();
            crashesHistory = new List<int>();

            xteAvgHistory = new List<float>();
            xteVarHistory = new List<float>();
            maxXteHistory = new List<float>();

            steersAvgHistory = new List<float>();
            steersVarsHistory = new List<float>();

            speedAvgHistory = new List<float>();
            speedVarHistory = new List<float>();

            string filename = "Simulation-" + generateFilenameFromTimestamp();

            // check if directory doesn't exit
            if (!Directory.Exists(Application.dataPath + "/Testing/"))
            {
                //if it doesn't, create it
                Directory.CreateDirectory(Application.dataPath + "/Testing/");
            }

            // check if directory exits
            // if (Directory.Exists(Application.dataPath + "/Testing/tub"))
            // {
            //     Directory.Delete(Application.dataPath + "/Testing/tub");
            // }

            // Directory.CreateDirectory(Application.dataPath + "/Testing/tub");

            frameLogPath = Application.dataPath + "/Testing/" + filename + ".csv";
            System.IO.File.AppendAllLines(frameLogPath, new string[] { "frameId,lap,xte,steering,throttle,speed,acceleration,x,y,isOffTrack" });
        }

        // Initializing PM
        pm = FindObjectOfType<PathManager>();

        // Initializing Car
        car = (Car)Utilities.tryGetCar("DonkeyCar");
        if (car != null)
            startingCarPosition = car.transform.position;

        // Getting labels to update
        GameObject statsPanel = GameObject.Find("StatsPanel");
        if (statsPanel != null)
        {
            Text[] labels = statsPanel.GetComponentsInChildren<Text>();

            if (labels.Length >= 10)
            {
                lapLabel = labels[0];
                currWaypointLabel = labels[1];
                timeLabel = labels[2];
                outOfTrackLabel = labels[3];
                prevTimeLabel = labels[4];
                currSpeedLabel = labels[5];
                avgSpeedLabel = labels[6];
                xteLabel = labels[7];
                avgXteLabel = labels[8];
                xteMaxLabel = labels[9];
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (car == null)
        {
            car = (Car)Utilities.tryGetCar("DonkeyCar");
            if (car != null)
                startingCarPosition = car.transform.position;
        }

        if (!carStarted)
        {
            carStarted = checkCarStarted();
        }

        if (carStarted)
        {
            getUpdatedStats();
            displayStats();
        }

        if (writeLog && carStarted)
        {
            // writes at a frame rate of 21
            timeSinceLastCapture += Time.deltaTime;

            if (timeSinceLastCapture < 1.0f / limitFPS)
                return;

            timeSinceLastCapture -= (1.0f / limitFPS);

            writePerFrameStats();
        }
    }

    private void OnDestroy()
    {
        if (writeLog)
        {
            string hour = "" + scriptStartTime.Hour;
            string minute = "" + scriptStartTime.Minute;

            if (scriptStartTime.Hour < 10)
            {
                hour = "0" + hour;
            }

            if (scriptStartTime.Minute < 10)
            {
                minute = "0" + minute;
            }

            // Writing laps (MacOS)
            string filename = "Laps - " + scriptStartTime.Year + "" + scriptStartTime.Month + "" + scriptStartTime.Day + "-" + hour + "h" + minute + "m";
            string filepath = Application.dataPath + "/Testing/" + filename + ".csv";

            string text = "Lap time,Max XTE,XTE avg,XTE var,Steer avg,Steer var,Speed avg,Speed var,Off track\n";
            for (int i = 0; i < timesHistory.Count; i++)
            {
                float t = timesHistory[i];
                float m = maxXteHistory[i];
                float xa = xteAvgHistory[i];
                float xv = xteVarHistory[i];
                float sta = steersAvgHistory[i];
                float stv = steersVarsHistory[i];
                float spa = speedAvgHistory[i];
                float spv = speedVarHistory[i];
                float c = offTrackHistory[i];

                text += t + "," + m + "," + xa + "," + xv + "," + sta + "," + stv + "," + spa + "," + spv + "," + c + "\n";
            }

            File.WriteAllText(filepath, text);
            Debug.Log("Log file written to " + filepath);
        }

        carStarted = false;
        InitializeStats(false);
    }

    private bool checkCarStarted()
    {
        if (carStarted)
        {
            return true;
        }

        bool areClose(double a, double b)
        {
            if (Math.Abs(a - b) < epsilon)
            {
                return true;
            };

            return false;
        }

        if (car == null)
        {
            return false;
        }

        if (
            areClose(car.transform.position.x, startingCarPosition.x) &&
            areClose(car.transform.position.z, startingCarPosition.z)
            )
        {
            return false;
        }

        startTime = Time.time;
        return true;
    }


    private void getUpdatedStats()
    {
        if (car == null)
        {
            Utilities.tryGetCar("DonkeyCar");

            if (car == null) { return; }
        }

        // Updating time
        lapTime = (float)Math.Round(Time.time - startTime, 2);

        // Updating frame infos
        lapSteers.Add(Math.Abs(car.GetSteering()));
        lapSpeeds.Add((float)Math.Round(car.GetVelocity().magnitude, 2)); // km/h
        curSpeed = (float)Math.Round(car.GetVelocity().magnitude, 2); // km/h
        avgSpeed = Utilities.getMean(lapSpeeds);

        if (pm != null)
        {
            // Updating current way point
            if (pm.path.iActiveSpan + 1 > currentWaypoint)
                currentWaypoint = pm.path.iActiveSpan + 1;

            // Checking if lap finished
            if (pm.path.iActiveSpan == 1 && currentWaypoint >= pm.path.nodes.Count - 2)
            {
                endOfLapUpdates();
            }

            // Updating XTE
            if (!pm.path.GetCrossTrackErr(car.GetTransform(), ref xte))
            {

                if (car.GetLastCollision() != null)
                {
                    // Car crashed
                    lapCrashes += 1;
                    car.ClearLastCollision();
                }
                else
                {
                    if (Utilities.carIsGoingForward(car))
                    {
                        // Lap finished, looping
                        pm.path.ResetActiveSpan();
                        endOfLapUpdates();
                    }
                }
            };

            // Updating xte infos
            lapXtes.Add(Math.Abs(xte));

            // Updating Out-of-track counter
            if (Math.Abs(xte) > Math.Abs(offTrackXTEThreshold))
            {
                if (!isOffTrack)
                {
                    offTrackEpisodeCounter += 1;
                    isOffTrack = true;
                }
            }
            else
            {
                isOffTrack = false;
            }
        }

        // Updating max XTE
        if (Math.Abs(xte) > maxXte)
        {
            maxXte = Math.Abs(xte);
        }

        currentWaypoint = pm.path.iActiveSpan + 1;
    }

    public static void endOfLapUpdates()
    {
        // Updating current waypoint
        currentWaypoint = pm.path.iActiveSpan + 1;

        // Counting lap
        lap += 1;

        // Updating prev. lap time and current starting time
        float finishTime = Time.time;
        prevLapTime = (float)Math.Round(finishTime - startTime, 2);
        startTime = finishTime;


        // Updating log infos
        if (writeLog)
        {
            timesHistory.Add(prevLapTime);

            // XTE
            float lapxteavg = (float)Math.Round(Utilities.getMean(lapXtes), 4);
            xteAvgHistory.Add((float)Math.Round(lapxteavg, 4));
            xteVarHistory.Add((float)Math.Round(Utilities.getVar(lapXtes, lapxteavg), 4));
            maxXteHistory.Add((float)Math.Round(maxXte, 4));

            // Steer
            float lapsteeravg = Utilities.getMean(lapSteers);
            steersAvgHistory.Add((float)Math.Round(lapsteeravg, 4));
            lastLapSteerVar = Utilities.getVar(lapSteers, lapsteeravg);
            steersVarsHistory.Add((float)Math.Round(lastLapSteerVar, 4));

            // Speed
            float lapspeedavg = (float)Math.Round(Utilities.getMean(lapSpeeds), 2);
            // avgSpeed = lapspeedavg;
            speedAvgHistory.Add(lapspeedavg);
            speedVarHistory.Add((float)Math.Round(Utilities.getVar(lapSpeeds, lapspeedavg), 2));

            // Crashes
            crashesHistory.Add(lapCrashes);
            offTrackHistory.Add(offTrackEpisodeCounter);
        }

        // Clear lap frames
        lapXtes.Clear();
        lapSteers.Clear();
        lapSpeeds.Clear();

        // Resetting max XTE and off-track counter for this lap
        maxXte = 0;
        offTrackEpisodeCounter = 0;
        lapCrashes = 0;
    }

    private void displayStats()
    {
        /*lapLabel.text = lapLabel.text.Split(new string[] { ": " }, StringSplitOptions.None)[0] + ": " + lap;
        timeLabel.text = timeLabel.text.Split(new string[] { ": " }, StringSplitOptions.None)[0] + ": " + lapTime;
        outOfTrackLabel.text = outOfTrackLabel.text.Split(new string[] { ": " }, StringSplitOptions.None)[0] + ": " + offTrackEpisodeCounter;
        prevTimeLabel.text = prevTimeLabel.text.Split(new string[] { ": " }, StringSplitOptions.None)[0] + ": " + prevLapTime;
        currSpeedLabel.text = currSpeedLabel.text.Split(new string[] { ": " }, StringSplitOptions.None)[0] + ": " + curSpeed;
        avgSpeedLabel.text = avgSpeedLabel.text.Split(new string[] { ": " }, StringSplitOptions.None)[0] + ": " + Math.Round(avgSpeed, 4);
        xteLabel.text = xteLabel.text.Split(new string[] { ": " }, StringSplitOptions.None)[0] + ": " + xte;
        avgXteLabel.text = avgXteLabel.text.Split(new string[] { ": " }, StringSplitOptions.None)[0] + ": " + Math.Round(Utilities.getMean(lapXtes), 4);
        xteMaxLabel.text = xteMaxLabel.text.Split(new string[] { ": " }, StringSplitOptions.None)[0] + ": " + maxXte;
        currWaypointLabel.text = currWaypointLabel.text.Split(new string[] { ": " }, StringSplitOptions.None)[0] + ": " + (currentWaypoint) + "/" + (pm.path.nodes.Count - 1);*/
    }

    private void writePerFrameStats()
    {
        string frameStats = frameId + "," + lap + "," + xte + "," + car.GetSteering() + "," + car.GetThrottle() + "," + car.GetVelocity().magnitude + "," + car.GetAccel().magnitude + "," + car.transform.position.x + "," + car.transform.position.z + "," + isOffTrack;
        File.AppendAllLines(frameLogPath, new string[] { frameStats });
        frameId += 1;
    }

    private string generateFilenameFromTimestamp()
    {
        string hour = "" + scriptStartTime.Hour;
        string minute = "" + scriptStartTime.Minute;
        string second = "" + scriptStartTime.Second;

        if (scriptStartTime.Hour < 10)
        {
            hour = "0" + hour;
        }

        if (scriptStartTime.Minute < 10)
        {
            minute = "0" + minute;
        }

        if (scriptStartTime.Second < 10)
        {
            second = "0" + second;
        }

        return scriptStartTime.Year + "" + scriptStartTime.Month + "" + scriptStartTime.Day + "-" + hour + "h" + minute + "m" + second + "s";
    }

    public static int getCurrentWaypoint()
    {
        return currentWaypoint;
    }

    public static int getLap()
    {
        return lap;
    }

}
