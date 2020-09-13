public enum MessageType
{
    None = 0, 
    //Empty message. Prevents timeout from server.
    AssignID = 1, 
    //AssignID contains: uint playerID
    Message = 2, 
    //Messages contains: string64 message
    Join = 3, 
    //Join contains: (roomID?), uint playerID, string32 name, float r,g,b
    Position = 4,
    //Move contains: uint playerID, float x,y,z (absolute position)
    PlayerLeave = 5,
    //Player leave: uint playerID -> just know which player left. the rest is handled.
    Ping = 6,
    //Ping: ulong timestamp.
}