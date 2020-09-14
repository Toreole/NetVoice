using Unity.Networking.Transport;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Collections;

public class ClientBehaviour : MonoBehaviour
{
    public NetworkDriver networkDriver;
    public NetworkConnection connection;
    public bool isDone;

    [SerializeField]
    protected InputField addressInput, nameInput, chatInput;
    [SerializeField]
    protected Button startButton;
    [SerializeField]
    protected Text pingDisplay;
    [SerializeField]
    protected Text chat;
    [SerializeField]
    protected ScrollRect chatBoxScroll;
    [SerializeField]
    protected string playerName = "crewmate?";
    [SerializeField]
    protected Color playerColor = Color.red;

    [SerializeField]
    protected GameObject playerPrefab;


    Transform self;
    //Dictionary from the uint playerID to the Transform component of the player in the scene.
    Dictionary<uint, NetworkPlayer> otherPlayers = new Dictionary<uint, NetworkPlayer>();
    Dictionary<uint, Coroutine> positionLerpRoutines = new Dictionary<uint, Coroutine>();
    uint ID;

    ulong lastPingSent = 0;
    //the last time a ping was sent out to the players.
    float lastPing = 0;
    [SerializeField]
    protected float pingInterval = 2.5f;

    private bool isStarted = false, canSend = false;

    float lastPosSync = 0;
    [SerializeField]
    protected float positionSyncFrequency = 5;

    public void StartClient()
    {
        if(addressInput.text == "")
            return;

        if( NetworkEndPoint.TryParse(addressInput.text, 9000, out NetworkEndPoint endpoint))
        {
            networkDriver = NetworkDriver.Create();
            connection = default(NetworkConnection);
            connection = networkDriver.Connect(endpoint);
            startButton.interactable = false;
            isStarted = true;
        }
    }

    public void StopClient()
    {
        connection.Disconnect(networkDriver);
        connection = default(NetworkConnection);
    }


    //Dispose the unmanaged
    private void OnDestroy() 
    {
        networkDriver.Dispose();    
    }

    // Update is called once per frame
    void Update()
    {
        if(!isStarted)
            return;
        
        networkDriver.ScheduleUpdate().Complete();

        //if no connection was established do some stuff.
        if(!connection.IsCreated)
        {
            if(!isDone)
                Debug.Log("something went wrong during connect"); //OR we just disconnected and need to handle it somehow.
            return;
        }

        DataStreamReader streamReader;
        NetworkEvent.Type cmd;
        //handle all the events currently stored.
        while((cmd = connection.PopEvent(networkDriver, out streamReader)) != NetworkEvent.Type.Empty)
        {
            if(cmd == NetworkEvent.Type.Connect)
            {
                Debug.Log("Connected to the server");
                canSend = true;
            }
            else if(cmd == NetworkEvent.Type.Data)
            {
                MessageType type = (MessageType) streamReader.ReadUInt();
                switch(type)
                {
                    case MessageType.Message:
                        HandleMessage(streamReader);
                        break;
                    case MessageType.AssignID:
                        ID = streamReader.ReadUInt();
                        SendPlayerInfo();
                        break;
                    case MessageType.Join:
                        HandlePlayerJoin(streamReader);
                        break;
                    case MessageType.Ping:
                        HandlePing();
                        break;
                    case MessageType.Position:
                        HandlePositionData(streamReader);
                        break;
                    case MessageType.PlayerLeave:
                        HandlePlayerLeave(streamReader);
                        break;
                }
            }
            else if(cmd == NetworkEvent.Type.Disconnect)
            {
                //Debug.Log("Disonnected from the server");
                //TODO: add error code (timeout???)
                chat.text += "-- Disconnected from Server. --\n"; //TODO: future: go back to menu scene or something to avoid getting a million debug logs.
                chatBoxScroll.verticalNormalizedPosition = 0;
                connection = default(NetworkConnection);
            }
        }

        //temp move
        float inputX = Input.GetAxis("Horizontal");
        float inputY = Input.GetAxis("Vertical");
        if(self)
            self.Translate(new Vector3(inputX * Time.deltaTime, inputY * Time.deltaTime));

        if(!canSend)
            return;
        if(Time.time - lastPosSync >= 1f/positionSyncFrequency) //&& delta since last move != 0
        {
            SendPosition();
            lastPosSync = Time.time;
        }
        if(Time.time - lastPing >= pingInterval)
        {
            lastPing = Time.time;
            PingServer();
        }
    }

    ///<summary>
    ///this will ping the server and save the current time offset. which is used for actual ping (ms) calculation.
    ///</summary>
    void PingServer()
    {
        var writer = networkDriver.BeginSend(connection);
        writer.WriteUInt((uint)MessageType.Ping);
        networkDriver.EndSend(writer);
        lastPingSent = Util.GetTimeMillis();
    }

    ///<summary>
    ///handles the server pinging the player. just send an empty package back to ensure that connection is still there.
    ///</summary>
    void HandlePing()
    {
        ulong timestamp = Util.GetTimeMillis();
        ulong msPing = (timestamp - lastPingSent) / 2;
        pingDisplay.text = msPing.ToString() + "ms";
    }

