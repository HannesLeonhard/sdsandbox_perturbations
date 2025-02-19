using System.Collections;
using UnityEngine;
using System;
using UnityEngine.UI;
using System.Globalization;
using UnityEngine.SceneManagement;
using UnityStandardAssets.ImageEffects;
using System.Collections.Generic;


namespace tk
{

    public class TcpCarHandler : MonoBehaviour
    {

        public GameObject carObj;
        public ICar car;

        public PathManager pm;
        public CameraSensor camSensor;
        private tk.JsonTcpClient client;
        public Text ai_text;

        public float limitFPS = 21.0f;
        float timeSinceLastCapture = 0.0f;

        float steer_to_angle = 25.0f;

        float ai_steering = 0.0f;
        float ai_throttle = 0.0f;
        float ai_brake = 0.0f;

        bool asynchronous = true;
        float time_step = 0.1f;
        bool bResetCar = false;
        bool bExitScene = false;

        public enum State
        {
            UnConnected,
            SendTelemetry
        }

        public State state = State.UnConnected;
        State prev_state = State.UnConnected;

        void Awake()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = (int)limitFPS;

            car = carObj.GetComponent<ICar>();
            pm = GameObject.FindObjectOfType<PathManager>();

            Canvas canvas = GameObject.FindObjectOfType<Canvas>();
            GameObject go = CarSpawner.getChildGameObject(canvas.gameObject, "AISteering");
            if (go != null)
                ai_text = go.GetComponent<Text>();
        }

        public void Init(tk.JsonTcpClient _client)
        {
            client = _client;

            if (client == null)
            {
                Debug.Log("Initial Client is null");
                return;
            }

            client.dispatchInMainThread = false; //too slow to wait.
            client.dispatcher.Register("control", new tk.Delegates.OnMsgRecv(OnControlsRecv));
            client.dispatcher.Register("exit_scene", new tk.Delegates.OnMsgRecv(OnExitSceneRecv));
            client.dispatcher.Register("reset_car", new tk.Delegates.OnMsgRecv(OnResetCarRecv));
            client.dispatcher.Register("new_car", new tk.Delegates.OnMsgRecv(OnRequestNewCarRecv));
            client.dispatcher.Register("step_mode", new tk.Delegates.OnMsgRecv(OnStepModeRecv));
            client.dispatcher.Register("quit_app", new tk.Delegates.OnMsgRecv(OnQuitApp));
            client.dispatcher.Register("regen_road", new tk.Delegates.OnMsgRecv(OnRegenRoad));
            client.dispatcher.Register("car_config", new tk.Delegates.OnMsgRecv(OnCarConfig));
            client.dispatcher.Register("cam_config", new tk.Delegates.OnMsgRecv(OnCamConfig));
            client.dispatcher.Register("disconnect", new tk.Delegates.OnMsgRecv(OnDisconnect));

            Debug.Log("Finished Car Handler init");
        }

        public void Start()
        {
            SendCarLoaded();
            state = State.SendTelemetry;
            Debug.Log("Started Car Handler");
        }

        public tk.JsonTcpClient GetClient()
        {
            return client;
        }

        public void OnDestroy()
        {
            if (client)
                client.dispatcher.Reset();
            Debug.Log("Destroyed Car Handler");
        }

        void OnDisconnect(JSONObject json)
        {
            OnExitSceneRecv(json);
            Disconnect();
            OnQuitApp(json);
        }

        void Disconnect()
        {
            Debug.Log("Disconnecting");
            client.Disconnect();
        }

        void SendTelemetry()
        {
            if (client == null)
                return;

            JSONObject json = new JSONObject(JSONObject.Type.OBJECT);
            json.AddField("msg_type", "telemetry");

            json.AddField("steering_angle", car.GetSteering() / steer_to_angle);
            json.AddField("throttle", car.GetThrottle());
            json.AddField("speed", car.GetVelocity().magnitude);
            json.AddField("image", Convert.ToBase64String(camSensor.GetImageBytes()));

            json.AddField("hit", car.GetLastCollisionName());
            car.ClearLastCollision();

            Transform tm = car.GetTransform();
            json.AddField("pos_x", tm.position.x);
            json.AddField("pos_y", tm.position.y);
            json.AddField("pos_z", tm.position.z);

            json.AddField("time", Time.timeSinceLevelLoad);

            json.AddField("lap", StatsDisplayer.getLap());
            json.AddField("sector", StatsDisplayer.getCurrentWaypoint());

            if (pm != null)
            {
                json.AddField("sector", pm.path.iActiveSpan);
                float cte = 0.0f;
                if (pm.path.GetCrossTrackErr(tm.position, ref cte))
                {
                    if (cte > 2.0f || cte < -2.0f) {
                        json.AddField("done", true);
                        Debug.Log("Cross track error is greater than 2, resetting active span");
                    }
                    else {
                        json.AddField("done", false);
                        Debug.Log("Cross track error is " + cte);
                    }
                    json.AddField("cte", cte);
                }
                else
                {
                    pm.path.ResetActiveSpan();
                    json.AddField("cte", 0.0f);
                    json.AddField("done", true);
                    Debug.Log("Resetting active span, cross track error set to 0");
                }
                json.AddField("maxSector", pm.path.getMaxWayPoints());
            }

            client.SendMsg(json);
        }

