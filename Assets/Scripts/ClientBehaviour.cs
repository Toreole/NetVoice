using Unity.Networking.Transport;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class ClientBehaviour : MonoBehaviour
{
    public NetworkDriver networkDriver;
    public NetworkConnection connection;
    public bool isDone;

    [SerializeField]
    protected InputField addressInput, nameInput;
    [SerializeField]
    protected Button startButton;
    [SerializeField]
    protected Text msgDisplay, pingDisplay;
    [SerializeField]
    protected string playerName = "crewmate?";
    [SerializeField]
    protected Color playerColor = Color.red;

    [SerializeField]
    protected GameObject playerPrefab;


    Transform self;
    //Dictionary from the uint playerID to the Transform component of the player in the scene.
    Dictionary<uint, Transform> otherPlayers = new Dictionary<uint, Transform>();
    uint ID;

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
                Debug.Log("something went wrong during connect");
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
                        HandlePing(streamReader);
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
                Debug.Log("Disonnected from the server");
                connection = default(NetworkConnection);
            }
        }

        //temp move
        float inputX = Input.GetAxis("Horizontal");
        float inputY = Input.GetAxis("Vertical");
        self.Translate(new Vector3(inputX * Time.deltaTime, inputY * Time.deltaTime));

        if(!canSend)
            return;
        if(Time.time - lastPosSync >= 1f/positionSyncFrequency) //&& delta since last move != 0
        {
            SendPosition();
            lastPosSync = Time.time;
        }
    }

    ///<summary>
    ///handles the server pinging the player. just send an empty package back to ensure that connection is still there.
    ///</summary>
    void HandlePing(DataStreamReader streamReader)
    {
        ulong timestamp = streamReader.ReadULong();
        ulong msPing = Util.GetTimeMillis() - timestamp;
        pingDisplay.text = msPing.ToString() + "ms";

        var writer = networkDriver.BeginSend(connection);
        writer.WriteUInt((uint)MessageType.None);
        networkDriver.EndSend(writer);
    }

    ///<summary>
    ///Handle when a player leaves / disconnects.
    ///</summary>
    void HandlePlayerLeave(DataStreamReader streamReader)
    {
        uint playerID = streamReader.ReadUInt();
        GameObject op = otherPlayers[playerID].gameObject;
        otherPlayers.Remove(playerID);
        Destroy(op);
    }

    ///<summary>
    ///Handle the received chat message.
    ///</summary>
    void HandleMessage(DataStreamReader streamReader)
    {
        var messageContent = streamReader.ReadFixedString64();
        msgDisplay.text = $"<Server>: \"{messageContent.ToString()}\"";
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

        otherPlayers.Add(playerID, newPlayer.transform);
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

        //instantiate self.
        GameObject localInstance = Instantiate(playerPrefab);
        localInstance.GetComponentInChildren<Text>().text = nameInput.text ?? playerName;
        localInstance.GetComponent<SpriteRenderer>().color = playerColor;
        self = localInstance.transform;
    }

    ///<summary>
    ///Send the players current position via the network.
    ///</summary>
    void SendPosition()
    {
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
        otherPlayers[pID].position = position;
    }
}