    ///<summary>
    ///Handle when a player leaves / disconnects.
    ///</summary>
    void HandlePlayerLeave(DataStreamReader streamReader)
    {
        uint playerID = streamReader.ReadUInt();
        GameObject op = otherPlayers[playerID].transform.gameObject;
        otherPlayers.Remove(playerID);
        Destroy(op);
    }

    ///<summary>
    ///Sends the message you just typed. (if enter was pressed)
    ///</summary>
    public void SendChatMessage(string inputMessage)
    {
        if(!Input.GetKey(KeyCode.Return))
            return;
        chatInput.text = "";

        chat.text += $"<{playerName}>:{inputMessage}\n";
        chatBoxScroll.verticalNormalizedPosition = 0;

        var writer = networkDriver.BeginSend(connection);
        writer.WriteUInt((uint)MessageType.Message);
        writer.WriteUInt(ID);
        writer.WriteFixedString64(inputMessage);
        networkDriver.EndSend(writer);
    }

    ///<summary>
    ///Handle the received chat message.
    ///</summary>
    void HandleMessage(DataStreamReader streamReader)
    {
        //playername: message
        chat.text += $"<{otherPlayers[streamReader.ReadUInt()].name}>:{streamReader.ReadFixedString64()}\n";
        chatBoxScroll.verticalNormalizedPosition = 0;
        //Debug.Log($"Received message: {messageContent.ToString()}");
    }

    ///<summary>
    ///Handle another player joining the game.
    ///</summary>
    void HandlePlayerJoin(DataStreamReader streamReader)
    {
        //Join Message = uint playerID, string32 name, float r,g,b
        var newPlayer = Instantiate(playerPrefab);
        uint playerID = streamReader.ReadUInt();
        string otherPlayerName = streamReader.ReadFixedString32().ToString();
        float red = streamReader.ReadFloat();
        float green = streamReader.ReadFloat();
        float blue = streamReader.ReadFloat();
        //set up the player with the received data.
        newPlayer.GetComponent<SpriteRenderer>().color = new Color(red, green, blue, 1);
        newPlayer.GetComponentInChildren<Text>().text = otherPlayerName;

        var playerData = new NetworkPlayer()
        {
            transform = newPlayer.transform,
            name = otherPlayerName
        };
        //Debug.Log($"Received info on player: {playerID}");
        otherPlayers.Add(playerID, playerData);
    }

    ///<summary>
    ///Send the basic info about this player. ID, Name, Colour
    ///</summary>
    void SendPlayerInfo()
    {
        var writer = networkDriver.BeginSend(connection);
        //tell the server that this player is just joining
        writer.WriteUInt((uint)MessageType.Join);
        //the ID given by the server.
        writer.WriteUInt(ID);
        //custom player name.
        writer.WriteFixedString32(nameInput.text ?? playerName);
        //custom player colour.
        writer.WriteFloat(playerColor.r);
        writer.WriteFloat(playerColor.g);
        writer.WriteFloat(playerColor.b);
        //send it.
        networkDriver.EndSend(writer);

        if(!string.IsNullOrEmpty(nameInput.text))
            playerName = nameInput.text;

        //instantiate self.
        GameObject localInstance = Instantiate(playerPrefab);
        localInstance.GetComponentInChildren<Text>().text = nameInput.text ?? playerName;
        localInstance.GetComponent<SpriteRenderer>().color = playerColor;
        self = localInstance.transform;
        Camera.main.transform.SetParent(self);
    }

    ///<summary>
    ///Send the players current position via the network.
    ///</summary>
    void SendPosition()
    {
        if(!self)
            return;
        var writer = networkDriver.BeginSend(connection);
        writer.WriteUInt((uint)MessageType.Position);
        var position = self.position;
        writer.WriteUInt(ID);
        writer.WriteFloat(position.x);
        writer.WriteFloat(position.y);
        writer.WriteFloat(position.z);
        networkDriver.EndSend(writer);
    }

    ///<summary>
    ///Handle getting the position data of another player.
    ///</summary>
    void HandlePositionData(DataStreamReader reader)
    {
        uint pID = reader.ReadUInt();
        Vector3 position = new Vector3()
        {
            x = reader.ReadFloat(),
            y = reader.ReadFloat(),
            z = reader.ReadFloat()
        };
        //Debug.Log($"Received position for: {pID} at {position.x}|{position.y}");
        //otherPlayers[pID].position = position;
        if(positionLerpRoutines.TryGetValue(pID, out Coroutine runningRoutine))
            StopCoroutine(runningRoutine);
        if(otherPlayers.TryGetValue(pID, out NetworkPlayer other))
            positionLerpRoutines[pID] = StartCoroutine(LerpPosition(other.transform, position));
    }

    ///<summary>
    ///Interpolate the position of the other player towards the new one. This causes other player to lag behind by (ping1 + ping2 + 1/frequency) seconds.
    ///</summary>
    IEnumerator LerpPosition(Transform player, Vector3 targetPosition)
    {
        float lerpTime = 1f / positionSyncFrequency;
        Vector3 startPosition = player.position;

        for(float t = 0; t < lerpTime; t += Time.deltaTime)
        {
            player.position = Vector3.Lerp(startPosition, targetPosition, t / lerpTime);
            yield return null;
        }
    }

    class NetworkPlayer
    {
        public Transform transform;
        public string name;
    } 
}
