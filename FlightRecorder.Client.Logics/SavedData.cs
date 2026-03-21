using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace FlightRecorder.Client.Logics;

// --- Oject models for Replay Logic
public class AircraftRecord
{
    public long milliseconds;
    public AircraftPositionStruct? position;
}

public class AiAircraftRecord
{
    public long milliseconds;
    public AiAircraftPositionStruct? position;
}


// Store the event send every minutes telling if any aircraft is here
public class AircraftStatus
{
    [JsonConstructor]
    public AircraftStatus (long milliseconds, SimStateStruct? position)
    {
        this.milliseconds = milliseconds;
        this.position = position;
    }

    public AircraftStatus () { }

    public long milliseconds { get; set; }
    public SimStateStruct? position { get; set; }

}



public class UserAircraft
{
    public uint UserArcraftID;
    public SimStateStruct? SimState;
    public List<AircraftRecord>? Records;
    public long StartTime;
    public long EndTime;
}
public class AiAircraft
{
    public uint ObjectID;              // Store the aircraft object ID at recording time
    public SimStateStruct? AircraftStatus;
    public List<AiAircraftRecord>? Records;
    public List<AircraftStatus>? Minutes;
    public int StartIndex;
    public int StopIndex;

    public uint? aiRequestId;       // Store the Request for aircraft spawn
    public uint? aiId;              // Store the Aircraft Object ID upon replay

}

// --- Oject models for Save logic
public class SavedData
{

    // Aircrafts are order the same way on the different lists
    public SavedData(string ClientVersion,  UserAircraft userAircraft, List<AiAircraft> aiAircraftList )
    {
        this.ClientVersion = ClientVersion;

        // Convert AI aircrafts
        AiAircraftList = new List<AiAircraftForSave>();
        int i = 0;
        foreach (var aircraft in aiAircraftList)
        {
            if ((aircraft.Records != null) && (aircraft.Records.Count > 0))
            {
                List<AiSavedRecord> mylist = new List<AiSavedRecord>();
                
                for (int j = 0; j < aircraft.Records.Count; j++) 
                {
                    var toto4 = new AiSavedRecord(aircraft.Records[j].milliseconds, aircraft.Records[j].position != null ? AiAircraftPosition.FromStruct((AiAircraftPositionStruct)aircraft.Records[j].position) : null);
                    mylist.Add(toto4);
                }

                SimState? lstate = aircraft.AircraftStatus == null ? null : SimState.FromStruct((SimStateStruct)aircraft.AircraftStatus);

                AiAircraftForSave myAiForSave = new AiAircraftForSave {  objectID = aircraft.ObjectID, Records = mylist, Minutes = aircraft.Minutes, StartIndex = aircraft.StartIndex, StopIndex = aircraft.StopIndex,  AircraftStatus = lstate };
                AiAircraftList.Add(myAiForSave);
            }

            i++;
        }

        // Convert User Aircraft
        List<SavedRecord> mylist2 = new List<SavedRecord>();
        if ((userAircraft.Records != null) && (userAircraft.Records.Count > 0))
        {
            for (int j = 0; j < userAircraft.Records.Count; j++)
            {
                var toto4 = new SavedRecord(userAircraft.Records[j].milliseconds, userAircraft.Records[j].position != null ? AircraftPosition.FromStruct((AircraftPositionStruct)userAircraft.Records[j].position) : null);
                mylist2.Add(toto4);
            }
        }
        
        SimState? uStartState = userAircraft.SimState.HasValue ? SimState.FromStruct(userAircraft.SimState.Value) : null;

        UserArcraft = new UserAircraftForSave { UserArcraftID = userAircraft.UserArcraftID, StartTime = userAircraft.StartTime, SimState = uStartState, Records = mylist2, EndTime = userAircraft.EndTime };
    }


    /*
    public SavedData(string clientVersion,  SimStateStruct? simState, UserAircraftForSave userArcraft, List<AiAircraftForSave> aircraftList)
    {
        ClientVersion = clientVersion;
        StartTime = startTime;
        EndTime = endTime;
        StartState = simState.HasValue ? SimState.FromStruct(simState.Value) : null;
        Records = records_reorganised;
        AircraftList = aircraftList;
        Records = records_reorganised;
        Minute_status = minutes_reoganised;
        UserArcraftID = userArcraftID;
        Index_range = index_range;
    }
    */

    [JsonConstructor]
    public SavedData(string ClientVersion, UserAircraftForSave UserArcraft, List<AiAircraftForSave> AiAircraftList)
    {
        this.ClientVersion = ClientVersion;
        this.UserArcraft = UserArcraft;
        this.AiAircraftList = AiAircraftList;
    }

    public string ClientVersion { get; set; }

    public UserAircraftForSave UserArcraft { get; set; }

    public List<AiAircraftForSave> AiAircraftList { get; set; }


    public class UserAircraftForSave
    {
        [JsonConstructor]
        public UserAircraftForSave(uint UserArcraftID, SimState? SimState, List<SavedRecord>? Records, long StartTime, long EndTime)
        {
            this.UserArcraftID = UserArcraftID;
            this.SimState = SimState;
            this.Records = Records;
            this.StartTime = StartTime;
            this.EndTime = EndTime;
        }

        public UserAircraftForSave() { }

        public uint UserArcraftID { get; set; }
        public SimState? SimState { get; set; }
        public List<SavedRecord>? Records { get; set; }
        public long StartTime { get; set; }
        public long EndTime { get; set; }
    }
    public class AiAircraftForSave
    {
        [JsonConstructor]
        public AiAircraftForSave(uint objectID, SimState? AircraftStatus, List<AiSavedRecord>? Records, List<AircraftStatus>? Minutes, int StartIndex, int StopIndex)
        {
            this.objectID = objectID;
            this.AircraftStatus = AircraftStatus;
            this.Records = Records;
            this.Minutes = Minutes;
            this.StartIndex = StartIndex;
            this.StopIndex = StopIndex;
        }

        public AiAircraftForSave () { }

        public uint objectID { get; set; }
        public SimState? AircraftStatus { get; set; }
        public List<AiSavedRecord>? Records { get; set; }
        public List<AircraftStatus>? Minutes { get; set; }
        public int StartIndex { get; set; }
        public int StopIndex { get; set; }  
    }

    public class SavedRecord
    {
        [JsonConstructor]
        public SavedRecord(long Time, AircraftPosition? Position)
        {
            this.Time = Time;
            this.Position = Position;
        }

        public long Time { get; set; }
        public AircraftPosition? Position { get; set; }
    }

    public class AiSavedRecord
    {
        [JsonConstructor]
        public AiSavedRecord(long Time, AiAircraftPosition? Position)
        {
            this.Time = Time;
            this.Position = Position;
        }

        public long Time { get; set; }
        public AiAircraftPosition? Position { get; set; }
    }
}
