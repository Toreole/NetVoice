using UnityEngine.Assertions;
using UnityEngine;
using System.Collections.Generic;

using Unity.Collections;
using Unity.Networking.Transport;
using UnityEngine.UI;

public class ServerBehaviour : MonoBehaviour
{
    public NetworkDriver networkDriver;
    private NativeList<NetworkConnection> connections;
    [SerializeField]
    protected Text addressText;
    [SerializeField]
    protected InputField inputField;

    uint nextID = 1;

    //the last time a ping was sent out to the players.
    float lastPing = 0;
    [SerializeField]
    protected float pingInterval = 2.5f;

    List<PlayerInfo> connectedPlayers = new List<PlayerInfo>();

    // Start is called before the first frame update
    void Start()
    {
        networkDriver = NetworkDriver.Create();
        var endpoint = NetworkEndPoint.AnyIpv4;
        endpoint.Port = 9000;
        if(networkDriver.Bind(endpoint) != 0)
            Debug.Log("Failed to bind to port 9000");
        else 
            networkDriver.Listen();

        addressText.text = $"hosting server on {endpoint.Address}";

        connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);
    }

    //Dispose of the stuff manually because its unmanaged.
    private void OnDestroy() 
    {
        networkDriver.Dispose();
        connections.Dispose();    
    }

    public void SendMessageToAllClients()
    {
        string messageRaw = inputField.text;
        inputField.text = "";
        FixedString64 message = messageRaw;

        for(int i = 0; i < connections.Length; i++)
        {
            if(!connections[i].IsCreated)
                continue;
            
            SendMessage(connections[i], message);
        }
    }

    void SendMessage(NetworkConnection connection, FixedString64 message)
    {
        //let the networkDriver start.
        DataStreamWriter streamWriter = networkDriver.BeginSend(NetworkPipeline.Null, connection);
        //write relevant data
        streamWriter.WriteUInt((uint)MessageType.Message);
        streamWriter.WriteFixedString64(message);
        //finish sending.
        networkDriver.EndSend(streamWriter);
    }

    // Update is called once per frame
    void Update()
    {
        //update the network driver. since this is a Job it needs the Complete() extra to ensure it runs before we continue.
        networkDriver.ScheduleUpdate().Complete();

        //clean up old connections before accepting new ones.
        for(int i = 0; i < connections.Length; i++)
        {
            if(!connections[i].IsCreated)
            {
                connections.RemoveAtSwapBack(i);
                i--;
            }
        }

        //now accept new connections.
        NetworkConnection connection;
        //while new connections are valid (not the default) add them to the list.
        while((connection = networkDriver.Accept()) != default(NetworkConnection))
        {
            connections.Add(connection);
            Debug.Log("Accepted a connection");
            AssignID(connection);
        }

        //Read data sent from the connections.
        DataStreamReader streamReader;
        for(int i = 0; i < connections.Length; i++)
        {
            if(!connections[i].IsCreated)
                continue;
            NetworkEvent.Type cmd;
            //handle all events for each connection
            while((cmd = networkDriver.PopEventForConnection(connections[i], out streamReader)) != NetworkEvent.Type.Empty)
            {
                if(cmd == NetworkEvent.Type.Data)
                {
                    //TODO: handle data incoming from client.
                    var messageType = (MessageType)streamReader.ReadUInt();
                    switch(messageType)
                    {
                        case MessageType.Join:
                            HandlePlayerJoin(streamReader, connections[i]);
                            break;
                        case MessageType.Position:
                            HandlePositionData(streamReader, connections[i]);
                            break;
                    }
                }
                else if(cmd == NetworkEvent.Type.Disconnect)
                {
                    //dirty but should work.
                    PlayerInfo disconnectPlayer = connectedPlayers.Find(x=> x.connection == connections[i]);
                    connectedPlayers.Remove(disconnectPlayer);
                    HandlePlayerDisconnect(disconnectPlayer.iD);
                    Debug.Log("Client disconnected from server");
                    connections[i] = default(NetworkConnection);
                }

            }
        }

        //ping all players if necessary.
        if(Time.time - lastPing > pingInterval)
        {
            for(int i = 0; i < connections.Length; i++)
            {
                if(!connections[i].IsCreated)
                    continue;
                Ping(connections[i]);
            }
            lastPing = Time.time;
        }
    }

    ///<summary>
    ///Handles the disconnecting of a player.
    ///</summary>
    void HandlePlayerDisconnect(uint disconnectID)
    {
        foreach(PlayerInfo player in connectedPlayers)
        {
            var writer = networkDriver.BeginSend(NetworkPipeline.Null, player.connection);
            writer.WriteUInt((uint)MessageType.PlayerLeave);
            writer.WriteUInt(disconnectID);
            networkDriver.EndSend(writer);
        }
    }

    ///<summary>
    ///Sends out a *ping* to the player on this connection.
    ///</summary>
    void Ping(NetworkConnection connection)
    {
        var writer = networkDriver.BeginSend(NetworkPipeline.Null, connection);
        writer.WriteUInt((uint)MessageType.Ping);
        writer.WriteULong(Util.GetTimeMillis());
        networkDriver.EndSend(writer);
    }

    ///<summary>
    ///Forward the position of one player to all others.
    ///</summary>
    void HandlePositionData(DataStreamReader streamReader, NetworkConnection sender)
    {
        //Get the data.
        uint playerID = streamReader.ReadUInt();
        Vector3 pos = new Vector3()
        {
            x = streamReader.ReadFloat(),
            y = streamReader.ReadFloat(),
            z = streamReader.ReadFloat()
        };
        //Send it
        for(int i = 0; i < connections.Length; i++)
        {
            if(!connections[i].IsCreated || connections[i] == sender)
                continue;
            
            var writer = networkDriver.BeginSend(connections[i]);
            writer.WriteUInt((uint)MessageType.Position);
            writer.WriteUInt(playerID);
            writer.WriteFloat(pos.x);
            writer.WriteFloat(pos.y);
            writer.WriteFloat(pos.z);
            networkDriver.EndSend(writer);
        }
    }
        

    ///<summary>
    ///Passes the info of the joining player on to the other players and tracks info.
    ///</summary>
    void HandlePlayerJoin(DataStreamReader streamReader, NetworkConnection sender)
    {
        PlayerInfo joinPlayer = new PlayerInfo()
        {
            connection = sender,
            iD = streamReader.ReadUInt(),
            name = streamReader.ReadFixedString32().ToString(),
            colour = new Color(streamReader.ReadFloat(), streamReader.ReadFloat(), streamReader.ReadFloat(), 1)
        };
        
        //notify all other players that a new one joined.
        for(int i = 0; i < connections.Length; i++)
        {
            if(!connections[i].IsCreated || connections[i] == sender)
                continue;
            var writer = networkDriver.BeginSend(NetworkPipeline.Null, connections[i]);
            writer.WriteUInt((uint) MessageType.Join);
            //playerID
            writer.WriteUInt(joinPlayer.iD);
            //playername
            writer.WriteFixedString32(joinPlayer.name);
            //colour
            writer.WriteFloat(joinPlayer.colour.r);
            writer.WriteFloat(joinPlayer.colour.g);
            writer.WriteFloat(joinPlayer.colour.b);
            networkDriver.EndSend(writer);
        }
        //send info on all existing players.
        foreach(PlayerInfo player in connectedPlayers)
        {
            var writer = networkDriver.BeginSend(NetworkPipeline.Null, sender);
            writer.WriteUInt((uint) MessageType.Join);
            writer.WriteUInt(player.iD);
            writer.WriteFixedString32(player.name);
            writer.WriteFloat(player.colour.r);
            writer.WriteFloat(player.colour.g);
            writer.WriteFloat(player.colour.b);
            networkDriver.EndSend(writer);
        }

        connectedPlayers.Add(joinPlayer);
    }

    ///<summary>
    ///Assigns a unique uint ID to the player on this connection.
    ///</summary>
    void AssignID(NetworkConnection connection)
    {
        var writer = networkDriver.BeginSend(NetworkPipeline.Null, connection);
        writer.WriteUInt((uint)MessageType.AssignID);
        writer.WriteUInt(nextID);
        nextID++;
        networkDriver.EndSend(writer);
    }

    struct PlayerInfo
    {
        public NetworkConnection connection;
        public string name;
        public uint iD;
        public Color colour;
    }
}