        void SendCarLoaded()
        {
            Debug.Log("sendin car loaded");
            if (client == null)
                return;

            JSONObject json = new JSONObject(JSONObject.Type.OBJECT);
            json.AddField("msg_type", "car_loaded");
            client.SendMsg(json);
            Debug.Log("car loaded.");
        }

        void OnControlsRecv(JSONObject json)
        {
            try
            {
                ai_steering = float.Parse(json["steering"].str, CultureInfo.InvariantCulture.NumberFormat) * steer_to_angle;
                ai_throttle = float.Parse(json["throttle"].str, CultureInfo.InvariantCulture.NumberFormat);
                ai_brake = float.Parse(json["brake"].str, CultureInfo.InvariantCulture.NumberFormat);

                car.RequestSteering(ai_steering);
                car.RequestThrottle(ai_throttle);
                car.RequestFootBrake(ai_brake);
            }
            catch (Exception e)
            {
                Debug.Log(e.ToString());
            }
        }

        void OnExitSceneRecv(JSONObject json)
        {
            bExitScene = true;
        }

        void ExitScene()
        {
            SceneManager.LoadSceneAsync(0);
        }

        void OnResetCarRecv(JSONObject json)
        {
            bResetCar = true;
        }

        void OnRequestNewCarRecv(JSONObject json)
        {
            tk.JsonTcpClient client = null; //TODO where to get client?

            //We get this callback in a worker thread, but need to make mainthread calls.
            //so use this handy utility dispatcher from
            // https://github.com/PimDeWitte/UnityMainThreadDispatcher
            Debug.Log("Spawning new car in TcpCarHandler");
            UnityMainThreadDispatcher.Instance().Enqueue(SpawnNewCar(client));
        }

        IEnumerator SpawnNewCar(tk.JsonTcpClient client)
        {
            CarSpawner spawner = GameObject.FindObjectOfType<CarSpawner>();

            if (spawner != null)
            {
                spawner.Spawn(client);
            }

            yield return null;
        }

        void OnRegenRoad(JSONObject json)
        {
            //This causes the track to be regenerated with the given settings.
            //This only works in scenes that have random track generation enabled.
            float turn_increment = 0;
            string wayPointsString = json.GetField("wayPoints").str;

            string[] wayPoints = new string[0];
            if (!string.IsNullOrEmpty(wayPointsString))
            {
                wayPoints = wayPointsString.Split("@");
            }



            //We get this callback in a worker thread, but need to make mainthread calls.
            //so use this handy utility dispatcher from
            // https://github.com/PimDeWitte/UnityMainThreadDispatcher
            UnityMainThreadDispatcher.Instance().Enqueue(RegenRoad(turn_increment, wayPoints));
        }

        IEnumerator RegenRoad(float turn_increment, string[] wayPoints)
        {
            // TrainingManager train_mgr = GameObject.FindObjectOfType<TrainingManager>();
            // PathManager path_mgr = GameObject.FindObjectOfType<PathManager>();
            Debug.Log("Calling Regen Road");
            if (pm != null)
            {
                Debug.Log("Train manager is not null");
                if (turn_increment != 0.0 && pm != null)
                {
                    pm.turnInc = turn_increment;
                }

                RoadGen(wayPoints);
                // train_mgr.SetRoadStyle(road_style);
                // train_mgr.OnMenuRegenTrack();
            }
            else
            {
                Debug.Log("pm is null");
            }

            yield return null;
        }

        void RoadGen(string[] wayPoints)
        {
            Debug.Log("We start a new run in tcp client");
            car.RestorePosRot();
            if (wayPoints.Length > 1)
            {
                Debug.Log("We create a new road");
                pm.DestroyRoad();
                pm.InitNewRoad(wayPoints);
            }
            pm.path.ResetActiveSpan();

            car.RequestFootBrake(1);
        }

