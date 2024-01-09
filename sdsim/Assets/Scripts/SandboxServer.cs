using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using tk;
using System.Net;
using System.Net.Sockets;
using System;

[RequireComponent(typeof(tk.TcpServer))]
public class SandboxServer : MonoBehaviour
{
    public string host = "0.0.0.0";
    public int port = 9090;

    tk.TcpServer _server = null;

    public GameObject clientTemplateObj = null;
    public Transform spawn_pt;
    public bool spawnCarswClients = true;
    public bool autoStart = true;
    public SceneLoader loader;


    public void CheckCommandLineConnectArgs()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--host")
            {
                host = args[i + 1];
            }
            else if (args[i] == "--port")
            {
                port = int.Parse(args[i + 1]);
            }
            // else if (args[i] == "--logDir")
            // {
            //     Application.dataPath = Application.dataPath + "/../log/";
            // }
        }
    }

    private void Awake()
    {
        _server = GetComponent<tk.TcpServer>();
    }

    // Start is called before the first frame update
    void Start()
    {
        CheckCommandLineConnectArgs();

        Debug.Log("SDSandbox Server starting.");
        _server.onClientConntedCB += new tk.TcpServer.OnClientConnected(OnClientConnected);
        _server.onClientDisconntedCB += new tk.TcpServer.OnClientDisconnected(OnClientDisconnected);

        _server.Run(host, port);
    }

    // It's our responsibility to create a GameObject with a TcpClient
    // and return it to the server.
    public tk.TcpClient OnClientConnected()
    {
        if (clientTemplateObj == null)
        {
            Debug.LogError("client template object was null.");
            return null;
        }

        if (_server.debug)
            Debug.Log("creating client obj");

        GameObject go = GameObject.Instantiate(clientTemplateObj) as GameObject;

        go.transform.parent = this.transform;

        if (spawn_pt != null)
            go.transform.position = spawn_pt.position + UnityEngine.Random.insideUnitSphere * 2;

        tk.TcpClient client = go.GetComponent<tk.TcpClient>();
        Debug.Log("client connected: " + client);
        InitClient(client);

        return client;
    }

    private void InitClient(tk.TcpClient client)
    {
        if (autoStart)
        {
            Debug.Log("auto starting client.");
            // Instruct the scene loader to load the generated track scene.
            SceneLoader loader = GameObject.FindObjectOfType<SceneLoader>();

            loader.LoadGenerateRoadScene();
            // use the menu handler to select OnUseNNNetworkSteering.
            MenuHandler menuHandler = GameObject.FindObjectOfType<MenuHandler>();
            if (menuHandler)
                menuHandler.OnUseNNNetworkSteering();
            else
                Debug.Log("menu handler was null.");
            Debug.Log("auto started client.");
            autoStart = false;
        }
        else if (spawnCarswClients)
        {
            // active NN steering.
            MenuHandler menuHandler = GameObject.FindObjectOfType<MenuHandler>();
            if (menuHandler)
                menuHandler.OnUseNNNetworkSteering();
            else
                Debug.LogError("menu handler was null.");
            
            Debug.Log("spawning car with client.");
            CarSpawner spawner = GameObject.FindObjectOfType<CarSpawner>();
            // We want to make sure that only one car exists in the game.
            if (spawner)
            {
                if (_server.debug)
                    Debug.Log("spawning car.");
                spawner.RemoveAllCars();

                spawner.Spawn(client.gameObject.GetComponent<tk.JsonTcpClient>());
            }
        }
        else
        {
            Debug.Log("not spawning car with client.");
            //we are in the front end.
            tk.TcpMenuHandler handler = GameObject.FindObjectOfType<TcpMenuHandler>();

            if (handler)
            {
                if (_server.debug)
                    Debug.Log("init menu handler.");

                handler.Init(client.gameObject.GetComponent<tk.JsonTcpClient>());
            }
        }
    }


    public void OnSceneLoaded(bool bFrontEnd)
    {
        spawnCarswClients = !bFrontEnd;

        List<tk.TcpClient> clients = _server.GetClients();

        foreach (tk.TcpClient client in clients)
        {
            if (_server.debug)
                Debug.Log("init network client.");

            InitClient(client);
        }

        if (GlobalState.bCreateCarWithoutNetworkClient && !bFrontEnd && clients.Count == 0)
        {
            CarSpawner spawner = GameObject.FindObjectOfType<CarSpawner>();

            if (spawner)
            {
                if (_server.debug)
                    Debug.Log("spawning car.");
                Debug.Log("spawning car without network client.");
                spawner.Spawn(null);
            }
        }
    }

    public void OnClientDisconnected(tk.TcpClient client)
    {
        CarSpawner spawner = GameObject.FindObjectOfType<CarSpawner>();

        if (spawner)
        {
            spawner.RemoveCar(client.gameObject.GetComponent<tk.JsonTcpClient>());
        }

        GameObject.Destroy(client.gameObject);
    }
}