        void OnCarConfig(JSONObject json)
        {
            Debug.Log("Got car config message");

            string body_style = json.GetField("body_style").str;
            int body_r = int.Parse(json.GetField("body_r").str);
            int body_g = int.Parse(json.GetField("body_g").str);
            int body_b = int.Parse(json.GetField("body_b").str);
            string car_name = json.GetField("car_name").str;
            int font_size = 100;

            if (json.GetField("font_size") != null)
                font_size = int.Parse(json.GetField("font_size").str);

            if (carObj != null)
                UnityMainThreadDispatcher.Instance().Enqueue(SetCarConfig(body_style, body_r, body_g, body_b, car_name, font_size));
        }

        IEnumerator SetCarConfig(string body_style, int body_r, int body_g, int body_b, string car_name, int font_size)
        {
            CarConfig conf = carObj.GetComponent<CarConfig>();

            if (conf)
            {
                conf.SetStyle(body_style, body_r, body_g, body_b, car_name, font_size);
            }

            yield return null;
        }

        void OnCamConfig(JSONObject json)
        {
            float fov = float.Parse(json.GetField("fov").str, CultureInfo.InvariantCulture.NumberFormat);
            float offset_x = float.Parse(json.GetField("offset_x").str, CultureInfo.InvariantCulture.NumberFormat);
            float offset_y = float.Parse(json.GetField("offset_y").str, CultureInfo.InvariantCulture.NumberFormat);
            float offset_z = float.Parse(json.GetField("offset_z").str, CultureInfo.InvariantCulture.NumberFormat);
            float rot_x = float.Parse(json.GetField("rot_x").str, CultureInfo.InvariantCulture.NumberFormat);
            float fish_eye_x = float.Parse(json.GetField("fish_eye_x").str, CultureInfo.InvariantCulture.NumberFormat);
            float fish_eye_y = float.Parse(json.GetField("fish_eye_y").str, CultureInfo.InvariantCulture.NumberFormat);
            int img_w = int.Parse(json.GetField("img_w").str);
            int img_h = int.Parse(json.GetField("img_h").str);
            int img_d = int.Parse(json.GetField("img_d").str);
            string img_enc = json.GetField("img_enc").str;

            if (carObj != null)
                UnityMainThreadDispatcher.Instance().Enqueue(SetCamConfig(fov, offset_x, offset_y, offset_z, rot_x, img_w, img_h, img_d, img_enc, fish_eye_x, fish_eye_y));
        }

        IEnumerator SetCamConfig(float fov, float offset_x, float offset_y, float offset_z, float rot_x,
            int img_w, int img_h, int img_d, string img_enc, float fish_eye_x, float fish_eye_y)
        {
            CameraSensor camSensor = carObj.transform.GetComponentInChildren<CameraSensor>();

            if (camSensor)
            {
                camSensor.SetConfig(fov, offset_x, offset_y, offset_z, rot_x, img_w, img_h, img_d, img_enc);

                Fisheye fe = camSensor.gameObject.GetComponent<Fisheye>();

                if (fe != null && (fish_eye_x != 0.0f || fish_eye_y != 0.0f))
                {
                    fe.enabled = true;
                    fe.strengthX = fish_eye_x;
                    fe.strengthY = fish_eye_y;
                }
            }

            yield return null;
        }

        void OnStepModeRecv(JSONObject json)
        {
            string step_mode = json.GetField("step_mode").str;
            float _time_step = float.Parse(json.GetField("time_step").str);

            Debug.Log("got settings");

            if (step_mode == "synchronous")
            {
                Debug.Log("setting mode to synchronous");
                asynchronous = false;
                this.time_step = _time_step;
                Time.timeScale = 0.0f;
            }
            else
            {
                Debug.Log("setting mode to asynchronous");
                asynchronous = true;
            }
        }

        void OnQuitApp(JSONObject json)
        {
            // This needs to be called from the main thread.
            UnityMainThreadDispatcher.Instance().Enqueue(quitApp());
        }

        IEnumerator quitApp()
        {
            Application.Quit();
            yield return null;
        }

        // Update is called once per frame
        void Update()
        {
            if (bExitScene)
            {
                bExitScene = false;
                ExitScene();
            }

            if (state == State.SendTelemetry)
            {
                if (bResetCar)
                {
                    car.RestorePosRot();
                    pm.path.ResetActiveSpan();
                    bResetCar = false;
                }


                timeSinceLastCapture += Time.deltaTime;

                if (timeSinceLastCapture > 1.0f / limitFPS)
                {
                    timeSinceLastCapture -= (1.0f / limitFPS);
                    SendTelemetry();
                }

                if (ai_text != null)
                    ai_text.text = string.Format("NN: steering->{0} : throttle->{1}", ai_steering, ai_throttle);

            }
        }
    }
}
